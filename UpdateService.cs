using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Threading.Tasks;
using System.Threading;

namespace ComputerTestApp
{
    internal static class UpdateService
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/mhqb365/ComputerTestApp/releases/latest";

        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

        public static string DisplayVersion =>
            $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        public static async Task<UpdateCheckResult> CheckLatestReleaseAsync()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ComputerTestApp");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                using (var response = await client.GetAsync(LatestReleaseUrl))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return UpdateCheckResult.NoRelease();
                    }

                    response.EnsureSuccessStatusCode();

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                        var release = (GitHubRelease)serializer.ReadObject(stream);
                        var latestVersion = ParseVersion(release.TagName ?? release.Name);

                        if (latestVersion == null)
                        {
                            return UpdateCheckResult.UnknownVersion(release);
                        }

                        return latestVersion > NormalizeVersion(CurrentVersion)
                            ? UpdateCheckResult.UpdateAvailable(release, latestVersion)
                            : UpdateCheckResult.UpToDate(release, latestVersion);
                    }
                }
            }
        }

        public static async Task<PreparedUpdate> DownloadAndPrepareUpdateAsync(
            GitHubRelease release,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            var asset = FindUpdateAsset(release);
            if (asset == null)
            {
                throw new InvalidOperationException(LocalizationService.Get("NoUpdateAssetMessage"));
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), $"computer-test-update-{Guid.NewGuid():N}");
            var zipPath = Path.Combine(tempRoot, asset.Name);
            var extractPath = Path.Combine(tempRoot, "extracted");

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(extractPath);

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("ComputerTestApp");
                    
                    // Request headers only first to get content length
                    using (var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        
                        using (var input = await response.Content.ReadAsStreamAsync())
                        using (var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            var totalRead = 0L;
                            int read;
                            
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await output.WriteAsync(buffer, 0, read, cancellationToken);
                                totalRead += read;
                                
                                if (totalBytes != -1)
                                {
                                    var percentage = (int)((totalRead * 100) / totalBytes);
                                    progress?.Report(new UpdateProgressInfo(UpdateProgressState.Downloading, percentage));
                                }
                            }
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new UpdateProgressInfo(UpdateProgressState.Extracting, 100));

                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(zipPath, extractPath);
                }, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new UpdateProgressInfo(UpdateProgressState.Preparing, 100));

                return new PreparedUpdate(GetInstallSourceDirectory(extractPath), tempRoot);
            }
            catch (Exception)
            {
                // Clean up temp directory on failure/cancellation
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, true);
                    }
                }
                catch (Exception)
                {
                    // Ignore cleanup errors to throw original exception
                }
                throw;
            }
        }

        public static void InstallPreparedUpdate(PreparedUpdate update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));

            var appDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var appExe = Assembly.GetExecutingAssembly().Location;
            var scriptPath = Path.Combine(update.TempRoot, "install-update.ps1");
            var script = $@"
$ErrorActionPreference = 'Stop'
$source = '{EscapePowerShellString(update.SourceDirectory)}'
$target = '{EscapePowerShellString(appDirectory)}'
$exe = '{EscapePowerShellString(appExe)}'
$temp = '{EscapePowerShellString(update.TempRoot)}'
$pidToWait = {Process.GetCurrentProcess().Id}
Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
Start-Process -FilePath $exe -WorkingDirectory $target
Start-Sleep -Seconds 2
Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
";
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = appDirectory
            });

            Application.Current.Shutdown();
        }

        private static GitHubReleaseAsset FindUpdateAsset(GitHubRelease release)
        {
            if (release?.Assets == null) return null;

            foreach (var asset in release.Assets)
            {
                if (string.IsNullOrWhiteSpace(asset?.DownloadUrl)) continue;
                if (asset.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return asset;
                }
            }

            return null;
        }

        private static string GetInstallSourceDirectory(string extractPath)
        {
            var rootFiles = Directory.GetFiles(extractPath);
            var rootDirectories = Directory.GetDirectories(extractPath);
            if (rootFiles.Length == 0 && rootDirectories.Length == 1)
            {
                return rootDirectories[0];
            }

            return extractPath;
        }

        private static string EscapePowerShellString(string value)
        {
            return value.Replace("'", "''");
        }

        private static Version ParseVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var match = Regex.Match(value, @"\d+(?:\.\d+){0,3}");
            return match.Success && Version.TryParse(match.Value, out var version)
                ? NormalizeVersion(version)
                : null;
        }

        private static Version NormalizeVersion(Version version)
        {
            return new Version(
                Math.Max(version.Major, 0),
                Math.Max(version.Minor, 0),
                Math.Max(version.Build, 0),
                Math.Max(version.Revision, 0));
        }
    }

    internal sealed class UpdateCheckResult
    {
        private UpdateCheckResult(
            UpdateCheckStatus status,
            GitHubRelease release = null,
            Version latestVersion = null)
        {
            Status = status;
            Release = release;
            LatestVersion = latestVersion;
        }

        public UpdateCheckStatus Status { get; }
        public GitHubRelease Release { get; }
        public Version LatestVersion { get; }

        public string DisplayLatestVersion =>
            LatestVersion != null
                ? (LatestVersion.Revision > 0
                    ? $"{LatestVersion.Major}.{LatestVersion.Minor}.{LatestVersion.Build}.{LatestVersion.Revision}"
                    : $"{LatestVersion.Major}.{LatestVersion.Minor}.{LatestVersion.Build}")
                : string.Empty;

        public static UpdateCheckResult NoRelease() =>
            new UpdateCheckResult(UpdateCheckStatus.NoRelease);

        public static UpdateCheckResult UnknownVersion(GitHubRelease release) =>
            new UpdateCheckResult(UpdateCheckStatus.UnknownVersion, release);

        public static UpdateCheckResult UpToDate(GitHubRelease release, Version latestVersion) =>
            new UpdateCheckResult(UpdateCheckStatus.UpToDate, release, latestVersion);

        public static UpdateCheckResult UpdateAvailable(GitHubRelease release, Version latestVersion) =>
            new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release, latestVersion);
    }

    internal enum UpdateCheckStatus
    {
        NoRelease,
        UnknownVersion,
        UpToDate,
        UpdateAvailable
    }

    [DataContract]
    internal sealed class GitHubRelease
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "html_url")]
        public string HtmlUrl { get; set; }

        [DataMember(Name = "assets")]
        public GitHubReleaseAsset[] Assets { get; set; }
    }

    [DataContract]
    internal sealed class GitHubReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string DownloadUrl { get; set; }
    }

    internal sealed class PreparedUpdate
    {
        public PreparedUpdate(string sourceDirectory, string tempRoot)
        {
            SourceDirectory = sourceDirectory;
            TempRoot = tempRoot;
        }

        public string SourceDirectory { get; }
        public string TempRoot { get; }
    }

    internal enum UpdateProgressState
    {
        Downloading,
        Extracting,
        Preparing,
        Failed,
        Completed
    }

    internal sealed class UpdateProgressInfo
    {
        public UpdateProgressInfo(UpdateProgressState state, int percentage, string message = null)
        {
            State = state;
            Percentage = percentage;
            Message = message;
        }

        public UpdateProgressState State { get; }
        public int Percentage { get; }
        public string Message { get; }
    }
}

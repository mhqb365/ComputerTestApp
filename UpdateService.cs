using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
    }
}

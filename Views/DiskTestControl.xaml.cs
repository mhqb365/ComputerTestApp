using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ComputerTestApp.Views
{
    public partial class DiskTestControl : UserControl
    {
        private DiskCheckResult currentResult;

        public DiskTestControl()
        {
            InitializeComponent();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Unloaded += DiskTestControl_Unloaded;
        }

        private async void CheckDiskButton_Click(object sender, RoutedEventArgs e)
        {
            CheckDiskButton.IsEnabled = false;
            CheckDiskButton.SetResourceReference(ContentControl.ContentProperty, "CheckingDisk");
            ResultPanel.Children.Clear();

            try
            {
                currentResult = await Task.Run(() => LoadDiskInfo());
                ShowResults(currentResult);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Format("DiskCheckErrorFormat", ex.Message),
                    LocalizationService.Get("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                CheckDiskButton.IsEnabled = true;
                CheckDiskButton.SetResourceReference(ContentControl.ContentProperty, "CheckDisk");
            }
        }

        private void ShowResults(DiskCheckResult result)
        {
            ResultPanel.Children.Clear();
            if (result == null) return;

            if (!string.IsNullOrWhiteSpace(result.MessageResourceKey))
            {
                AddMutedMessage(LocalizationService.Get(result.MessageResourceKey));
                return;
            }

            foreach (var disk in result.Disks)
            {
                ResultPanel.Children.Add(CreateDiskCard(disk));
            }
        }

        private void AddMutedMessage(string message)
        {
            var text = new TextBlock
            {
                Text = message,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            ResultPanel.Children.Add(text);
        }

        private Border CreateDiskCard(DiskInfo disk)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24),
                Margin = new Thickness(0, 0, 0, 16),
                MaxWidth = 820,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            card.SetResourceReference(Border.BackgroundProperty, "RegionBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            card.BorderThickness = new Thickness(1);

            var panel = new StackPanel();
            card.Child = panel;

            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(disk.Model) ? disk.DeviceName : disk.Model,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20),
                TextWrapping = TextWrapping.Wrap
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            panel.Children.Add(title);

            AddInfoRow(panel, LocalizationService.Get("DiskProtocol"), disk.Protocol);
            AddInfoRow(panel, LocalizationService.Get("DiskSerial"), disk.Serial);
            AddInfoRow(panel, LocalizationService.Get("DiskCapacity"), disk.Capacity);
            AddInfoRow(panel, LocalizationService.Get("DiskHealth"), disk.Health);
            AddInfoRow(
                panel,
                LocalizationService.Get("DiskSmartStatus"),
                LocalizationService.Get(string.IsNullOrWhiteSpace(disk.SmartStatusResourceKey) ? "Unknown" : disk.SmartStatusResourceKey));
            AddInfoRow(panel, LocalizationService.Get("DiskTemperature"), disk.Temperature);
            AddInfoRow(panel, LocalizationService.Get("DiskPowerOnHours"), disk.PowerOnHours);
            AddInfoRow(panel, LocalizationService.Get("DiskPowerCycles"), disk.PowerCycles);

            return card;
        }

        private static void AddInfoRow(Panel panel, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelText = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 12, 0),
                TextWrapping = TextWrapping.Wrap
            };
            labelText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            Grid.SetColumn(labelText, 0);
            grid.Children.Add(labelText);

            var valueText = new TextBlock
            {
                Text = value,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(valueText);

            panel.Children.Add(grid);
        }

        private static DiskCheckResult LoadDiskInfo()
        {
            var smartctlPath = FindSmartctlPath();
            if (string.IsNullOrWhiteSpace(smartctlPath))
            {
                return DiskCheckResult.Message("SmartctlNotFound");
            }

            var physicalDisks = LoadInternalPhysicalDisks();
            if (physicalDisks.Count == 0)
            {
                return DiskCheckResult.Message("NoDiskFound");
            }

            var scanDevices = LoadSmartScanDevices(smartctlPath);
            var disks = physicalDisks
                .Select(disk => CreateDiskInfo(smartctlPath, scanDevices, disk))
                .ToList();

            return DiskCheckResult.Success(disks);
        }

        private static List<SmartScanDevice> LoadSmartScanDevices(string smartctlPath)
        {
            try
            {
                var scanOutput = RunSmartctl(smartctlPath, "--scan -j");
                var scan = Deserialize<SmartScanResult>(scanOutput);
                return scan?.Devices?.Where(device => !string.IsNullOrWhiteSpace(device.Name)).ToList()
                    ?? new List<SmartScanDevice>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not scan smartctl devices: " + ex.Message);
                return new List<SmartScanDevice>();
            }
        }

        private static DiskInfo CreateDiskInfo(
            string smartctlPath,
            IReadOnlyCollection<SmartScanDevice> scanDevices,
            PhysicalDiskInfo physicalDisk)
        {
            foreach (var device in GetSmartCandidates(scanDevices, physicalDisk))
            {
                try
                {
                    var typeArgument = string.IsNullOrWhiteSpace(device.Type)
                        ? string.Empty
                        : $"-d {device.Type} ";
                    var detailOutput = RunSmartctl(smartctlPath, $"-a -j {typeArgument}\"{device.Name}\"");
                    var detail = Deserialize<SmartDiskDetail>(detailOutput);
                    var smartInfo = CreateDiskInfo(device, detail);
                    smartInfo.DeviceName = physicalDisk.DeviceId;
                    smartInfo.Protocol = FirstNonEmpty(smartInfo.Protocol, physicalDisk.InterfaceType);
                    smartInfo.Model = FirstNonEmpty(smartInfo.Model, physicalDisk.Model);
                    smartInfo.Capacity = FirstNonEmpty(smartInfo.Capacity, physicalDisk.SizeText);
                    return smartInfo;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not read SMART for {physicalDisk.DeviceId} using {device.Name}: {ex.Message}");
                }
            }

            return new DiskInfo
            {
                DeviceName = physicalDisk.DeviceId,
                Protocol = physicalDisk.InterfaceType,
                Model = physicalDisk.Model,
                Serial = physicalDisk.Serial,
                Capacity = physicalDisk.SizeText,
                SmartStatusResourceKey = "DiskSmartUnavailable"
            };
        }

        private static IEnumerable<SmartScanDevice> GetSmartCandidates(
            IEnumerable<SmartScanDevice> scanDevices,
            PhysicalDiskInfo physicalDisk)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                physicalDisk.DeviceId,
                $"/dev/pd{physicalDisk.Index}",
                $"/dev/sd{(char)('a' + physicalDisk.Index)}"
            };

            foreach (var device in scanDevices.Where(device => names.Contains(device.Name)))
            {
                yield return device;
            }

            foreach (var name in names)
            {
                yield return new SmartScanDevice { Name = name };
            }
        }

        private static DiskInfo CreateDiskInfo(SmartScanDevice device, SmartDiskDetail detail)
        {
            var smartPassed = detail?.SmartStatus?.Passed;
            var temperature = detail?.Temperature?.Current ??
                detail?.NvmeSmartLog?.Temperature;
            var powerOnHours = detail?.PowerOnTime?.Hours ??
                detail?.NvmeSmartLog?.PowerOnHours;
            var powerCycles = detail?.PowerCycleCount ??
                detail?.NvmeSmartLog?.PowerCycles;
            var wearUsed = detail?.NvmeSmartLog?.PercentageUsed;
            var health = wearUsed.HasValue ? Math.Max(0, 100 - wearUsed.Value) : (int?)null;

            return new DiskInfo
            {
                DeviceName = device.Name,
                Protocol = detail?.Device?.Protocol ?? device.Protocol ?? device.Type,
                Model = FirstNonEmpty(detail?.ModelName, detail?.Device?.ModelName, device.InfoName),
                Serial = detail?.SerialNumber,
                Capacity = detail?.UserCapacity?.StringValue,
                SmartStatusResourceKey = smartPassed.HasValue
                    ? (smartPassed.Value ? "DiskSmartPassed" : "DiskSmartFailed")
                    : "Unknown",
                Temperature = temperature.HasValue
                    ? LocalizationService.Format("DiskTemperatureFormat", temperature.Value)
                    : null,
                PowerOnHours = powerOnHours.HasValue ? powerOnHours.Value.ToString() : null,
                PowerCycles = powerCycles.HasValue ? powerCycles.Value.ToString() : null,
                Health = health.HasValue
                    ? LocalizationService.Format("DiskHealthFormat", health.Value)
                    : null
            };
        }

        private static string FindSmartctlPath()
        {
            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExternalApp", "smartmontools", "smartctl.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smartctl.exe"),
                @"C:\Program Files\smartmontools\bin\smartctl.exe",
                @"C:\Program Files (x86)\smartmontools\bin\smartctl.exe"
            };

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            candidates.AddRange(path
                .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => Path.Combine(folder.Trim(), "smartctl.exe")));

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string RunSmartctl(string smartctlPath, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = smartctlPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException(LocalizationService.Get("SmartctlRunFailed"));
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(20000);

                if (!process.HasExited)
                {
                    process.Kill();
                    throw new TimeoutException(LocalizationService.Get("SmartctlTimeout"));
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? LocalizationService.Get("SmartctlRunFailed")
                        : error.Trim());
                }

                return output;
            }
        }

        private static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static List<PhysicalDiskInfo> LoadInternalPhysicalDisks()
        {
            var disks = new List<PhysicalDiskInfo>();

            using (var searcher = new ManagementObjectSearcher(
                       "SELECT Index, DeviceID, Model, SerialNumber, InterfaceType, MediaType, Size FROM Win32_DiskDrive"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject result in results)
                {
                    var disk = new PhysicalDiskInfo
                    {
                        Index = ToInt(result["Index"]),
                        DeviceId = Convert.ToString(result["DeviceID"]),
                        Model = Convert.ToString(result["Model"]),
                        Serial = Convert.ToString(result["SerialNumber"])?.Trim(),
                        InterfaceType = Convert.ToString(result["InterfaceType"]),
                        MediaType = Convert.ToString(result["MediaType"]),
                        SizeText = FormatBytes(ToLong(result["Size"]))
                    };

                    if (!IsRemovableDisk(disk))
                    {
                        disks.Add(disk);
                    }
                }
            }

            return disks.OrderBy(disk => disk.Index).ToList();
        }

        private static bool IsRemovableDisk(PhysicalDiskInfo disk)
        {
            var text = $"{disk.InterfaceType} {disk.MediaType} {disk.Model}".ToUpperInvariant();
            return text.Contains("USB") ||
                   text.Contains("REMOVABLE") ||
                   text.Contains("SD CARD") ||
                   text.Contains("CARD READER");
        }

        private static int ToInt(object value)
        {
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static long ToLong(object value)
        {
            return value == null ? 0 : Convert.ToInt64(value);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return null;

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var size = (double)bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";
        }

        private class DiskCheckResult
        {
            public string MessageResourceKey { get; private set; }
            public IReadOnlyCollection<DiskInfo> Disks { get; private set; } = new List<DiskInfo>();

            public static DiskCheckResult Message(string resourceKey) =>
                new DiskCheckResult { MessageResourceKey = resourceKey };

            public static DiskCheckResult Success(IReadOnlyCollection<DiskInfo> disks) =>
                new DiskCheckResult { Disks = disks };
        }

        private class DiskInfo
        {
            public string DeviceName { get; set; }
            public string Protocol { get; set; }
            public string Model { get; set; }
            public string Serial { get; set; }
            public string Capacity { get; set; }
            public string SmartStatusResourceKey { get; set; }
            public string Temperature { get; set; }
            public string PowerOnHours { get; set; }
            public string PowerCycles { get; set; }
            public string Health { get; set; }
        }

        private class PhysicalDiskInfo
        {
            public int Index { get; set; }
            public string DeviceId { get; set; }
            public string Model { get; set; }
            public string Serial { get; set; }
            public string InterfaceType { get; set; }
            public string MediaType { get; set; }
            public string SizeText { get; set; }
        }

        [DataContract]
        private class SmartScanResult
        {
            [DataMember(Name = "devices")]
            public SmartScanDevice[] Devices { get; set; }
        }

        [DataContract]
        private class SmartScanDevice
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "info_name")]
            public string InfoName { get; set; }

            [DataMember(Name = "type")]
            public string Type { get; set; }

            [DataMember(Name = "protocol")]
            public string Protocol { get; set; }
        }

        [DataContract]
        private class SmartDiskDetail
        {
            [DataMember(Name = "device")]
            public SmartDeviceInfo Device { get; set; }

            [DataMember(Name = "model_name")]
            public string ModelName { get; set; }

            [DataMember(Name = "serial_number")]
            public string SerialNumber { get; set; }

            [DataMember(Name = "user_capacity")]
            public SmartCapacity UserCapacity { get; set; }

            [DataMember(Name = "smart_status")]
            public SmartStatus SmartStatus { get; set; }

            [DataMember(Name = "temperature")]
            public SmartTemperature Temperature { get; set; }

            [DataMember(Name = "power_on_time")]
            public SmartPowerOnTime PowerOnTime { get; set; }

            [DataMember(Name = "power_cycle_count")]
            public long? PowerCycleCount { get; set; }

            [DataMember(Name = "nvme_smart_health_information_log")]
            public NvmeSmartLog NvmeSmartLog { get; set; }
        }

        [DataContract]
        private class SmartDeviceInfo
        {
            [DataMember(Name = "protocol")]
            public string Protocol { get; set; }

            [DataMember(Name = "model_name")]
            public string ModelName { get; set; }
        }

        [DataContract]
        private class SmartCapacity
        {
            [DataMember(Name = "string")]
            public string StringValue { get; set; }
        }

        [DataContract]
        private class SmartStatus
        {
            [DataMember(Name = "passed")]
            public bool? Passed { get; set; }
        }

        [DataContract]
        private class SmartTemperature
        {
            [DataMember(Name = "current")]
            public int? Current { get; set; }
        }

        [DataContract]
        private class SmartPowerOnTime
        {
            [DataMember(Name = "hours")]
            public long? Hours { get; set; }
        }

        [DataContract]
        private class NvmeSmartLog
        {
            [DataMember(Name = "temperature")]
            public int? Temperature { get; set; }

            [DataMember(Name = "power_on_hours")]
            public long? PowerOnHours { get; set; }

            [DataMember(Name = "power_cycles")]
            public long? PowerCycles { get; set; }

            [DataMember(Name = "percentage_used")]
            public int? PercentageUsed { get; set; }
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ShowResults(currentResult);
        }

        private void DiskTestControl_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            Unloaded -= DiskTestControl_Unloaded;
        }
    }
}

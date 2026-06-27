using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ComputerTestApp.Views
{
    public partial class SystemInfoControl : UserControl
    {
        private IReadOnlyCollection<SystemInfoSection> currentSections = new List<SystemInfoSection>();

        public SystemInfoControl()
        {
            InitializeComponent();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Unloaded += SystemInfoControl_Unloaded;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            RefreshButton.SetResourceReference(ContentControl.ContentProperty, "LoadingSystemInfo");
            ResultPanel.Children.Clear();

            try
            {
                currentSections = await Task.Run(() => LoadSystemInfo());
                ShowResults(currentSections);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Format("SystemInfoErrorFormat", ex.Message),
                    LocalizationService.Get("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RefreshButton.IsEnabled = true;
                RefreshButton.SetResourceReference(ContentControl.ContentProperty, "RefreshSystemInfo");
            }
        }

        private void ShowResults(IReadOnlyCollection<SystemInfoSection> sections)
        {
            ResultPanel.Children.Clear();

            if (sections == null || sections.Count == 0)
            {
                AddMutedMessage(LocalizationService.Get("NoSystemInfo"));
                return;
            }

            foreach (var section in sections)
            {
                ResultPanel.Children.Add(CreateSectionCard(section));
            }
        }

        private Border CreateSectionCard(SystemInfoSection section)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24),
                Margin = new Thickness(0, 0, 0, 16),
                MaxWidth = 820,
                HorizontalAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(1)
            };
            card.SetResourceReference(Border.BackgroundProperty, "RegionBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var panel = new StackPanel();
            card.Child = panel;

            var title = new TextBlock
            {
                Text = LocalizationService.Get(section.TitleResourceKey),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20),
                TextWrapping = TextWrapping.Wrap
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            panel.Children.Add(title);

            foreach (var row in section.Rows)
            {
                AddInfoRow(panel, LocalizationService.Get(row.LabelResourceKey), row.Value);
            }

            return card;
        }

        private static void AddInfoRow(Panel panel, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
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

        private static IReadOnlyCollection<SystemInfoSection> LoadSystemInfo()
        {
            var sections = new List<SystemInfoSection>
            {
                CreateOperatingSystemSection(),
                CreateComputerSection(),
                CreateProcessorSection(),
                CreateBiosSection(),
                CreateGraphicsSection(),
                CreateDiskSection()
            };

            return sections.Where(section => section.Rows.Count > 0).ToList();
        }

        private static SystemInfoSection CreateOperatingSystemSection()
        {
            var os = QueryFirst("SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem");
            return Section("SystemInfoOsSection",
                Row("SystemInfoOsName", Get(os, "Caption")),
                Row("SystemInfoOsVersion", CombineVersion(Get(os, "Version"), Get(os, "BuildNumber"))),
                Row("SystemInfoOsArchitecture", Get(os, "OSArchitecture")),
                Row("SystemInfoInstallDate", FormatWmiDate(Get(os, "InstallDate"))),
                Row("SystemInfoLastBoot", FormatWmiDate(Get(os, "LastBootUpTime"))));
        }

        private static SystemInfoSection CreateComputerSection()
        {
            var computer = QueryFirst("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem");
            var board = QueryFirst("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            return Section("SystemInfoComputerSection",
                Row("SystemInfoManufacturer", Get(computer, "Manufacturer")),
                Row("SystemInfoModel", Get(computer, "Model")),
                Row("SystemInfoMemory", FormatBytes(ToLong(Get(computer, "TotalPhysicalMemory")))),
                Row("SystemInfoMainboard", CombineNonEmpty(Get(board, "Manufacturer"), Get(board, "Product"))));
        }

        private static SystemInfoSection CreateProcessorSection()
        {
            var cpu = QueryFirst("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            return Section("SystemInfoCpuSection",
                Row("SystemInfoCpuName", Get(cpu, "Name")),
                Row("SystemInfoCpuCores", Get(cpu, "NumberOfCores")),
                Row("SystemInfoCpuThreads", Get(cpu, "NumberOfLogicalProcessors")),
                Row("SystemInfoCpuClock", FormatClock(Get(cpu, "MaxClockSpeed"))));
        }

        private static SystemInfoSection CreateBiosSection()
        {
            var bios = QueryFirst("SELECT Manufacturer, SMBIOSBIOSVersion, SerialNumber, ReleaseDate FROM Win32_BIOS");
            return Section("SystemInfoBiosSection",
                Row("SystemInfoBiosVendor", Get(bios, "Manufacturer")),
                Row("SystemInfoBiosVersion", Get(bios, "SMBIOSBIOSVersion")),
                Row("SystemInfoSerialNumber", Get(bios, "SerialNumber")),
                Row("SystemInfoBiosDate", FormatWmiDate(Get(bios, "ReleaseDate"))));
        }

        private static SystemInfoSection CreateGraphicsSection()
        {
            var gpus = Query("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController").ToList();
            var section = new SystemInfoSection("SystemInfoGraphicsSection");

            for (var index = 0; index < gpus.Count; index++)
            {
                var gpu = gpus[index];
                var name = Get(gpu, "Name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var detail = CombineNonEmpty(
                    FormatBytes(ToLong(Get(gpu, "AdapterRAM"))),
                    LocalizationService.Format("SystemInfoDriverFormat", Get(gpu, "DriverVersion")));
                section.Rows.Add(new SystemInfoRow("SystemInfoGpuFormat", LocalizationService.Format("SystemInfoDeviceWithDetails", index + 1, name, detail)));
            }

            return section;
        }

        private static SystemInfoSection CreateDiskSection()
        {
            var disks = Query("SELECT Model, InterfaceType, Size FROM Win32_DiskDrive").ToList();
            var section = new SystemInfoSection("SystemInfoStorageSection");

            for (var index = 0; index < disks.Count; index++)
            {
                var disk = disks[index];
                var model = Get(disk, "Model");
                if (string.IsNullOrWhiteSpace(model)) continue;

                var detail = CombineNonEmpty(Get(disk, "InterfaceType"), FormatBytes(ToLong(Get(disk, "Size"))));
                section.Rows.Add(new SystemInfoRow("SystemInfoDiskFormat", LocalizationService.Format("SystemInfoDeviceWithDetails", index + 1, model, detail)));
            }

            return section;
        }

        private static IEnumerable<ManagementObject> Query(string query)
        {
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject result in results)
                {
                    yield return result;
                }
            }
        }

        private static ManagementObject QueryFirst(string query)
        {
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                return results.Cast<ManagementObject>().FirstOrDefault();
            }
        }

        private static string Get(ManagementBaseObject item, string propertyName)
        {
            return item == null ? null : Convert.ToString(item[propertyName])?.Trim();
        }

        private static long ToLong(string value)
        {
            return long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
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

        private static string FormatClock(string value)
        {
            return int.TryParse(value, out var mhz) ? $"{mhz / 1000.0:0.##} GHz" : null;
        }

        private static string FormatWmiDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            try
            {
                return ManagementDateTimeConverter.ToDateTime(value).ToString("yyyy-MM-dd HH:mm");
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static string CombineVersion(string version, string build)
        {
            if (string.IsNullOrWhiteSpace(version)) return build;
            return string.IsNullOrWhiteSpace(build) ? version : $"{version} (Build {build})";
        }

        private static string CombineNonEmpty(params string[] values)
        {
            return string.Join(" | ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static SystemInfoSection Section(string titleResourceKey, params SystemInfoRow[] rows)
        {
            var section = new SystemInfoSection(titleResourceKey);
            foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.Value)))
            {
                section.Rows.Add(row);
            }

            return section;
        }

        private static SystemInfoRow Row(string labelResourceKey, string value) =>
            new SystemInfoRow(labelResourceKey, value);

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ShowResults(currentSections);
        }

        private void SystemInfoControl_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            Unloaded -= SystemInfoControl_Unloaded;
        }

        private sealed class SystemInfoSection
        {
            public SystemInfoSection(string titleResourceKey)
            {
                TitleResourceKey = titleResourceKey;
            }

            public string TitleResourceKey { get; }
            public List<SystemInfoRow> Rows { get; } = new List<SystemInfoRow>();
        }

        private sealed class SystemInfoRow
        {
            public SystemInfoRow(string labelResourceKey, string value)
            {
                LabelResourceKey = labelResourceKey;
                Value = value;
            }

            public string LabelResourceKey { get; }
            public string Value { get; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace ComputerTestApp.Views
{
    public partial class BatteryTestControl : UserControl
    {
        private readonly DispatcherTimer powerRefreshTimer;
        private List<BatteryInfo> currentBatteries = new List<BatteryInfo>();

        public BatteryTestControl()
        {
            InitializeComponent();
            powerRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            powerRefreshTimer.Tick += PowerRefreshTimer_Tick;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        }

        private async void CheckBatteryButton_Click(object sender, RoutedEventArgs e)
        {
            CheckBatteryButton.IsEnabled = false;
            CheckBatteryButton.SetResourceReference(ContentControl.ContentProperty, "CheckingBattery");
            ResultPanel.Children.Clear();

            try
            {
                currentBatteries = (await Task.Run(() => LoadBatteryInfo())).ToList();
                ShowResults(currentBatteries);
                if (currentBatteries.Count > 0)
                {
                    powerRefreshTimer.Start();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Format("BatteryCheckErrorFormat", ex.Message),
                    LocalizationService.Get("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                CheckBatteryButton.IsEnabled = true;
                CheckBatteryButton.SetResourceReference(ContentControl.ContentProperty, "CheckBattery");
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            powerRefreshTimer.Stop();
            powerRefreshTimer.Tick -= PowerRefreshTimer_Tick;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            if (currentBatteries.Count == 0) return;

            RefreshCurrentBatteryStatus();
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.StatusChange) return;

            _ = Dispatcher.BeginInvoke(new Action(RefreshCurrentBatteryStatus));
        }

        private void PowerRefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshCurrentBatteryStatus();
        }

        private void RefreshCurrentBatteryStatus()
        {
            if (currentBatteries.Count == 0) return;

            var states = LoadBatteryStates();
            for (var index = 0; index < currentBatteries.Count; index++)
            {
                var state = index < states.Count ? states[index] : states.FirstOrDefault();
                if (state == null) continue;

                currentBatteries[index].ChargePercent = state.ChargePercent;
                currentBatteries[index].Status = state.Status;
                currentBatteries[index].EstimatedRunTime = state.EstimatedRunTime;
            }

            ShowResults(currentBatteries);
        }

        private void ShowResults(IReadOnlyCollection<BatteryInfo> batteries)
        {
            ResultPanel.Children.Clear();

            if (batteries.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 0, 12),
                    TextWrapping = TextWrapping.Wrap
                };
                emptyText.SetResourceReference(TextBlock.TextProperty, "NoBattery");
                emptyText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
                ResultPanel.Children.Add(emptyText);
                return;
            }

            foreach (var battery in batteries)
            {
                ResultPanel.Children.Add(CreateBatteryCard(battery));
            }
        }

        private Border CreateBatteryCard(BatteryInfo battery)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24),
                Margin = new Thickness(0, 0, 0, 16),
                MaxWidth = 720,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            card.SetResourceReference(Border.BackgroundProperty, "RegionBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            card.BorderThickness = new Thickness(1);

            var panel = new StackPanel();
            card.Child = panel;

            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(battery.Id)
                    ? LocalizationService.Get("Battery")
                    : LocalizationService.Format("BatteryNameFormat", battery.Id),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20)
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            panel.Children.Add(title);

            AddProgressRow(panel, LocalizationService.Get("BatteryCharge"), battery.ChargePercent, battery.Status);
            AddProgressRow(panel, LocalizationService.Get("BatteryHealth"), battery.HealthPercent, null);
            AddInfoRow(panel, LocalizationService.Get("BatteryCycleCount"), battery.CycleCount);
            AddInfoRow(panel, LocalizationService.Get("BatteryDesignCapacity"), FormatMWh(battery.DesignCapacity));
            AddInfoRow(panel, LocalizationService.Get("BatteryFullChargeCapacity"), FormatMWh(battery.FullChargeCapacity));
            AddInfoRow(panel, LocalizationService.Get("BatteryWearLevel"), FormatPercent(battery.WearPercent));

            if (!string.IsNullOrWhiteSpace(battery.EstimatedRunTime))
            {
                AddInfoRow(panel, LocalizationService.Get("BatteryEstimatedRuntime"), battery.EstimatedRunTime);
            }

            return card;
        }

        private static void AddProgressRow(Panel panel, string label, double percent, string suffix)
        {
            var grid = CreateRowGrid();

            var labelText = CreateMutedText(label);
            Grid.SetColumn(labelText, 0);
            grid.Children.Add(labelText);

            var progress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = Clamp(percent),
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(progress, 1);
            grid.Children.Add(progress);

            var valueText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(suffix)
                    ? FormatPercent(percent)
                    : $"{FormatPercent(percent)} ({suffix})",
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            Grid.SetColumn(valueText, 2);
            grid.Children.Add(valueText);

            panel.Children.Add(grid);
        }

        private static void AddInfoRow(Panel panel, string label, string value)
        {
            var grid = CreateRowGrid();

            var labelText = CreateMutedText(label);
            Grid.SetColumn(labelText, 0);
            grid.Children.Add(labelText);

            var valueText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? LocalizationService.Get("Unknown") : value,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(valueText, 1);
            Grid.SetColumnSpan(valueText, 2);
            grid.Children.Add(valueText);

            panel.Children.Add(grid);
        }

        private static Grid CreateRowGrid()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return grid;
        }

        private static TextBlock CreateMutedText(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 0, 12, 0),
                TextWrapping = TextWrapping.Wrap
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            return textBlock;
        }

        private static IReadOnlyCollection<BatteryInfo> LoadBatteryInfo()
        {
            var reportPath = Path.Combine(Path.GetTempPath(), $"computer-test-battery-{Guid.NewGuid():N}.xml");

            try
            {
                CreateBatteryReport(reportPath);
                if (!File.Exists(reportPath))
                {
                    throw new InvalidOperationException(LocalizationService.Get("BatteryReportCreateFailed"));
                }

                var report = XDocument.Load(reportPath);
                var batteryNodes = report.Descendants()
                    .Where(element => element.Name.LocalName == "Battery" &&
                        element.Parent?.Name.LocalName == "Batteries")
                    .ToList();
                var wmiBatteries = LoadBatteryStates();
                var results = new List<BatteryInfo>();

                for (var index = 0; index < batteryNodes.Count; index++)
                {
                    var node = batteryNodes[index];
                    var wmi = index < wmiBatteries.Count ? wmiBatteries[index] : null;
                    var designCapacity = ReadDouble(node, "DesignCapacity");
                    var fullChargeCapacity = ReadDouble(node, "FullChargeCapacity");
                    var healthPercent = designCapacity > 0
                        ? Math.Round(fullChargeCapacity / designCapacity * 100, 2)
                        : 0;

                    results.Add(new BatteryInfo
                    {
                        Id = ReadString(node, "Id"),
                        CycleCount = ReadString(node, "CycleCount"),
                        DesignCapacity = designCapacity,
                        FullChargeCapacity = fullChargeCapacity,
                        HealthPercent = healthPercent,
                        WearPercent = Math.Round(Math.Max(0, 100 - healthPercent), 2),
                        ChargePercent = wmi?.ChargePercent ?? 0,
                        Status = wmi?.Status ?? LocalizationService.Get("Unknown"),
                        EstimatedRunTime = wmi?.EstimatedRunTime
                    });
                }

                return results;
            }
            finally
            {
                try
                {
                    if (File.Exists(reportPath))
                    {
                        File.Delete(reportPath);
                    }
                }
                catch (IOException ex)
                {
                    Debug.WriteLine("Could not delete battery report: " + ex.Message);
                }
            }
        }

        private static void CreateBatteryReport(string outputPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /xml /output \"{outputPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException(LocalizationService.Get("BatteryReportCreateFailed"));
                }

                process.WaitForExit(15000);
                if (!process.HasExited)
                {
                    process.Kill();
                    throw new TimeoutException(LocalizationService.Get("BatteryReportTimeout"));
                }

                if (process.ExitCode != 0 && !File.Exists(outputPath))
                {
                    var error = process.StandardError.ReadToEnd();
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? LocalizationService.Get("BatteryReportCreateFailed")
                        : error.Trim());
                }
            }
        }

        private static List<WmiBatteryInfo> LoadBatteryStates()
        {
            var batteries = new List<WmiBatteryInfo>();
            var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
            var isAcOnline = powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            var isAcOffline = powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                           "SELECT BatteryStatus, EstimatedChargeRemaining, EstimatedRunTime FROM Win32_Battery"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject result in results)
                    {
                        var statusCode = ToInt(result["BatteryStatus"]);
                        batteries.Add(new WmiBatteryInfo
                        {
                            ChargePercent = ToInt(result["EstimatedChargeRemaining"]),
                            Status = GetBatteryStatus(statusCode, isAcOnline, isAcOffline),
                            EstimatedRunTime = FormatRuntime(ToInt(result["EstimatedRunTime"]))
                        });
                    }
                }
            }
            catch (ManagementException ex)
            {
                Debug.WriteLine("Could not refresh battery WMI state: " + ex.Message);
            }

            if (batteries.Count == 0 && powerStatus.BatteryLifePercent >= 0)
            {
                batteries.Add(new WmiBatteryInfo
                {
                    ChargePercent = (int)Math.Round(powerStatus.BatteryLifePercent * 100),
                    Status = isAcOffline
                        ? LocalizationService.Get("BatteryDischarging")
                        : LocalizationService.Get("BatteryCharging"),
                    EstimatedRunTime = FormatRuntime(powerStatus.BatteryLifeRemaining > 0
                        ? powerStatus.BatteryLifeRemaining / 60
                        : 0)
                });
            }

            return batteries;
        }

        private static string GetBatteryStatus(int statusCode, bool isAcOnline, bool isAcOffline)
        {
            if (isAcOffline)
            {
                return LocalizationService.Get("BatteryDischarging");
            }

            switch (statusCode)
            {
                case 1:
                    return LocalizationService.Get("BatteryDischarging");
                case 2:
                    return isAcOnline
                        ? LocalizationService.Get("BatteryCharging")
                        : LocalizationService.Get("BatteryDischarging");
                case 3:
                    return LocalizationService.Get("BatteryFullyCharged");
                default:
                    return isAcOnline
                        ? LocalizationService.Get("BatteryCharging")
                        : LocalizationService.Get("Unknown");
            }
        }

        private static string FormatRuntime(int totalMinutes)
        {
            if (totalMinutes <= 0 || totalMinutes == 71582788) return null;

            var days = totalMinutes / 1440;
            var hours = totalMinutes % 1440 / 60;
            var minutes = totalMinutes % 60;
            var parts = new List<string>();

            if (days > 0) parts.Add(LocalizationService.Format("BatteryRuntimeDays", days));
            if (hours > 0) parts.Add(LocalizationService.Format("BatteryRuntimeHours", hours));
            parts.Add(LocalizationService.Format("BatteryRuntimeMinutes", minutes));

            return string.Join(" ", parts);
        }

        private static string ReadString(XElement element, string name)
        {
            return element.Elements()
                .FirstOrDefault(child => child.Name.LocalName == name)
                ?.Value;
        }

        private static double ReadDouble(XElement element, string name)
        {
            return double.TryParse(ReadString(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private static int ToInt(object value)
        {
            return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static double Clamp(double value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }

        private static string FormatPercent(double value)
        {
            return $"{Math.Round(value, 2):0.##}%";
        }

        private static string FormatMWh(double value)
        {
            return value <= 0 ? LocalizationService.Get("Unknown") : $"{value / 1000:0.##} Wh";
        }

        private class BatteryInfo
        {
            public string Id { get; set; }
            public string CycleCount { get; set; }
            public double DesignCapacity { get; set; }
            public double FullChargeCapacity { get; set; }
            public double HealthPercent { get; set; }
            public double WearPercent { get; set; }
            public int ChargePercent { get; set; }
            public string Status { get; set; }
            public string EstimatedRunTime { get; set; }
        }

        private class WmiBatteryInfo
        {
            public int ChargePercent { get; set; }
            public string Status { get; set; }
            public string EstimatedRunTime { get; set; }
        }
    }
}

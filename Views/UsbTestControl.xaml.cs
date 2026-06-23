using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ComputerTestApp.Views
{
    public partial class UsbTestControl : UserControl
    {
        private static readonly Regex VidPidPattern = new Regex(
            @"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private Dictionary<string, UsbDeviceInfo> connectedDevices = new Dictionary<string, UsbDeviceInfo>();
        private ManagementEventWatcher deviceWatcher;
        private string displayedDeviceKey;
        private int eventGeneration;

        public UsbTestControl()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ClearDevice();

            try
            {
                connectedDevices = await Task.Run(() => LoadUsbDevices());
                if (!IsLoaded) return;

                StartWatcher();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Format("UsbMonitorErrorFormat", ex.Message),
                    LocalizationService.Get("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void StartWatcher()
        {
            if (deviceWatcher != null) return;

            deviceWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent"));
            deviceWatcher.EventArrived += DeviceWatcher_EventArrived;

            try
            {
                deviceWatcher.Start();
            }
            catch
            {
                deviceWatcher.EventArrived -= DeviceWatcher_EventArrived;
                deviceWatcher.Dispose();
                deviceWatcher = null;
                throw;
            }
        }

        private void DeviceWatcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            var value = e.NewEvent.Properties["EventType"]?.Value;
            if (value == null) return;

            var eventType = Convert.ToInt32(value);
            if (eventType != 2 && eventType != 3) return;

            var generation = Interlocked.Increment(ref eventGeneration);
            Task.Run(async () =>
            {
                await Task.Delay(700);
                if (generation != eventGeneration) return;

                try
                {
                    var snapshot = LoadUsbDevices();
                    _ = Dispatcher.BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Could not refresh USB devices: " + ex.Message);
                }
            });
        }

        private void ApplySnapshot(Dictionary<string, UsbDeviceInfo> snapshot)
        {
            var added = snapshot.Values
                .Where(device => !connectedDevices.ContainsKey(device.DeviceId))
                .ToList();
            var removedKeys = connectedDevices.Values
                .Where(device => !snapshot.ContainsKey(device.DeviceId))
                .Select(GetDeviceKey)
                .ToList();

            if (!string.IsNullOrEmpty(displayedDeviceKey) &&
                removedKeys.Any(key => string.Equals(key, displayedDeviceKey, StringComparison.OrdinalIgnoreCase)))
            {
                ClearDevice();
            }

            if (added.Count > 0)
            {
                var group = added
                    .GroupBy(GetDeviceKey, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(items => items.Count())
                    .First();
                ShowDevice(CreateDisplayDevice(group.ToList()), group.Key);
            }

            connectedDevices = snapshot;
        }

        private static UsbDeviceInfo CreateDisplayDevice(IReadOnlyCollection<UsbDeviceInfo> nodes)
        {
            var representative = nodes
                .OrderBy(device => device.DeviceId.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(device => IsGenericName(device.Name))
                .ThenBy(device => device.DeviceId.Length)
                .First();
            var manufacturer = nodes
                .Select(device => device.Manufacturer)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
                    value.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) < 0);
            var name = nodes
                .Select(device => device.Name)
                .FirstOrDefault(value => !IsGenericName(value));

            return new UsbDeviceInfo
            {
                Name = name ?? representative.Name,
                Manufacturer = manufacturer ?? representative.Manufacturer,
                Status = representative.Status,
                DeviceId = representative.DeviceId
            };
        }

        private static bool IsGenericName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;

            return name.Equals("USB Device", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("USB Input Device", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("USB Composite Device", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDeviceKey(UsbDeviceInfo device)
        {
            var match = VidPidPattern.Match(device.DeviceId);
            if (match.Success) return match.Value.ToUpperInvariant();

            var separator = device.DeviceId.LastIndexOf('\u005c');
            return separator > 0 ? device.DeviceId.Substring(0, separator).ToUpperInvariant() : device.DeviceId;
        }

        private void ShowDevice(UsbDeviceInfo device, string deviceKey)
        {
            displayedDeviceKey = deviceKey;
            DeviceNameText.Text = device.Name;
            SetTextOrUnknown(ManufacturerText, device.Manufacturer);
            SetTextOrUnknown(StatusText, device.Status);
            DeviceIdText.Text = device.DeviceId;
            DeviceCard.Visibility = Visibility.Visible;
        }

        private static void SetTextOrUnknown(TextBlock textBlock, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                textBlock.SetResourceReference(TextBlock.TextProperty, "Unknown");
                return;
            }

            textBlock.Text = value;
        }

        private void ClearDevice()
        {
            displayedDeviceKey = null;
            DeviceNameText.Text = string.Empty;
            ManufacturerText.Text = string.Empty;
            StatusText.Text = string.Empty;
            DeviceIdText.Text = string.Empty;
            DeviceCard.Visibility = Visibility.Collapsed;
        }

        private static Dictionary<string, UsbDeviceInfo> LoadUsbDevices()
        {
            const string query = "SELECT Name, Manufacturer, PNPDeviceID, Status FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'";
            var devices = new Dictionary<string, UsbDeviceInfo>(StringComparer.OrdinalIgnoreCase);

            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject result in results)
                {
                    var deviceId = Convert.ToString(result["PNPDeviceID"]);
                    if (string.IsNullOrWhiteSpace(deviceId)) continue;

                    devices[deviceId] = new UsbDeviceInfo
                    {
                        Name = Convert.ToString(result["Name"]) ?? "USB Device",
                        Manufacturer = Convert.ToString(result["Manufacturer"]),
                        Status = Convert.ToString(result["Status"]),
                        DeviceId = deviceId
                    };
                }
            }

            return devices;
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            Interlocked.Increment(ref eventGeneration);
            if (deviceWatcher == null) return;

            deviceWatcher.EventArrived -= DeviceWatcher_EventArrived;
            try
            {
                deviceWatcher.Stop();
            }
            catch (ManagementException ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not stop USB watcher: " + ex.Message);
            }

            deviceWatcher.Dispose();
            deviceWatcher = null;
        }

        private class UsbDeviceInfo
        {
            public string Name { get; set; }
            public string Manufacturer { get; set; }
            public string Status { get; set; }
            public string DeviceId { get; set; }
        }
    }
}

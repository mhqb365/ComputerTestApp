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
        private static readonly Regex DeviceIdReferencePattern = new Regex(
            @"DeviceID=""(?<id>(?:[^""]|"""")*)""",
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
                DeviceId = representative.DeviceId,
                Controller = nodes.Select(device => device.Controller).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                UsbStandard = InferUsbStandard(nodes),
                Connector = InferUsbConnector(nodes)
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
            SetTextOrUnknown(UsbStandardText, device.UsbStandard);
            SetTextOrUnknown(UsbControllerText, device.Controller);
            SetTextOrUnknown(UsbConnectorText, device.Connector);
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
            UsbStandardText.Text = string.Empty;
            UsbControllerText.Text = string.Empty;
            UsbConnectorText.Text = string.Empty;
            DeviceIdText.Text = string.Empty;
            DeviceCard.Visibility = Visibility.Collapsed;
        }

        private static Dictionary<string, UsbDeviceInfo> LoadUsbDevices()
        {
            const string query = "SELECT Name, Manufacturer, PNPDeviceID, Status FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'";
            var devices = new Dictionary<string, UsbDeviceInfo>(StringComparer.OrdinalIgnoreCase);
            var topology = LoadUsbTopology();

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
                        DeviceId = deviceId,
                        Controller = topology.FindControllerName(deviceId)
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
            public string Controller { get; set; }
            public string UsbStandard { get; set; }
            public string Connector { get; set; }
        }

        private static string InferUsbStandard(IEnumerable<UsbDeviceInfo> devices)
        {
            var context = GetInferenceContext(devices);
            if (ContainsAny(context, "USB4"))
            {
                return LocalizationService.Get("Usb4Inferred");
            }

            if (ContainsAny(context, "USB 3", "USB3", "3.0", "3.1", "3.2", "SUPERSPEED", "XHCI", "ROOT_HUB30"))
            {
                return LocalizationService.Get("Usb3Inferred");
            }

            if (ContainsAny(context, "USB 2", "USB2", "2.0", "HIGH-SPEED", "HIGH SPEED", "EHCI", "ENHANCED HOST CONTROLLER", "ROOT_HUB20"))
            {
                return LocalizationService.Get("Usb2Inferred");
            }

            if (ContainsAny(context, "UHCI", "OHCI", "OPENHCD"))
            {
                return LocalizationService.Get("Usb1Inferred");
            }

            return null;
        }

        private static string InferUsbConnector(IEnumerable<UsbDeviceInfo> devices)
        {
            var context = GetInferenceContext(devices);
            if (ContainsAny(context, "TYPE-C", "TYPE C", "USB-C", "USBC", "UCSI", "USB CONNECTOR MANAGER", "USB4"))
            {
                return LocalizationService.Get("UsbTypeCInferred");
            }

            return null;
        }

        private static string GetInferenceContext(IEnumerable<UsbDeviceInfo> devices)
        {
            return string.Join(" | ", devices.SelectMany(device => new[]
            {
                device.Name,
                device.Manufacturer,
                device.Status,
                device.DeviceId,
                device.Controller
            }).Where(value => !string.IsNullOrWhiteSpace(value))).ToUpperInvariant();
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            return needles.Any(needle => value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static UsbTopologyInfo LoadUsbTopology()
        {
            var topology = new UsbTopologyInfo();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_USBController"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject result in results)
                    {
                        var deviceId = Convert.ToString(result["PNPDeviceID"]);
                        if (string.IsNullOrWhiteSpace(deviceId)) continue;

                        topology.ControllerNames[deviceId] = Convert.ToString(result["Name"]);
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_USBControllerDevice"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject result in results)
                    {
                        var controllerId = ExtractDeviceId(Convert.ToString(result["Antecedent"]));
                        var dependentId = ExtractDeviceId(Convert.ToString(result["Dependent"]));
                        if (string.IsNullOrWhiteSpace(controllerId) || string.IsNullOrWhiteSpace(dependentId)) continue;

                        topology.DeviceControllerIds[dependentId] = controllerId;
                    }
                }
            }
            catch (ManagementException ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not load USB topology: " + ex.Message);
            }

            return topology;
        }

        private static string ExtractDeviceId(string objectReference)
        {
            if (string.IsNullOrWhiteSpace(objectReference)) return null;

            var match = DeviceIdReferencePattern.Match(objectReference);
            if (!match.Success) return null;

            return match.Groups["id"].Value
                .Replace("\"\"", "\"")
                .Replace(@"\\", @"\");
        }

        private class UsbTopologyInfo
        {
            public Dictionary<string, string> ControllerNames { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> DeviceControllerIds { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string FindControllerName(string deviceId)
            {
                if (string.IsNullOrWhiteSpace(deviceId)) return null;

                if (DeviceControllerIds.TryGetValue(deviceId, out var controllerId) &&
                    ControllerNames.TryGetValue(controllerId, out var controllerName))
                {
                    return controllerName;
                }

                return null;
            }
        }
    }
}

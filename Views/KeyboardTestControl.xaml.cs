using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ComputerTestApp.Views
{
    public partial class KeyboardTestControl : UserControl
    {
        private Dictionary<string, KeyUIInfo> keyInfos = new Dictionary<string, KeyUIInfo>();
        private int totalKeysPressed = 0;
        private HwndSource hwndSource;
        private bool rawInputRegistered;

        public KeyboardTestControl()
        {
            InitializeComponent();
            this.KeyDown += KeyboardTestControl_KeyDown;
            this.KeyUp += KeyboardTestControl_KeyUp;
            this.Unloaded += UserControl_Unloaded;
            GenerateKeyboardUI();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
            RegisterRawKeyboardInput();
            RegisterMouseButtons();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (hwndSource != null)
            {
                hwndSource.RemoveHook(WndProc);
                hwndSource = null;
            }
        }

        private void RegisterMouseButtons()
        {
            // Register XAML mouse borders into keyInfos
            RegisterMouseBorder(MouseLeftBtn, "Mouse_Left", "MouseLeft");
            RegisterMouseBorder(MouseMiddleBtn, "Mouse_Middle", "MouseMiddle");
            RegisterMouseBorder(MouseRightBtn, "Mouse_Right", "MouseRight");

            // Hook mouse events on the outer container
            this.PreviewMouseDown += UserControl_PreviewMouseDown;
            this.PreviewMouseUp += UserControl_PreviewMouseUp;
            this.PreviewMouseWheel += UserControl_PreviewMouseWheel;
        }

        private void RegisterMouseBorder(Border border, string keyId, string labelResourceKey)
        {
            var label = LocalizationService.Get(labelResourceKey);
            // Find the TextBlock inside the border
            var tb = border.Child as TextBlock;

            var countTb = new TextBlock
            {
                Text = "",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 2)
            };

            // Wrap existing content in a Grid to allow count overlay
            var grid = new Grid();
            border.Child = null;
            if (tb != null)
            {
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                tb.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(tb);
            }
            grid.Children.Add(countTb);
            border.Child = grid;

            var info = new KeyUIInfo
            {
                Border = border,
                MainText = tb ?? new TextBlock { Text = label },
                CountText = countTb,
                Label = label,
                LabelResourceKey = labelResourceKey
            };

            if (!keyInfos.ContainsKey(keyId))
                keyInfos.Add(keyId, info);

            UpdateKeyAppearance(info);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            totalKeysPressed = 0;
            TotalKeysText.Text = "0";
            HistoryPanel.Children.Clear();
            
            foreach (var kvp in keyInfos)
            {
                var info = kvp.Value;
                info.PressCount = 0;
                info.HasBeenPressed = false;
                info.IsPressed = false;
                info.CountText.Text = "";
                UpdateKeyAppearance(info);
            }
            this.Focus();
        }

        private void KeyboardTestControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (rawInputRegistered)
            {
                e.Handled = true;
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            // Use integer value to avoid WPF Key enum alias issues (e.g. Oem3 == OemTilde)
            string keyId = $"Key_{(int)key}";
            HandleKeyPress(keyId, key.ToString());
            e.Handled = true;
        }

        private void KeyboardTestControl_KeyUp(object sender, KeyEventArgs e)
        {
            if (rawInputRegistered)
            {
                e.Handled = true;
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            string keyId = $"Key_{(int)key}";
            HandleKeyRelease(keyId);
            e.Handled = true;
        }

        private void UserControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Do not intercept Reset button
            if (e.OriginalSource is DependencyObject source)
            {
                var parent = VisualTreeHelper.GetParent(source);
                while (parent != null)
                {
                    if (parent is Button btn && btn.Content?.ToString() == "Reset")
                        return;
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            string keyId = $"Mouse_{e.ChangedButton}";
            string label = e.ChangedButton.ToString() + " Click";
            HandleKeyPress(keyId, label);
            this.Focus();

            // Prevent middle click from triggering WPF ScrollViewer autoscroll
            if (e.ChangedButton == MouseButton.Middle)
                e.Handled = true;
        }

        private void UserControl_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            string keyId = $"Mouse_{e.ChangedButton}";
            HandleKeyRelease(keyId);
        }

        private void UserControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            string dir = e.Delta > 0 ? "Up" : "Down";
            string keyId = $"Mouse_Wheel{dir}";
            string label = $"Wheel {dir}";

            HandleKeyPress(keyId, label);
            
            System.Threading.Tasks.Task.Delay(100).ContinueWith(_ => 
            {
                Dispatcher.Invoke(() => HandleKeyRelease(keyId));
            });
        }

        private void HandleKeyPress(string keyId, string fallbackLabel)
        {
            if (keyInfos.TryGetValue(keyId, out KeyUIInfo info))
            {
                if (!info.IsPressed)
                {
                    info.IsPressed = true;
                    if (!info.HasBeenPressed)
                    {
                        info.HasBeenPressed = true;
                        totalKeysPressed++;
                        TotalKeysText.Text = totalKeysPressed.ToString();
                    }
                    info.PressCount++;
                    info.CountText.Text = info.PressCount.ToString();
                    
                    AddHistoryItem(info.LabelResourceKey == null ? info.Label : LocalizationService.Get(info.LabelResourceKey));
                }
                UpdateKeyAppearance(info);
            }
        }

        private void RegisterRawKeyboardInput()
        {
            if (rawInputRegistered) return;

            hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource == null) return;

            var device = new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = 0,
                hwndTarget = hwndSource.Handle
            };

            rawInputRegistered = RegisterRawInputDevices(
                new[] { device },
                1,
                (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));

            if (rawInputRegistered)
            {
                hwndSource.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmInput)
            {
                return IntPtr.Zero;
            }

            ProcessRawKeyboardInput(lParam);
            return IntPtr.Zero;
        }

        private void ProcessRawKeyboardInput(IntPtr rawInputHandle)
        {
            var headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint dataSize = 0;
            GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref dataSize, headerSize);
            if (dataSize == 0) return;

            var buffer = Marshal.AllocHGlobal((int)dataSize);
            try
            {
                if (GetRawInputData(rawInputHandle, RidInput, buffer, ref dataSize, headerSize) != dataSize)
                {
                    return;
                }

                var rawInput = (RAWINPUT)Marshal.PtrToStructure(buffer, typeof(RAWINPUT));
                if (rawInput.header.dwType != RimTypeKeyboard) return;

                var keyId = GetPhysicalKeyId(rawInput.keyboard, out var label);
                if (string.IsNullOrWhiteSpace(keyId)) return;

                if (rawInput.keyboard.Message == WmKeyDown || rawInput.keyboard.Message == WmSysKeyDown)
                {
                    HandleKeyPress(keyId, label);
                    return;
                }

                if (rawInput.keyboard.Message == WmKeyUp || rawInput.keyboard.Message == WmSysKeyUp)
                {
                    HandleKeyRelease(keyId);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static Key GetPhysicalKey(RAWKEYBOARD keyboard)
        {
            var virtualKey = keyboard.VKey;
            var isExtended = (keyboard.Flags & RiKeyE0) == RiKeyE0;

            if (virtualKey == VkShift)
            {
                virtualKey = (ushort)MapVirtualKey(keyboard.MakeCode, MapvkVscToVkEx);
            }
            else if (virtualKey == VkControl)
            {
                virtualKey = isExtended ? VkRControl : VkLControl;
            }
            else if (virtualKey == VkMenu)
            {
                virtualKey = isExtended ? VkRMenu : VkLMenu;
            }

            return KeyInterop.KeyFromVirtualKey(virtualKey);
        }
        private static string GetPhysicalKeyId(RAWKEYBOARD keyboard, out string label)
        {
            var virtualKey = keyboard.VKey;
            var isExtended = (keyboard.Flags & RiKeyE0) == RiKeyE0;

            if (virtualKey == VkReturn && isExtended)
            {
                label = "Ent";
                return NumpadEnterKeyId;
            }

            var key = GetPhysicalKey(keyboard);
            if (key == Key.None)
            {
                label = null;
                return null;
            }

            label = key.ToString();
            return $"Key_{(int)key}";
        }

        private void HandleKeyRelease(string keyId)
        {
            if (keyInfos.TryGetValue(keyId, out KeyUIInfo info))
            {
                info.IsPressed = false;
                UpdateKeyAppearance(info);
            }
        }

        private void AddHistoryItem(string label)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 5, 0)
            };
            border.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
            
            var tb = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.Bold,
                FontSize = 12
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "ReverseTextBrush");
            border.Child = tb;
            HistoryPanel.Children.Add(border);
            
            if (HistoryPanel.Parent is ScrollViewer sv)
            {
                Dispatcher.BeginInvoke(new Action(() => sv.ScrollToRightEnd()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void UpdateKeyAppearance(KeyUIInfo info)
        {
            if (info.IsPressed)
            {
                info.Border.SetResourceReference(Border.BackgroundProperty, "LightDangerBrush");
                info.Border.SetResourceReference(Border.BorderBrushProperty, "DangerBrush");
                info.Border.BorderThickness = new Thickness(1.5);
                info.MainText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
                info.CountText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            }
            else if (info.HasBeenPressed)
            {
                info.Border.SetResourceReference(Border.BackgroundProperty, "LightSuccessBrush");
                info.Border.SetResourceReference(Border.BorderBrushProperty, "SuccessBrush");
                info.Border.BorderThickness = new Thickness(1.5);
                info.MainText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
                info.CountText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            }
            else
            {
                info.Border.SetResourceReference(Border.BackgroundProperty, "RegionBrush");
                info.Border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                info.Border.BorderThickness = new Thickness(1);
                info.MainText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
                info.CountText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            }
        }

        private class KeyUIInfo
        {
            public Border Border { get; set; }
            public TextBlock MainText { get; set; }
            public TextBlock CountText { get; set; }
            public int PressCount { get; set; }
            public bool IsPressed { get; set; }
            public bool HasBeenPressed { get; set; }
            public string Label { get; set; }
            public string LabelResourceKey { get; set; }
        }

        private class KeyDef
        {
            public string KeyId { get; set; }
            public string Label { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double MarginRight { get; set; }
            public KeyDef(string kid, string l, double w = 1, double h = 1, double mr = 0) 
            { KeyId = kid; Label = l; Width = w; Height = h; MarginRight = mr; }
        }

        private void GenerateKeyboardUI()
        {
            double baseSize = 45;
            double margin = 3;

            // --- MAIN BLOCK ---
            // Use Key_(int)Key.Xxx format so ID always matches KeyDown handler
            Func<Key, string> KI = k => $"Key_{(int)k}";
            var mainRows = new List<List<KeyDef>>
            {
                new List<KeyDef> {
                    new KeyDef(KI(Key.Escape), "Esc", mr: baseSize),
                    new KeyDef(KI(Key.F1), "F1"), new KeyDef(KI(Key.F2), "F2"), new KeyDef(KI(Key.F3), "F3"), new KeyDef(KI(Key.F4), "F4", mr: baseSize * 0.5),
                    new KeyDef(KI(Key.F5), "F5"), new KeyDef(KI(Key.F6), "F6"), new KeyDef(KI(Key.F7), "F7"), new KeyDef(KI(Key.F8), "F8", mr: baseSize * 0.5),
                    new KeyDef(KI(Key.F9), "F9"), new KeyDef(KI(Key.F10), "F10"), new KeyDef(KI(Key.F11), "F11"), new KeyDef(KI(Key.F12), "F12")
                },
                new List<KeyDef> {
                    new KeyDef(KI(Key.OemTilde), "`"), new KeyDef(KI(Key.D1), "1"), new KeyDef(KI(Key.D2), "2"), new KeyDef(KI(Key.D3), "3"), new KeyDef(KI(Key.D4), "4"),
                    new KeyDef(KI(Key.D5), "5"), new KeyDef(KI(Key.D6), "6"), new KeyDef(KI(Key.D7), "7"), new KeyDef(KI(Key.D8), "8"), new KeyDef(KI(Key.D9), "9"),
                    new KeyDef(KI(Key.D0), "0"), new KeyDef(KI(Key.OemMinus), "-"), new KeyDef(KI(Key.OemPlus), "="), new KeyDef(KI(Key.Back), "Backspace", 2)
                },
                new List<KeyDef> {
                    new KeyDef(KI(Key.Tab), "Tab", 1.5), new KeyDef(KI(Key.Q), "Q"), new KeyDef(KI(Key.W), "W"), new KeyDef(KI(Key.E), "E"), new KeyDef(KI(Key.R), "R"),
                    new KeyDef(KI(Key.T), "T"), new KeyDef(KI(Key.Y), "Y"), new KeyDef(KI(Key.U), "U"), new KeyDef(KI(Key.I), "I"), new KeyDef(KI(Key.O), "O"),
                    new KeyDef(KI(Key.P), "P"), new KeyDef(KI(Key.OemOpenBrackets), "["), new KeyDef(KI(Key.OemCloseBrackets), "]"), new KeyDef(KI(Key.OemPipe), "\\", 1.5)
                },
                new List<KeyDef> {
                    new KeyDef(KI(Key.CapsLock), "Caps", 1.75), new KeyDef(KI(Key.A), "A"), new KeyDef(KI(Key.S), "S"), new KeyDef(KI(Key.D), "D"), new KeyDef(KI(Key.F), "F"),
                    new KeyDef(KI(Key.G), "G"), new KeyDef(KI(Key.H), "H"), new KeyDef(KI(Key.J), "J"), new KeyDef(KI(Key.K), "K"), new KeyDef(KI(Key.L), "L"),
                    new KeyDef(KI(Key.OemSemicolon), ";"), new KeyDef(KI(Key.OemQuotes), "'"), new KeyDef(KI(Key.Enter), "Enter", 2.25)
                },
                new List<KeyDef> {
                    new KeyDef(KI(Key.LeftShift), "Shift", 2.25), new KeyDef(KI(Key.Z), "Z"), new KeyDef(KI(Key.X), "X"), new KeyDef(KI(Key.C), "C"), new KeyDef(KI(Key.V), "V"),
                    new KeyDef(KI(Key.B), "B"), new KeyDef(KI(Key.N), "N"), new KeyDef(KI(Key.M), "M"), new KeyDef(KI(Key.OemComma), ","), new KeyDef(KI(Key.OemPeriod), "."),
                    new KeyDef(KI(Key.OemQuestion), "/"), new KeyDef(KI(Key.RightShift), "Shift", 2.75)
                },
                new List<KeyDef> {
                    new KeyDef(KI(Key.LeftCtrl), "Ctrl", 1.25), new KeyDef(KI(Key.LWin), "Win", 1.25), new KeyDef(KI(Key.LeftAlt), "Alt", 1.25),
                    new KeyDef(KI(Key.Space), "Space", 6.25), new KeyDef(KI(Key.RightAlt), "Alt", 1.25), new KeyDef(KI(Key.RWin), "Win", 1.25),
                    new KeyDef(KI(Key.Apps), "Menu", 1.25), new KeyDef(KI(Key.RightCtrl), "Ctrl", 1.25)
                }
            };

            foreach (var row in mainRows)
            {
                var spRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, margin, 0, margin) };
                foreach (var k in row)
                {
                    double w = baseSize * k.Width + (k.Width - 1) * margin * 2;
                    var border = CreateKeyUI(k.KeyId, k.Label, w, baseSize, margin, k.MarginRight);
                    spRow.Children.Add(border);
                }
                MainBlock.Children.Add(spRow);
            }

            // --- NAV BLOCK ---
            // Height of the mouse-buttons area in Viewbox units = 8 (margin-top) + 48 (height) = 56
            double mouseAreaH = 56;
            for(int i=0; i<3; i++) NavBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(baseSize + margin*2) });
            for(int i=0; i<6; i++) NavBlock.RowDefinitions.Add(new RowDefinition { Height = new GridLength(baseSize + margin*2) });
            NavBlock.RowDefinitions.Add(new RowDefinition { Height = new GridLength(mouseAreaH) }); // row 6: aligns with mouse buttons

            void AddToGrid(Grid g, string keyId, string label, int row, int col, int rowSpan = 1, int colSpan = 1)
            {
                double w = (baseSize * colSpan) + (colSpan - 1) * margin * 2;
                double h = (baseSize * rowSpan) + (rowSpan - 1) * margin * 2;
                var b = CreateKeyUI(keyId, label, w, h, margin, 0);
                Grid.SetRow(b, row);
                Grid.SetColumn(b, col);
                if (rowSpan > 1) Grid.SetRowSpan(b, rowSpan);
                if (colSpan > 1) Grid.SetColumnSpan(b, colSpan);
                g.Children.Add(b);
            }

            // Row 0: PrtSc (F-key level)
            AddToGrid(NavBlock, KI(Key.PrintScreen), "PrtSc", 0, 0); AddToGrid(NavBlock, KI(Key.Scroll), "ScrLk", 0, 1); AddToGrid(NavBlock, KI(Key.Pause), "Pause", 0, 2);
            // Row 1: Ins/Home/PgUp
            AddToGrid(NavBlock, KI(Key.Insert), "Ins", 1, 0); AddToGrid(NavBlock, KI(Key.Home), "Home", 1, 1); AddToGrid(NavBlock, KI(Key.PageUp), "PgUp", 1, 2);
            // Row 2: Del/End/PgDn
            AddToGrid(NavBlock, KI(Key.Delete), "Del", 2, 0); AddToGrid(NavBlock, KI(Key.End), "End", 2, 1); AddToGrid(NavBlock, KI(Key.PageDown), "PgDn", 2, 2);
            // Rows 3, 4: empty spacers (aligns with ASDF + ZXCV rows)
            // Row 5: Up arrow (aligns with Ctrl/Space row)
            AddToGrid(NavBlock, KI(Key.Up), "🡡", 4, 1);
            AddToGrid(NavBlock, KI(Key.Left), "🡠", 5, 0);
            AddToGrid(NavBlock, KI(Key.Down), "🡣", 5, 1);
            AddToGrid(NavBlock, KI(Key.Right), "🡢", 5, 2);

            // --- NUMPAD BLOCK ---
            for(int i=0; i<4; i++) NumpadBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(baseSize + margin*2) });
            for(int i=0; i<6; i++) NumpadBlock.RowDefinitions.Add(new RowDefinition { Height = new GridLength(baseSize + margin*2) });
            NumpadBlock.RowDefinitions.Add(new RowDefinition { Height = new GridLength(mouseAreaH) }); // row 6: bottom spacer to align with main keyboard

            // Row 0: empty (aligns with F-key row gap)
            AddToGrid(NumpadBlock, KI(Key.NumLock), "Num", 1, 0); AddToGrid(NumpadBlock, KI(Key.Divide), "/", 1, 1); AddToGrid(NumpadBlock, KI(Key.Multiply), "*", 1, 2); AddToGrid(NumpadBlock, KI(Key.Subtract), "-", 1, 3);
            AddToGrid(NumpadBlock, KI(Key.NumPad7), "7", 2, 0); AddToGrid(NumpadBlock, KI(Key.NumPad8), "8", 2, 1); AddToGrid(NumpadBlock, KI(Key.NumPad9), "9", 2, 2); AddToGrid(NumpadBlock, KI(Key.Add), "+", 2, 3, 2, 1);
            AddToGrid(NumpadBlock, KI(Key.NumPad4), "4", 3, 0); AddToGrid(NumpadBlock, KI(Key.NumPad5), "5", 3, 1); AddToGrid(NumpadBlock, KI(Key.NumPad6), "6", 3, 2); 
            AddToGrid(NumpadBlock, KI(Key.NumPad1), "1", 4, 0); AddToGrid(NumpadBlock, KI(Key.NumPad2), "2", 4, 1); AddToGrid(NumpadBlock, KI(Key.NumPad3), "3", 4, 2); AddToGrid(NumpadBlock, NumpadEnterKeyId, "Ent", 4, 3, 2, 1);
            AddToGrid(NumpadBlock, KI(Key.NumPad0), "0", 5, 0, 1, 2); AddToGrid(NumpadBlock, KI(Key.Decimal), ".", 5, 2);
            // Row 6: empty spacer - aligns numpad bottom with main keyboard + mouse buttons bottom
        }

        private Border CreateKeyUI(string keyId, string label, double width, double height, double margin, double marginRight)
        {
            var border = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(margin, margin, margin + marginRight, margin)
            };

            var grid = new Grid();
            
            var tbMain = new TextBlock
            {
                Text = label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.Medium
            };
            
            var tbCount = new TextBlock
            {
                Text = "",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 2)
            };

            grid.Children.Add(tbMain);
            grid.Children.Add(tbCount);
            border.Child = grid;

            var info = new KeyUIInfo
            {
                Border = border,
                MainText = tbMain,
                CountText = tbCount,
                Label = label
            };
            
            if (!keyInfos.ContainsKey(keyId))
            {
                keyInfos.Add(keyId, info);
            }

            UpdateKeyAppearance(info);
            return border;
        }

        private const int WmInput = 0x00FF;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const uint RidInput = 0x10000003;
        private const int RimTypeKeyboard = 1;
        private const ushort RiKeyE0 = 0x02;
        private const string NumpadEnterKeyId = "Key_NumPadEnter";
        private const ushort VkReturn = 0x0D;
        private const ushort VkShift = 0x10;
        private const ushort VkControl = 0x11;
        private const ushort VkMenu = 0x12;
        private const ushort VkLShift = 0xA0;
        private const ushort VkRShift = 0xA1;
        private const ushort VkLControl = 0xA2;
        private const ushort VkRControl = 0xA3;
        private const ushort VkLMenu = 0xA4;
        private const ushort VkRMenu = 0xA5;
        private const uint MapvkVscToVkEx = 0x03;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            RAWINPUTDEVICE[] pRawInputDevices,
            uint uiNumDevices,
            uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            IntPtr hRawInput,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize,
            uint cbSizeHeader);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(ushort uCode, uint uMapType);

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUT
        {
            public RAWINPUTHEADER header;
            public RAWKEYBOARD keyboard;
        }
    }
}





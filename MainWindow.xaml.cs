using System.Windows;
using System.Windows.Controls;
using ComputerTestApp.Views;

namespace ComputerTestApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            NavListBox.SelectedIndex = 0;
            UpdateNavigationSelectionStyles();
            UpdateLanguageButtons();
            UpdateThemeToggle();
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var isDark = !ThemeService.IsDark;
            _ = Dispatcher.BeginInvoke((System.Action)(() =>
            {
                ThemeService.SetDarkMode(isDark);
                UpdateThemeToggle();
                UpdateNavigationSelectionStyles();
            }));
        }

        private void UpdateThemeToggle()
        {
            ThemeToggleButton.SetResourceReference(
                ContentControl.ContentProperty,
                ThemeService.IsDark ? "EnableLightMode" : "EnableDarkMode");
        }

        private void VietnameseButton_Click(object sender, RoutedEventArgs e)
        {
            SetLanguage("vi");
        }

        private void EnglishButton_Click(object sender, RoutedEventArgs e)
        {
            SetLanguage("en");
        }

        private void SetLanguage(string language)
        {
            LocalizationService.SetLanguage(language);
            UpdateLanguageButtons();
        }

        private void UpdateLanguageButtons()
        {
            var isVietnamese = LocalizationService.CurrentLanguage == "vi";
            VietnameseButton.Style = (Style)FindResource(isVietnamese ? "ButtonPrimary" : "ButtonDefault");
            EnglishButton.Style = (Style)FindResource(isVietnamese ? "ButtonDefault" : "ButtonPrimary");
        }

        private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainContent == null) return;

            var item = NavListBox.SelectedItem as ListBoxItem;
            switch (item?.Tag?.ToString())
            {
                case "Keyboard":
                    MainContent.Content = new KeyboardTestControl();
                    break;
                case "Screen":
                    MainContent.Content = new ScreenTestControl();
                    break;
                case "Speaker":
                    MainContent.Content = new SoundTestControl();
                    break;
                case "Mic":
                    MainContent.Content = new MicTestControl();
                    break;
                case "Webcam":
                    MainContent.Content = new WebcamTestControl();
                    break;
                case "Usb":
                    MainContent.Content = new UsbTestControl();
                    break;
            }

            UpdateNavigationSelectionStyles();
        }

        private void UpdateNavigationSelectionStyles()
        {
            foreach (var entry in NavListBox.Items)
            {
                var item = entry as ListBoxItem;
                if (item == null) continue;

                if (item.IsSelected)
                {
                    item.SetResourceReference(Control.BackgroundProperty, "PrimaryBrush");
                    item.SetResourceReference(Control.ForegroundProperty, "ReverseTextBrush");
                    continue;
                }

                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
            }
        }
    }
}

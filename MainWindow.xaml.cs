using System;
using System.Diagnostics;
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
            UpdateWindowTitle();
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

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/mhqb365/ComputerTestApp");
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            CheckUpdateButton.SetResourceReference(ContentControl.ContentProperty, "CheckingUpdate");

            try
            {
                var result = await UpdateService.CheckLatestReleaseAsync();
                HandleUpdateResult(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Format("UpdateCheckErrorMessage", ex.Message),
                    LocalizationService.Get("UpdateCheckErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
                CheckUpdateButton.SetResourceReference(ContentControl.ContentProperty, "CheckUpdate");
            }
        }

        private void HandleUpdateResult(UpdateCheckResult result)
        {
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    var answer = MessageBox.Show(
                        LocalizationService.Format(
                            "UpdateAvailableMessage",
                            UpdateService.DisplayVersion,
                            result.LatestVersion,
                            result.Release?.TagName ?? result.Release?.Name),
                        LocalizationService.Get("UpdateAvailableTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (answer == MessageBoxResult.Yes)
                    {
                        OpenUrl(result.Release?.HtmlUrl ?? "https://github.com/mhqb365/ComputerTestApp/releases/latest");
                    }
                    break;
                case UpdateCheckStatus.UpToDate:
                    MessageBox.Show(
                        LocalizationService.Format("NoUpdateMessage", UpdateService.DisplayVersion),
                        LocalizationService.Get("NoUpdateTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;
                case UpdateCheckStatus.NoRelease:
                    MessageBox.Show(
                        LocalizationService.Get("NoReleaseMessage"),
                        LocalizationService.Get("NoUpdateTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;
                default:
                    var releaseUrl = result.Release?.HtmlUrl ?? "https://github.com/mhqb365/ComputerTestApp/releases/latest";
                    var openRelease = MessageBox.Show(
                        LocalizationService.Get("UnknownReleaseVersionMessage"),
                        LocalizationService.Get("UpdateAvailableTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    if (openRelease == MessageBoxResult.Yes)
                    {
                        OpenUrl(releaseUrl);
                    }
                    break;
            }
        }

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
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
            UpdateWindowTitle();
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

        private void UpdateWindowTitle()
        {
            Title = $"{LocalizationService.Get("AppTitle")} v{UpdateService.DisplayVersion}";
        }
    }
}

using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
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
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;

            if (!NetworkInterface.GetIsNetworkAvailable()) return;

            await CheckForUpdatesAsync(false);
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
            await CheckForUpdatesAsync(true);
        }

        private async Task CheckForUpdatesAsync(bool showInformationalMessages)
        {
            CheckUpdateButton.IsEnabled = false;
            CheckUpdateButton.SetResourceReference(ContentControl.ContentProperty, "CheckingUpdate");

            try
            {
                var result = await UpdateService.CheckLatestReleaseAsync();
                await HandleUpdateResultAsync(result, showInformationalMessages);
            }
            catch (Exception ex)
            {
                if (!showInformationalMessages) return;

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

        private async Task HandleUpdateResultAsync(UpdateCheckResult result, bool showInformationalMessages)
        {
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    var updateNow = ShowUpdateAvailableDialog(result);

                    if (updateNow)
                    {
                        var cts = new CancellationTokenSource();
                        var progressWindow = new UpdateProgressWindow(cts)
                        {
                            Owner = this
                        };

                        var progress = new Progress<UpdateProgressInfo>(info =>
                        {
                            progressWindow.UpdateProgress(info);
                        });

                        PreparedUpdate update = null;
                        bool isCanceled = false;

                        // Run download/prepare task in background thread to keep UI responsive
                        var downloadTask = Task.Run(async () =>
                        {
                            try
                            {
                                update = await UpdateService.DownloadAndPrepareUpdateAsync(result.Release, progress, cts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                isCanceled = true;
                            }
                            finally
                            {
                                // Close progress window on UI thread when done/canceled
                                _ = Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    if (progressWindow.IsLoaded)
                                    {
                                        progressWindow.Close();
                                    }
                                }));
                            }
                        });

                        // Show progress dialog as modal (blocks MainWindow inputs)
                        progressWindow.ShowDialog();

                        // If dialog closed but download is still running (e.g. closed window manually), cancel it
                        if (!downloadTask.IsCompleted)
                        {
                            cts.Cancel();
                            try
                            {
                                await downloadTask;
                            }
                            catch (Exception) { }
                        }

                        if (isCanceled)
                        {
                            MessageBox.Show(
                                LocalizationService.Get("UpdateCanceled"),
                                LocalizationService.Get("NoUpdateTitle"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else if (update != null)
                        {
                            MessageBox.Show(
                                LocalizationService.Get("UpdateReadyMessage"),
                                LocalizationService.Get("UpdateAvailableTitle"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            UpdateService.InstallPreparedUpdate(update);
                        }
                        else
                        {
                            // If update failed for other reasons (e.g. network/io exceptions), rethrow to catch block
                            try
                            {
                                await downloadTask;
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(
                                    LocalizationService.Format("UpdateFailed", ex.Message),
                                    LocalizationService.Get("UpdateCheckErrorTitle"),
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                            }
                        }
                    }
                    break;
                case UpdateCheckStatus.UpToDate:
                    if (!showInformationalMessages) break;

                    MessageBox.Show(
                        LocalizationService.Format("NoUpdateMessage", UpdateService.DisplayVersion),
                        LocalizationService.Get("NoUpdateTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;
                case UpdateCheckStatus.NoRelease:
                    if (!showInformationalMessages) break;

                    MessageBox.Show(
                        LocalizationService.Get("NoReleaseMessage"),
                        LocalizationService.Get("NoUpdateTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;
                default:
                    if (!showInformationalMessages) break;

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

        private bool ShowUpdateAvailableDialog(UpdateCheckResult result)
        {
            var dialog = new Window
            {
                Title = LocalizationService.Get("UpdateAvailableTitle"),
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(20),
                Width = 420
            };

            panel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Format(
                    "UpdateAvailableMessage",
                    UpdateService.DisplayVersion,
                    result.DisplayLatestVersion,
                    result.Release?.TagName ?? result.Release?.Name),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var updateButton = new Button
            {
                Content = LocalizationService.Get("UpdateNow"),
                Width = 120,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            var skipButton = new Button
            {
                Content = LocalizationService.Get("SkipUpdate"),
                Width = 120,
                Height = 34,
                IsCancel = true
            };

            updateButton.Click += (sender, args) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };
            skipButton.Click += (sender, args) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttons.Children.Add(updateButton);
            buttons.Children.Add(skipButton);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true;
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
                case "SystemInfo":
                    MainContent.Content = new SystemInfoControl();
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
                case "Battery":
                    MainContent.Content = new BatteryTestControl();
                    break;
                case "Disk":
                    MainContent.Content = new DiskTestControl();
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


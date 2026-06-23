using System;
using System.Threading;
using System.Windows;

namespace ComputerTestApp.Views
{
    public partial class UpdateProgressWindow : Window
    {
        private readonly CancellationTokenSource _cts;

        public UpdateProgressWindow(CancellationTokenSource cts)
        {
            InitializeComponent();
            _cts = cts;

            // Set localized labels
            TitleLabel.Text = LocalizationService.Get("CheckUpdate");
            CancelBtn.Content = LocalizationService.Get("Cancel");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cts.Cancel();
                CancelBtn.IsEnabled = false;
                StatusLabel.Text = LocalizationService.Get("UpdateCanceled");
                PercentLabel.Text = "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error canceling update: {ex.Message}");
            }
        }

        internal void UpdateProgress(UpdateProgressInfo info)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)(() => UpdateProgress(info)));
                return;
            }

            switch (info.State)
            {
                case UpdateProgressState.Downloading:
                    UpdateProgressBar.IsIndeterminate = false;
                    UpdateProgressBar.Value = info.Percentage;
                    StatusLabel.Text = LocalizationService.Get("DownloadingUpdateProgressLabel");
                    PercentLabel.Text = $"{info.Percentage}%";
                    break;
                case UpdateProgressState.Extracting:
                    UpdateProgressBar.IsIndeterminate = true;
                    StatusLabel.Text = LocalizationService.Get("ExtractingUpdate");
                    PercentLabel.Text = "";
                    CancelBtn.IsEnabled = false; // Disable canceling when extraction starts
                    break;
                case UpdateProgressState.Preparing:
                    UpdateProgressBar.IsIndeterminate = true;
                    StatusLabel.Text = LocalizationService.Get("PreparingUpdate");
                    PercentLabel.Text = "";
                    CancelBtn.IsEnabled = false;
                    break;
            }
        }
    }
}

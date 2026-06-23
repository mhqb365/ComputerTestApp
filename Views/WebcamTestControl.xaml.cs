using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AForge.Video;
using AForge.Video.DirectShow;

namespace ComputerTestApp.Views
{
    public partial class WebcamTestControl : UserControl
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;

        public WebcamTestControl()
        {
            InitializeComponent();
            LoadCameras();
        }

        private void LoadCameras()
        {
            try
            {
                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                foreach (FilterInfo device in videoDevices)
                {
                    CameraList.Items.Add(device.Name);
                }
                if (CameraList.Items.Count > 0)
                {
                    CameraList.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Format("CameraListErrorFormat", ex.Message), LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartCamera_Click(object sender, RoutedEventArgs e)
        {
            if (CameraList.SelectedIndex >= 0)
            {
                StopCamera();

                videoSource = new VideoCaptureDevice(videoDevices[CameraList.SelectedIndex].MonikerString);
                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();
            }
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    var ms = new MemoryStream();
                    bitmap.Save(ms, ImageFormat.Bmp);
                    ms.Seek(0, SeekOrigin.Begin);
                    bi.StreamSource = ms;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze(); // Need to freeze so it can be accessed across threads

                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (!ReferenceEquals(videoSource, sender)) return;

                        CameraImage.Source = bi;
                        CameraPreview.Visibility = Visibility.Visible;
                    }));
                }
            }
            catch { }
        }

        private void StopCamera_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
        }

        private void StopCamera()
        {
            var source = videoSource;
            videoSource = null;

            if (source != null)
            {
                if (source.IsRunning)
                {
                    source.SignalToStop();
                    source.WaitForStop();
                }

                source.NewFrame -= VideoSource_NewFrame;
            }

            CameraImage.Source = null;
            CameraPreview.Visibility = Visibility.Collapsed;
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopCamera();
        }
    }
}

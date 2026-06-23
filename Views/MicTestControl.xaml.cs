using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ComputerTestApp.Views
{
    public partial class MicTestControl : UserControl
    {
        private WaveInEvent waveIn;
        private MemoryStream recordedAudio;
        private WaveFormat recordedFormat;
        private WaveOutEvent playback;
        private RawSourceWaveStream playbackStream;
        private MMDevice microphoneEndpoint;
        private bool isUpdatingSensitivity;

        public MicTestControl()
        {
            InitializeComponent();
            LoadMicrophones();
        }

        private void LoadMicrophones()
        {
            try
            {
                var captureEndpoints = LoadCaptureEndpoints();
                for (var deviceNumber = 0; deviceNumber < WaveIn.DeviceCount; deviceNumber++)
                {
                    var capabilities = WaveIn.GetCapabilities(deviceNumber);
                    MicrophoneList.Items.Add(new MicrophoneDeviceInfo
                    {
                        Name = capabilities.ProductName,
                        WaveInDeviceNumber = deviceNumber,
                        CoreAudioDeviceId = FindCoreAudioDeviceId(capabilities.ProductName, captureEndpoints)
                    });
                }

                if (MicrophoneList.Items.Count > 0)
                {
                    MicrophoneList.SelectedIndex = 0;
                    return;
                }

                StartButton.IsEnabled = false;
                SetStatusResource("NoMicrophone");
            }
            catch (Exception ex)
            {
                StartButton.IsEnabled = false;
                SetStatusResource("MicrophoneListError");
                MessageBox.Show(LocalizationService.Format("MicrophoneErrorFormat", ex.Message), LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartMic_Click(object sender, RoutedEventArgs e)
        {
            var selectedDevice = MicrophoneList.SelectedItem as MicrophoneDeviceInfo;
            if (waveIn != null || selectedDevice == null) return;

            try
            {
                StopPlayback();
                recordedAudio?.Dispose();
                recordedAudio = new MemoryStream();
                waveIn = new WaveInEvent
                {
                    DeviceNumber = selectedDevice.WaveInDeviceNumber
                };
                recordedFormat = waveIn.WaveFormat;
                waveIn.DataAvailable += WaveIn_DataAvailable;
                waveIn.StartRecording();
                MicrophoneList.IsEnabled = false;
                PlaybackButton.IsEnabled = false;
                SetStatusResource("RecordingNow");
            }
            catch (Exception ex)
            {
                if (waveIn != null)
                {
                    waveIn.DataAvailable -= WaveIn_DataAvailable;
                    waveIn.Dispose();
                    waveIn = null;
                }

                recordedAudio?.Dispose();
                recordedAudio = null;
                recordedFormat = null;
                MicrophoneList.IsEnabled = true;
                PlaybackButton.IsEnabled = false;
                SetStatusResource("NoRecording");
                MessageBox.Show(LocalizationService.Format("MicrophoneErrorFormat", ex.Message), LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            recordedAudio.Write(e.Buffer, 0, e.BytesRecorded);
            float max = 0;
            for (int index = 0; index < e.BytesRecorded; index += 2)
            {
                short sample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index]);
                var sample32 = sample / 32768f;
                if (sample32 < 0) sample32 = -sample32;
                if (sample32 > max) max = sample32;
            }

            Dispatcher.Invoke(() =>
            {
                VolumeMeter.Value = max;
            });
        }

        private static List<CaptureEndpointInfo> LoadCaptureEndpoints()
        {
            var endpoints = new List<CaptureEndpointInfo>();
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    {
                        endpoints.Add(new CaptureEndpointInfo
                        {
                            Id = device.ID,
                            Name = device.FriendlyName
                        });
                        device.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not enumerate capture endpoints: " + ex.Message);
            }

            return endpoints;
        }

        private static string FindCoreAudioDeviceId(string waveInName, IEnumerable<CaptureEndpointInfo> endpoints)
        {
            var normalizedWaveInName = NormalizeDeviceName(waveInName);
            var bestMatch = endpoints
                .Select(endpoint => new
                {
                    Endpoint = endpoint,
                    Score = CommonPrefixLength(normalizedWaveInName, NormalizeDeviceName(endpoint.Name))
                })
                .OrderByDescending(match => match.Score)
                .FirstOrDefault();

            if (bestMatch == null || bestMatch.Score < Math.Min(12, normalizedWaveInName.Length)) return null;
            return bestMatch.Endpoint.Id;
        }

        private static string NormalizeDeviceName(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value ?? string.Empty)
            {
                if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        private static int CommonPrefixLength(string left, string right)
        {
            var length = Math.Min(left.Length, right.Length);
            var index = 0;
            while (index < length && left[index] == right[index]) index++;
            return index;
        }

        private void MicrophoneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DisposeMicrophoneEndpoint();
            MicrophoneSensitivitySlider.IsEnabled = false;
            SensitivityPercentText.SetResourceReference(TextBlock.TextProperty, "Unavailable");

            var selectedDevice = MicrophoneList.SelectedItem as MicrophoneDeviceInfo;
            if (string.IsNullOrWhiteSpace(selectedDevice?.CoreAudioDeviceId)) return;

            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    microphoneEndpoint = enumerator.GetDevice(selectedDevice.CoreAudioDeviceId);
                }

                microphoneEndpoint.AudioEndpointVolume.OnVolumeNotification += MicrophoneEndpoint_OnVolumeNotification;
                MicrophoneSensitivitySlider.IsEnabled = true;
                UpdateSensitivityDisplay(microphoneEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not initialize microphone sensitivity: " + ex.Message);
                DisposeMicrophoneEndpoint();
            }
        }

        private void MicrophoneSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingSensitivity || microphoneEndpoint == null) return;

            try
            {
                microphoneEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(e.NewValue / 100);
                SensitivityPercentText.Text = $"{Math.Round(e.NewValue)}%";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not change microphone sensitivity: " + ex.Message);
            }
        }

        private void MicrophoneEndpoint_OnVolumeNotification(AudioVolumeNotificationData data)
        {
            if (Dispatcher.HasShutdownStarted) return;
            _ = Dispatcher.BeginInvoke(new Action(() => UpdateSensitivityDisplay(data.MasterVolume * 100)));
        }

        private void UpdateSensitivityDisplay(double sensitivity)
        {
            isUpdatingSensitivity = true;
            MicrophoneSensitivitySlider.Value = sensitivity;
            SensitivityPercentText.Text = $"{Math.Round(sensitivity)}%";
            isUpdatingSensitivity = false;
        }

        private void DisposeMicrophoneEndpoint()
        {
            if (microphoneEndpoint == null) return;

            microphoneEndpoint.AudioEndpointVolume.OnVolumeNotification -= MicrophoneEndpoint_OnVolumeNotification;
            microphoneEndpoint.Dispose();
            microphoneEndpoint = null;
        }

        private void StopMic_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
        }

        private void StopRecording()
        {
            if (waveIn != null)
            {
                waveIn.DataAvailable -= WaveIn_DataAvailable;
                waveIn.StopRecording();
                waveIn.Dispose();
                waveIn = null;
                VolumeMeter.Value = 0;
                MicrophoneList.IsEnabled = true;
                PlaybackButton.IsEnabled = recordedAudio != null && recordedAudio.Length > 0;
                SetStatusResource(PlaybackButton.IsEnabled ? "RecordingReady" : "NoRecording");
            }
        }

        private void Playback_Click(object sender, RoutedEventArgs e)
        {
            if (playback != null)
            {
                StopPlayback();
                return;
            }

            if (recordedAudio == null || recordedAudio.Length == 0 || recordedFormat == null) return;

            try
            {
                playbackStream = new RawSourceWaveStream(new MemoryStream(recordedAudio.ToArray()), recordedFormat);
                playback = new WaveOutEvent();
                playback.PlaybackStopped += Playback_PlaybackStopped;
                playback.Init(playbackStream);
                playback.Play();
                SetPlaybackButtonResource("StopPlayback");
                SetStatusResource("PlaybackNow");
            }
            catch (Exception ex)
            {
                StopPlayback();
                MessageBox.Show(LocalizationService.Format("PlaybackErrorFormat", ex.Message), LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Playback_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(StopPlayback));
        }

        private void StopPlayback()
        {
            if (playback != null)
            {
                playback.PlaybackStopped -= Playback_PlaybackStopped;
                playback.Stop();
                playback.Dispose();
                playback = null;
            }

            playbackStream?.Dispose();
            playbackStream = null;
            SetPlaybackButtonResource("Playback");
            SetStatusResource(recordedAudio != null && recordedAudio.Length > 0 ? "RecordingReady" : "NoRecording");
        }

        private void SetStatusResource(string resourceKey)
        {
            StatusText.SetResourceReference(TextBlock.TextProperty, resourceKey);
        }

        private void SetPlaybackButtonResource(string resourceKey)
        {
            PlaybackButton.SetResourceReference(ContentControl.ContentProperty, resourceKey);
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopRecording();
            StopPlayback();
            DisposeMicrophoneEndpoint();
            recordedAudio?.Dispose();
        }

        private sealed class MicrophoneDeviceInfo
        {
            public string Name { get; set; }
            public int WaveInDeviceNumber { get; set; }
            public string CoreAudioDeviceId { get; set; }

            public override string ToString()
            {
                return Name;
            }
        }

        private sealed class CaptureEndpointInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
}

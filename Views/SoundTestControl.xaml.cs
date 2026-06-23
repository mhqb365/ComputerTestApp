using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ComputerTestApp.Views
{
    public partial class SoundTestControl : UserControl
    {
        private WaveOutEvent waveOut;
        private WaveOutEvent sampleWaveOut;
        private AudioFileReader sampleAudioReader;
        private MMDevice audioDevice;
        private float? activePan;
        private bool isUpdatingVolume;

        public SoundTestControl()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeSystemVolume();
        }

        private void InitializeSystemVolume()
        {
            if (audioDevice != null) return;

            SystemVolumeSlider.IsEnabled = true;
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    audioDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }

                audioDevice.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolume_OnVolumeNotification;
                UpdateAudioEndpointDisplay(
                    audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100,
                    audioDevice.AudioEndpointVolume.Mute);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not initialize system volume: " + ex.Message);
                if (audioDevice != null)
                {
                    audioDevice.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
                }
                audioDevice?.Dispose();
                audioDevice = null;
                SystemVolumeSlider.IsEnabled = false;
                SpeakerMuteButton.IsEnabled = false;
                VolumePercentText.SetResourceReference(TextBlock.TextProperty, "Unavailable");
                SpeakerMuteStatusText.SetResourceReference(TextBlock.TextProperty, "Unavailable");
                SpeakerMuteButton.SetResourceReference(ContentControl.ContentProperty, "Unavailable");
            }
        }

        private void SystemVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingVolume || audioDevice == null) return;

            try
            {
                audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(e.NewValue / 100);
                VolumePercentText.Text = $"{Math.Round(e.NewValue)}%";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not change system volume: " + ex.Message);
            }
        }

        private void AudioEndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data)
        {
            if (Dispatcher.HasShutdownStarted) return;

            _ = Dispatcher.BeginInvoke(new Action(() => UpdateAudioEndpointDisplay(data.MasterVolume * 100, data.Muted)));
        }

        private void UpdateAudioEndpointDisplay(double volume, bool isMuted)
        {
            isUpdatingVolume = true;
            SystemVolumeSlider.Value = volume;
            VolumePercentText.Text = $"{Math.Round(volume)}%";
            isUpdatingVolume = false;
            UpdateSpeakerMuteDisplay(isMuted);
        }

        private void UpdateSpeakerMuteDisplay(bool isMuted)
        {
            SpeakerMuteStatusText.SetResourceReference(TextBlock.TextProperty, isMuted ? "SpeakerMuted" : "SpeakerOn");
            SpeakerMuteButton.SetResourceReference(ContentControl.ContentProperty, isMuted ? "UnmuteSpeaker" : "MuteSpeaker");
        }

        private void SpeakerMuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioDevice == null) return;

            try
            {
                audioDevice.AudioEndpointVolume.Mute = !audioDevice.AudioEndpointVolume.Mute;
                UpdateSpeakerMuteDisplay(audioDevice.AudioEndpointVolume.Mute);
            }
            catch (Exception ex)
            {
                StatusText.Text = LocalizationService.Format("ErrorFormat", ex.Message);
            }
        }

        private void PlayLeft_Click(object sender, RoutedEventArgs e)
        {
            ToggleTone(-1f, "PlayingLeft");
        }

        private void PlayRight_Click(object sender, RoutedEventArgs e)
        {
            ToggleTone(1f, "PlayingRight");
        }

        private void ToggleTone(float pan, string statusResourceKey)
        {
            if (activePan == pan)
            {
                StopTone();
                return;
            }

            StopTone();
            StopSampleAudio();

            try
            {
                var sine = new SignalGenerator(44100, 1)
                {
                    Gain = 0.2,
                    Frequency = 440,
                    Type = SignalGeneratorType.Sin
                };

                waveOut = new WaveOutEvent();
                waveOut.Init(new PanningSampleProvider(sine) { Pan = pan });
                waveOut.Play();
                activePan = pan;
                StatusText.SetResourceReference(TextBlock.TextProperty, statusResourceKey);
                UpdateButtonLabels();
            }
            catch (Exception ex)
            {
                StopTone();
                StatusText.Text = LocalizationService.Format("ErrorFormat", ex.Message);
            }
        }

        private void StopTone()
        {
            if (waveOut != null)
            {
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
            }

            activePan = null;
            StatusText.SetResourceReference(TextBlock.TextProperty, "SpeakerReady");
            UpdateButtonLabels();
        }

        private void SampleAudioButton_Click(object sender, RoutedEventArgs e)
        {
            if (sampleWaveOut != null)
            {
                StopSampleAudio();
                return;
            }

            StopTone();
            PlaySampleAudio();
        }

        private void PlaySampleAudio()
        {
            try
            {
                var audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sound", "openmindaudio-vlog-outro.mp3");
                if (!File.Exists(audioPath))
                {
                    StatusText.Text = LocalizationService.Format("ErrorFormat", LocalizationService.Get("SampleAudioNotFound"));
                    return;
                }

                sampleAudioReader = new AudioFileReader(audioPath);
                sampleWaveOut = new WaveOutEvent();
                sampleWaveOut.PlaybackStopped += SampleWaveOut_PlaybackStopped;
                sampleWaveOut.Init(sampleAudioReader);
                sampleWaveOut.Play();
                SampleAudioButton.SetResourceReference(ContentControl.ContentProperty, "StopSampleAudio");
                StatusText.SetResourceReference(TextBlock.TextProperty, "PlayingSampleAudio");
            }
            catch (Exception ex)
            {
                StopSampleAudio();
                StatusText.Text = LocalizationService.Format("ErrorFormat", ex.Message);
            }
        }

        private void SampleWaveOut_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(StopSampleAudio));
        }

        private void StopSampleAudio()
        {
            if (sampleWaveOut != null)
            {
                sampleWaveOut.PlaybackStopped -= SampleWaveOut_PlaybackStopped;
                sampleWaveOut.Stop();
                sampleWaveOut.Dispose();
                sampleWaveOut = null;
            }

            sampleAudioReader?.Dispose();
            sampleAudioReader = null;
            SampleAudioButton.SetResourceReference(ContentControl.ContentProperty, "PlaySampleAudio");
            StatusText.SetResourceReference(TextBlock.TextProperty, "SpeakerReady");
        }

        private void UpdateButtonLabels()
        {
            LeftButton.SetResourceReference(ContentControl.ContentProperty, activePan == -1f ? "StopLeft" : "PlayLeft");
            RightButton.SetResourceReference(ContentControl.ContentProperty, activePan == 1f ? "StopRight" : "PlayRight");
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopTone();
            StopSampleAudio();
            if (audioDevice == null) return;

            audioDevice.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
            audioDevice.Dispose();
            audioDevice = null;
        }
    }
}

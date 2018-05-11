using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using Windows.Media.MediaProperties;
using Microsoft.ProjectOxford.SpeakerRecognition;
using Microsoft.ProjectOxford.SpeakerRecognition.Contract.Verification;
using System.Diagnostics;



// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace VoiceAuth
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        MediaCapture capture;
        InMemoryRandomAccessStream buffer;
        bool record;
        string filename;
        string audioFile = "faceauth.wav";

        //Register for a free Speaker Recognition API key and URL at https://azure.microsoft.com/en-us/try/cognitive-services/
        static string voiceApiKey = "<Insert your API key here>";

        SpeakerVerificationServiceClient voiceServiceClient = new SpeakerVerificationServiceClient(voiceApiKey);

        public MainPage()
        {
            this.InitializeComponent();
        }

        private async Task<bool> RecordProcess()
        {
            if (buffer != null)
            {
                buffer.Dispose();
            }
            buffer = new InMemoryRandomAccessStream();
            if (capture != null)
            {
                capture.Dispose();
            }
            try
            {
                MediaCaptureInitializationSettings settings = new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Audio
                };
                capture = new MediaCapture();
                await capture.InitializeAsync(settings);
                capture.RecordLimitationExceeded += (MediaCapture sender) =>
                {
                    //Stop 
                    //   await capture.StopRecordAsync(); 
                    record = false;
                    throw new Exception("Record Limitation Exceeded ");
                };
                capture.Failed += (MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs) =>
                {
                    record = false;
                    throw new Exception(string.Format("Code: {0}. {1}", errorEventArgs.Code, errorEventArgs.Message));
                };
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.GetType() == typeof(UnauthorizedAccessException))
                {
                    throw ex.InnerException;
                }
                throw;
            }
            return true;
        }

        public async Task PlayRecordedAudio(CoreDispatcher UiDispatcher)
        {
            playBtn.IsEnabled = false;
            MediaElement playback = new MediaElement();

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add(".wav");

            try
            {
                Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();

                IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
                playback.SetSource(stream, file.FileType);
                playback.Play();

            }
            catch
            {
                // User cancelled operation
                playBtn.IsEnabled = true;
            }

            playBtn.IsEnabled = true;

        }

        private async void recordBtn_Click(object sender, RoutedEventArgs e)
        {
            if (record)
            {
                //recording already in progress
            }
            else
            {
                await RecordProcess();
                //await capture.StartRecordToStreamAsync(MediaEncodingProfile.CreateMp3(AudioEncodingQuality.Auto), buffer);

                var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
                
                profile.Audio = AudioEncodingProperties.CreatePcm(16000, 1, 16);

                await capture.StartRecordToStreamAsync(profile, buffer);
                if (record)
                {
                    throw new InvalidOperationException();
                }
                record = true;
                recordBtn.IsEnabled = false;
            }

        }

        private async void stopBtn_Click(object sender, RoutedEventArgs e)
        {
            if (record == true)
            {
                await capture.StopRecordAsync();

                await SaveRecordedAudio(Dispatcher);

                record = false;
                recordBtn.IsEnabled = true;
            }

        }

        public async Task SaveRecordedAudio(CoreDispatcher UiDispatcher)
        {
            IRandomAccessStream audio = buffer.CloneStream();

            if (audio == null)
                throw new ArgumentNullException("buffer");

            //StorageFolder storageFolder = Windows.ApplicationModel.Package.Current.InstalledLocation;
            /*
            StorageFolder storageFolder = await DownloadsFolder.;
            if (!string.IsNullOrEmpty(filename))
            {
                StorageFile original = await storageFolder.GetFileAsync(filename);
                await original.DeleteAsync();
            }
            */
            await UiDispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                //StorageFile storageFile = await storageFolder.CreateFileAsync(audioFile, CreationCollisionOption.GenerateUniqueName);
                StorageFile storageFile = await DownloadsFolder.CreateFileAsync(audioFile, CreationCollisionOption.GenerateUniqueName);
                filename = storageFile.Name;
                using (IRandomAccessStream fileStream = await storageFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    await RandomAccessStream.CopyAndCloseAsync(audio.GetInputStreamAt(0), fileStream.GetOutputStreamAt(0));
                    await audio.FlushAsync();
                    audio.Dispose();
                }
                //IRandomAccessStream stream = await storageFile.OpenAsync(FileAccessMode.Read);

                LogMessage($"File {storageFile.Name} saved to {storageFile.Path}");
            });
        }

        private async void playBtn_Click(object sender, RoutedEventArgs e)
        {
            await PlayRecordedAudio(Dispatcher);
        }

        //Enrollment audio requirements
        //https://westus.dev.cognitive.microsoft.com/docs/services/563309b6778daf02acc0a508/operations/5645c3271984551c84ec6797

        private async void enrollBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add(".wav");

            Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                // Application now has read/write access to the picked file
                txtBoxOutput.Text = "Picked: " + file.Name;
            }
            else
            {
                //User has cancelled operation
                return;
            }

            //Convert to stream
            var audioStream = await file.OpenStreamForReadAsync();

            Guid speakerId = new Guid(txtBoxGuid.Text.ToString());

            try
            {
                Enrollment response = await voiceServiceClient.EnrollAsync(audioStream, speakerId);

                if (response.EnrollmentStatus == Microsoft.ProjectOxford.SpeakerRecognition.Contract.EnrollmentStatus.Enrolled)
                {
                    // enrollment successful
                    LogMessage($"Enrolled.");
                }

                if (response.EnrollmentStatus == Microsoft.ProjectOxford.SpeakerRecognition.Contract.EnrollmentStatus.Enrolling)
                {
                    // enrollment successful
                    LogMessage($"Enrolling.");
                }
            }
            catch (Exception error)
            {
                LogMessage($"{error.Message}");
            }
        }

        private async void validateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
                picker.FileTypeFilter.Add(".wav");

                Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    // Application now has read/write access to the picked file
                    txtBoxOutput.Text = "Picked: " + file.Name;
                }
                else
                {
                    //User has cancelled operation
                    return;
                }

                //Convert to stream
                var audioStream = await file.OpenStreamForReadAsync();

                Guid speakerId = new Guid(txtBoxGuid.Text.ToString());

                var response = await voiceServiceClient.VerifyAsync(audioStream, speakerId);

                LogMessage($"Result: {response.Result}");
                LogMessage($"Confidence: {response.Confidence}");
                LogMessage($"Phrase: {response.Phrase}");
            }
            catch (Exception error)
            {
                LogMessage($"{error.Message}");
            }

        }

        private async void verifyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Guid speakerId = new Guid(txtBoxGuid.Text.ToString());

                var response = await voiceServiceClient.GetProfileAsync(speakerId);

                LogMessage($"ProfileId: {response.ProfileId}");
                LogMessage($"EnrollmentStatus: {response.EnrollmentStatus}");
                LogMessage($"Locale: {response.Locale}");
                LogMessage($"EnrollmentsCount: {response.EnrollmentsCount}");
                LogMessage($"RemainingEnrollmentsCount: {response.RemainingEnrollmentsCount}");
            }
            catch (Exception error)
            {
                LogMessage($"{error.Message}");
            }


        }

        private async void createProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var response = await voiceServiceClient.CreateProfileAsync("en-us");

                txtBoxGuid.Text = response.ProfileId.ToString();

                LogMessage($"Profile created: {response.ProfileId}");
            }
            catch (Exception error)
            {
                LogMessage($"{error.Message}");
            }

        }

        private async void getProfilesBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                var response = await voiceServiceClient.GetProfilesAsync();

                foreach (Profile profile in response)
                {
                    LogMessage($"ProfileId: {profile.ProfileId}  EnrollmentStatus: {profile.EnrollmentStatus}");
                }
            }
            catch (Exception error)
            {
                LogMessage($"{error.Message}");
            }
        }

        private void txtBoxOutput_TextChanged(object sender, TextChangedEventArgs e)
        {
            var grid = (Grid)VisualTreeHelper.GetChild(txtBoxOutput, 0);
            for (var i = 0; i <= VisualTreeHelper.GetChildrenCount(grid) - 1; i++)
            {
                object obj = VisualTreeHelper.GetChild(grid, i);
                if (!(obj is ScrollViewer)) continue;
                ((ScrollViewer)obj).ChangeView(0.0f, ((ScrollViewer)obj).ExtentHeight, 1.0f);
                break;
            }
        }

        private void LogMessage(string message)
        {
            txtBoxOutput.Text += $"{message}\n";
            Debug.WriteLine($"{message}");
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Media.Core;
using GalaSoft.MvvmLight.Command;
using Windows.Media.Capture.Frames;
using System.Diagnostics;
using Windows.Media.Capture;
using Windows.Media.Playback;
using Windows.Media.Effects;
using Windows.Media;
using Windows.Foundation.Collections;
using Windows.AI.MachineLearning;
using Windows.Storage;
using System.Runtime.CompilerServices;

namespace StyleTransfer
{
    class AppViewModel : INotifyPropertyChanged
    {
        public AppViewModel()
        {
            _appModel = new AppModel();
            // TODO: Add a param to check if have an image to save. 
            SaveCommand = new RelayCommand(this.SaveOutput);
            MediaSourceCommand = new RelayCommand<string>(SetMediaSource);
            LiveStreamCommand = new RelayCommand(async () => await StartWebcamStream());
            ChangeMediaInputCommand = new RelayCommand(async () => await ChangeMediaInput());
        }

        Windows.Media.Capture.MediaCapture _mediaCapture;
        private List<MediaFrameSourceGroup> _mediaFrameSourceGroupList;
        private MediaFrameSourceGroup _selectedMediaFrameSourceGroup;
        private MediaFrameSource _selectedMediaFrameSource;

        private LearningModel m_model = null;
        private LearningModelDeviceKind m_inferenceDeviceSelected = LearningModelDeviceKind.Default;
        private LearningModelSession m_session;
        string _inputImageDescription;
        string _outputImageDescription;
        IMediaExtension videoEffect;

        private AppModel _appModel;
        public AppModel CurrentApp
        {
            get { return _appModel; }
        }

        private ICommand _saveCommand;
        public ICommand SaveCommand
        {
            get { return _saveCommand; }
            set
            {
                _saveCommand = value;
            }
        }

        public ICommand MediaSourceCommand { get; set; }
        public ICommand LiveStreamCommand { get; set; }
        public ICommand ChangeMediaInputCommand { get; set; }

        private int _selectedCameraIndex;
        public int SelectedCameraIndex
        {
            get { return _selectedCameraIndex; }
            set
            {
                _selectedCameraIndex = value;
                ChangeMediaInput();
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void SaveOutput()
        {
            // TODO: Take from UIButtonSaveImage_Click
            return;
        }

        public async Task StartWebcamStream()
        {
            try
            {
                // Find the sources 
                var allGroups = await MediaFrameSourceGroup.FindAllAsync();
                _mediaFrameSourceGroupList = allGroups.Where(group => group.SourceInfos.Any(sourceInfo => sourceInfo.SourceKind == MediaFrameSourceKind.Color
                                                                                                         && (sourceInfo.MediaStreamType == MediaStreamType.VideoPreview
                                                                                                         || sourceInfo.MediaStreamType == MediaStreamType.VideoRecord))).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                _mediaFrameSourceGroupList = null;
            }

            if ((_mediaFrameSourceGroupList == null) || (_mediaFrameSourceGroupList.Count == 0))
            {
                // No camera sources found
                Debug.WriteLine("No Camera found");
                return;
            }

            _appModel.CameraNamesList = _mediaFrameSourceGroupList.Select(group => group.DisplayName);
            SelectedCameraIndex = 0;
        }

        private async Task LoadModelAsync()
        {

            StorageFile modelFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/candy.onnx"));
            m_model = await LearningModel.LoadFromStorageFileAsync(modelFile);

            // TODO: Pass in useGPU as well. OR decide which side of binary these go on. 
            //m_inferenceDeviceSelected = _useGPU ? LearningModelDeviceKind.DirectXHighPerformance : LearningModelDeviceKind.Cpu;
            m_inferenceDeviceSelected = LearningModelDeviceKind.Cpu;
            m_session = new LearningModelSession(m_model, new LearningModelDevice(m_inferenceDeviceSelected));

            debugIO();

            _inputImageDescription = m_model.InputFeatures.ToList().First().Name;
            //m_model.InputFeatures.FirstOrDefault(feature => feature.Kind == LearningModelFeatureKind.Tensor)
            //as ImageFeatureDescriptor;

            _outputImageDescription = m_model.OutputFeatures.ToList().First().Name;
            //m_model.OutputFeatures.FirstOrDefault(feature => feature.Kind == LearningModelFeatureKind.Tensor)
            //as ImageFeatureDescriptor;
        }

        public void debugIO()
        {
            uint m_inWidth, m_inHeight, m_outWidth, m_outHeight;
            string m_inName, m_outName;
            foreach (var inputF in m_model.InputFeatures)
            {
                Debug.WriteLine($"input | kind:{inputF.Kind}, name:{inputF.Name}, type:{inputF.GetType()}");
                int i = 0;
                ImageFeatureDescriptor imgDesc = inputF as ImageFeatureDescriptor;
                TensorFeatureDescriptor tfDesc = inputF as TensorFeatureDescriptor;
                m_inWidth = (uint)(imgDesc == null ? tfDesc.Shape[3] : imgDesc.Width);
                m_inHeight = (uint)(imgDesc == null ? tfDesc.Shape[2] : imgDesc.Height);
                m_inName = inputF.Name;

                Debug.WriteLine($"N: {(imgDesc == null ? tfDesc.Shape[0] : 1)}, " +
                    $"Channel: {(imgDesc == null ? tfDesc.Shape[1].ToString() : imgDesc.BitmapPixelFormat.ToString())}, " +
                    $"Height:{(imgDesc == null ? tfDesc.Shape[2] : imgDesc.Height)}, " +
                    $"Width: {(imgDesc == null ? tfDesc.Shape[3] : imgDesc.Width)}");
            }
            foreach (var outputF in m_model.OutputFeatures)
            {
                Debug.WriteLine($"output | kind:{outputF.Kind}, name:{outputF.Name}, type:{outputF.GetType()}");
                int i = 0;
                ImageFeatureDescriptor imgDesc = outputF as ImageFeatureDescriptor;
                TensorFeatureDescriptor tfDesc = outputF as TensorFeatureDescriptor;
                m_outWidth = (uint)(imgDesc == null ? tfDesc.Shape[3] : imgDesc.Width);
                m_outHeight = (uint)(imgDesc == null ? tfDesc.Shape[2] : imgDesc.Height);
                m_outName = outputF.Name;

                Debug.WriteLine($"N: {(imgDesc == null ? tfDesc.Shape[0] : 1)}, " +
                   $"Channel: {(imgDesc == null ? tfDesc.Shape[1].ToString() : imgDesc.BitmapPixelFormat.ToString())}, " +
                   $"Height:{(imgDesc == null ? tfDesc.Shape[2] : imgDesc.Height)}, " +
                   $"Width: {(imgDesc == null ? tfDesc.Shape[3] : imgDesc.Width)}");
            }
        }
        public void SetMediaSource(object obj)
        {
            // TODO: Convert to a better value for the appModel object here. 
            _appModel.InputMedia = obj.ToString();

            // If video source, whole different code flow

            // If camera/image upload, gather input image then put to eval/render
            // If source == upload --> HelperMethods::LoadVideoFrameFromFilePickedAsync

            // Process everythign and then set MediaPlkayer object in AppModel for input/output
            return;
        }

        private void StartPreview()
        {

            Debug.WriteLine("StartPreview");
            _selectedMediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source => source.Value.Info.MediaStreamType == MediaStreamType.VideoPreview
                                                                                  && source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;
            if (_selectedMediaFrameSource == null)
            {
                _selectedMediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source => source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord
                                                                                      && source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;
            }

            // if no preview stream are available, bail
            if (_selectedMediaFrameSource == null)
            {
                return;
            }

            var _mediaPlayer = new MediaPlayer();
            _mediaPlayer.RealTimePlayback = true;
            _mediaPlayer.AutoPlay = true;
            _appModel.OutputMediaSource = MediaSource.CreateFromMediaFrameSource(_selectedMediaFrameSource);
            _appModel.InputMediaSource = MediaSource.CreateFromMediaFrameSource(_selectedMediaFrameSource);

            //UIInputMediaPlayerElement.SetMediaPlayer(_mediaPlayer);
            //UIOutputMediaPlayerElement.SetMediaPlayer(_mediaPlayer);

            /*UITxtBlockPreviewProperties.Text = string.Format("{0}x{1}@{2}, {3}",
                        _selectedMediaFrameSource.CurrentFormat.VideoFormat.Width,
                        _selectedMediaFrameSource.CurrentFormat.VideoFormat.Height,
                        _selectedMediaFrameSource.CurrentFormat.FrameRate.Numerator + "/" + _selectedMediaFrameSource.CurrentFormat.FrameRate.Denominator,
                        _selectedMediaFrameSource.CurrentFormat.Subtype);

            UICameraSelectionControls.Visibility = Visibility.Visible;
            UIInputMediaPlayerElement.Visibility = Visibility.Visible;
            UIOutputMediaPlayerElement.Visibility = Visibility.Visible;*/

        }

        private async Task ChangeMediaInput()
        {
            // UICmbCamera_SelectionChanged

            //await CleanupCameraAsync();

            try
            {
                _selectedMediaFrameSourceGroup = _mediaFrameSourceGroupList[SelectedCameraIndex];

                // Create MediaCapture and its settings
                _mediaCapture = new MediaCapture();
                var settings = new MediaCaptureInitializationSettings
                {
                    SourceGroup = _selectedMediaFrameSourceGroup,
                    PhotoCaptureSource = PhotoCaptureSource.Auto,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video
                };

                // Initialize MediaCapture
                await _mediaCapture.InitializeAsync(settings);
                await LoadModelAsync();

                // Initialize VideoEffect
                VideoEffectDefinition videoEffectDefinition = new VideoEffectDefinition("StyleTransferEffectComponent.StyleTransferVideoEffect");
                videoEffect = await _mediaCapture.AddVideoEffectAsync(videoEffectDefinition, MediaStreamType.VideoPreview);
                // Try loading the model here and passing as a property instead
                videoEffect.SetProperties(new PropertySet() {
                    { "Model", m_model},
                    { "Session", m_session },
                    { "InputImageDescription", _inputImageDescription },
                    { "OutputImageDescription", _outputImageDescription } });

                StartPreview();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        private async Task CleanupCameraAsync()
        {
            Debug.WriteLine("CleanupCameraAsync");

            try
            {
                if (_mediaCapture != null)
                {
                    if (videoEffect != null) await _mediaCapture.RemoveEffectAsync(videoEffect);
                    await _mediaCapture.StopRecordAsync();
                    _mediaCapture = null;
                }
                if (_appModel.OutputMediaSource != null)
                {
                    _appModel.OutputMediaSource = null;
                }
                if (videoEffect != null)
                {
                    videoEffect = null;
                }

                // Add inputmediasource once can show both at the same time
            }

            catch (Exception ex)
            {
                Debug.WriteLine($"CleanupCameraAsync: {ex.Message}");
            }

        }
    }
}

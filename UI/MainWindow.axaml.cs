using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Aviscribe.UI
{
    public partial class MainWindow : Avalonia.Controls.Window
    {
        private IVideoCapture? _video;
        private readonly IVideoProvider _videoProvider;

        private FrameProcessor? _processor;

        private Image? _previewImage;
        private VideoDevice? _currentDevice;
        bool updatePreview = false;

        public MainWindow(IVideoProvider provider)
        {
            InitializeComponent();

            _videoProvider = provider;
            InitControls();
            InitFrameProcessor();
        }

        private void InitControls()
        {
            // Get video input devices and add to video select combobox
            var devices = _videoProvider.GetDevices();

            ComboBox inputSelect = this.GetControl<ComboBox>("cbInputSelect");
            inputSelect.ItemsSource = devices;
            inputSelect.DisplayMemberBinding = new Avalonia.Data.Binding("Name");

            // Update Preview button
            Button updatePreview = this.GetControl<Button>("btnUpdatePreview");
            updatePreview.Click += StartPreview;

            // Get image preview control
            _previewImage = this.FindControl<Image>("imgPreview");
        }

        private void InitFrameProcessor()
        {
            var repo = MoonRepository.LoadDefault();

            var matcher = new MoonMatcher(
                repo,
                GameLanguage.ChineseTraditional,
                GameLanguage.English
            ); // #TODO allow language selection

            var state = new GameState();
            state.SetKingdom("Cascade"); // #TODO allow manual selection // #TODO add automatic detection

            //var ocr = new TesseractOcrService("chi_tra"); // #TODO allow language selection
            var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath); // #TODO allow language selection

            _processor = new FrameProcessor(ocr, matcher, state);
        }

        private void OnFrame(VideoFrame frame)
        {
            //frame.Frame.SaveImage("C:\\users\\amaho\\Downloads\\current.png");
            _processor!.PushFrame(frame);

            if (updatePreview)
            {
                UpdatePreview(frame.Frame);
                updatePreview = false;
            }

            //frame.Frame.Dispose();
        }

        private void StartPreview(object? sender, RoutedEventArgs args)
        {
            ComboBox inputSelect = this.GetControl<ComboBox>("cbInputSelect");
            VideoDevice? selected = inputSelect.SelectedItem as VideoDevice;
            if (selected == null) return;

            updatePreview = true;
            if (_currentDevice == null || _currentDevice.Id != selected.Id)
            {
                _processor?.Stop();
                _video?.Stop();

                _currentDevice = selected;
                _video = _videoProvider.GetVideoCapture(selected.Id);
                _video.FrameReceived += OnFrame;

                _video.Start();
                _processor.Start();
            }
        }

        private void UpdatePreview(Mat frame)
        {
            if (frame.Empty())
                return;

            // Encode Mat once (fast + safe for UI)
            Cv2.ImEncode(".png", frame, out var buffer);

            using var stream = new MemoryStream(buffer);

            var bitmap = new Bitmap(stream);

            Dispatcher.UIThread.Post(() =>
            {
                _previewImage!.Source = bitmap;
            });
        }
    }
}
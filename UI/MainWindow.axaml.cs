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
        private readonly MoonRepository _repo;
        private readonly GameState _state = new();
        private readonly RunStateStore _stateStore;
        private readonly RunOutputWriter _outputWriter = new();
        private readonly Queue<AmbiguousOcrResult> _reviewQueue = new();
        private readonly HashSet<string> _reviewSignatures = new(StringComparer.Ordinal);

        private FrameProcessor? _processor;
        private AmbiguousOcrResult? _activeReview;

        private Image? _previewImage;
        private VideoDevice? _currentDevice;
        private TextBlock? _statusText;
        private TextBlock? _countedCountText;
        private TextBlock? _actualCountText;
        private TextBlock? _reviewPromptText;
        private ListBox? _pendingList;
        private ListBox? _collectedList;
        private ListBox? _uncountedList;
        private ComboBox? _kingdomSelect;
        private ComboBox? _moonSelect;
        private ComboBox? _reviewSelect;
        private ComboBox? _inputLanguageSelect;
        private ComboBox? _outputLanguageSelect;
        private CheckBox? _includePostGameCheck;
        private CheckBox? _writeOverlayCheck;
        private TextBox? _overlayPathText;
        private bool _processorRunning;
        private bool _writeOverlayEnabled = true;
        bool updatePreview = false;

        public MainWindow()
            : this(new DesignVideoProvider())
        {
        }

        public MainWindow(IVideoProvider provider)
        {
            InitializeComponent();

            _videoProvider = provider;
            _repo = MoonRepository.LoadDefault();
            _stateStore = new RunStateStore(_repo);
            LoadSavedRunState();
            _outputWriter.Language = _state.Settings.OutputLanguage;
            InitControls();
            InitFrameProcessor();
            _state.Changed += (_, _) => UpdateRunState();
            UpdateRunState();
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

            _kingdomSelect = this.GetControl<ComboBox>("cbKingdomSelect");
            _kingdomSelect.SelectionChanged += (_, _) =>
            {
                if (_kingdomSelect.SelectedItem is string kingdom)
                {
                    _state.SetKingdom(kingdom);
                    RefreshMoonSelect();
                }
            };

            ComboBox categorySelect = this.GetControl<ComboBox>("cbCategorySelect");
            categorySelect.ItemsSource = Enum.GetValues<RunCategory>();
            categorySelect.SelectedItem = _state.Settings.Category;
            categorySelect.SelectionChanged += (_, _) =>
            {
                if (categorySelect.SelectedItem is RunCategory category)
                {
                    _state.Settings.Category = category;
                    _state.NotifySettingsChanged();
                }
            };

            _inputLanguageSelect = this.GetControl<ComboBox>("cbInputLanguageSelect");
            _inputLanguageSelect.ItemsSource = Enum.GetValues<GameLanguage>();
            _inputLanguageSelect.SelectedItem = _state.Settings.InputLanguage;
            _inputLanguageSelect.SelectionChanged += (_, _) =>
            {
                if (_inputLanguageSelect.SelectedItem is GameLanguage language)
                {
                    _state.Settings.InputLanguage = language;
                    if (_processor != null)
                        RecreateFrameProcessor();
                    _state.NotifySettingsChanged();
                }
            };

            _outputLanguageSelect = this.GetControl<ComboBox>("cbOutputLanguageSelect");
            _outputLanguageSelect.ItemsSource = Enum.GetValues<GameLanguage>();
            _outputLanguageSelect.SelectedItem = _state.Settings.OutputLanguage;
            _outputLanguageSelect.SelectionChanged += (_, _) =>
            {
                if (_outputLanguageSelect.SelectedItem is GameLanguage language)
                {
                    _state.Settings.OutputLanguage = language;
                    _outputWriter.Language = language;
                    RefreshMoonSelect();
                    _state.NotifySettingsChanged();
                }
            };

            _includePostGameCheck = this.GetControl<CheckBox>("chkIncludePostGame");
            _includePostGameCheck.IsChecked = _state.Settings.IncludePostGameKingdoms;
            _includePostGameCheck.IsCheckedChanged += (_, _) =>
            {
                _state.Settings.IncludePostGameKingdoms = _includePostGameCheck.IsChecked == true;
                RefreshKingdoms();
                RefreshMoonSelect();
                _state.NotifySettingsChanged();
            };

            // Get image preview control
            _previewImage = this.FindControl<Image>("imgPreview");
            _statusText = this.FindControl<TextBlock>("txtStatus");
            _countedCountText = this.FindControl<TextBlock>("txtCountedCount");
            _actualCountText = this.FindControl<TextBlock>("txtActualCount");
            _pendingList = this.FindControl<ListBox>("lstPending");
            _collectedList = this.FindControl<ListBox>("lstCollected");
            _uncountedList = this.FindControl<ListBox>("lstUncounted");
            _moonSelect = this.FindControl<ComboBox>("cbMoonSelect");
            _reviewPromptText = this.FindControl<TextBlock>("txtReviewPrompt");
            _reviewSelect = this.FindControl<ComboBox>("cbReviewSelect");
            _writeOverlayCheck = this.FindControl<CheckBox>("chkWriteOverlay");
            _overlayPathText = this.FindControl<TextBox>("txtOverlayPath");

            if (_overlayPathText != null)
            {
                _overlayPathText.Text = _outputWriter.OutputPath;
                _overlayPathText.TextChanged += (_, _) =>
                {
                    _outputWriter.OutputPath = _overlayPathText.Text ?? string.Empty;
                    var snapshot = _state.CreateSnapshot();
                    WriteOverlayOutput(snapshot);
                    PersistRunState(snapshot);
                };
            }

            if (_writeOverlayCheck != null)
            {
                _writeOverlayCheck.IsChecked = _writeOverlayEnabled;
                _writeOverlayCheck.IsCheckedChanged += (_, _) =>
                {
                    _writeOverlayEnabled = _writeOverlayCheck.IsChecked == true;
                    var snapshot = _state.CreateSnapshot();
                    WriteOverlayOutput(snapshot);
                    PersistRunState(snapshot);
                };
            }

            WireExclusiveListSelection();

            this.GetControl<Button>("btnManualPending").Click += (_, _) =>
            {
                if (GetSelectedMoon() is { } moon)
                    _state.AddPending(moon);
            };

            this.GetControl<Button>("btnManualCollected").Click += (_, _) =>
            {
                if (GetSelectedMoon() is { } moon)
                    _state.MarkCollected(moon);
            };

            this.GetControl<Button>("btnManualUncounted").Click += (_, _) =>
            {
                if (GetSelectedMoon() is { } moon)
                    _state.MarkUncounted(moon);
            };

            this.GetControl<Button>("btnManualRemove").Click += (_, _) =>
            {
                if (GetSelectedStateMoon() is { } moon)
                    _state.Remove(moon);
            };

            this.GetControl<Button>("btnResetKingdom").Click += (_, _) =>
            {
                _state.ResetKingdom();
            };

            this.GetControl<Button>("btnReviewApply").Click += (_, _) =>
            {
                if (_activeReview != null && _reviewSelect?.SelectedItem is ReviewCandidateItem item)
                {
                    ApplyReview(_activeReview.Type, item.Candidate.Moon);
                    CompleteActiveReview();
                }
            };

            this.GetControl<Button>("btnReviewDismiss").Click += (_, _) => CompleteActiveReview();

            RefreshKingdoms();

            if (_kingdomSelect.SelectedItem is string initialKingdom)
                _state.SetKingdom(initialKingdom);

            RefreshMoonSelect();
        }

        private void InitFrameProcessor()
        {
            var matcher = new MoonMatcher(
                _repo,
                _state.Settings.InputLanguage,
                _state.Settings.OutputLanguage
            );

            //var ocr = new TesseractOcrService("chi_tra");
            var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var detector = LoadTextPresenceDetector();

            _processor = new FrameProcessor(ocr, matcher, _state, detector);
            _processor.AmbiguousMatchReceived += (_, result) => EnqueueReview(result);
        }

        private void RecreateFrameProcessor()
        {
            var wasRunning = _processorRunning;

            _processor?.Stop();
            InitFrameProcessor();

            if (wasRunning)
                _processor?.Start();
        }

        private ITextPresenceDetector LoadTextPresenceDetector()
        {
            return new HeuristicTextPresenceDetector();
        }

        private void OnFrame(VideoFrame frame)
        {
            //frame.Frame.SaveImage("[removed]");
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
                _processorRunning = false;

                _currentDevice = selected;
                _video = _videoProvider.GetVideoCapture(selected.Id);
                _video.FrameReceived += OnFrame;

                _video.Start();
                _processor?.Start();
                _processorRunning = true;
                SetStatus($"Watching {selected.Name}");
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

        private void UpdateRunState()
        {
            var snapshot = _state.CreateSnapshot();

            Dispatcher.UIThread.Post(() =>
            {
                if (_countedCountText != null)
                    _countedCountText.Text = snapshot.CountedMoonCount.ToString();

                if (_actualCountText != null)
                    _actualCountText.Text = snapshot.ActualMoonCount.ToString();

                if (_pendingList != null)
                    _pendingList.ItemsSource = snapshot.Pending.Select(CreateListItem).ToList();

                if (_collectedList != null)
                    _collectedList.ItemsSource = snapshot.Collected.Select(CreateListItem).ToList();

                if (_uncountedList != null)
                    _uncountedList.ItemsSource = snapshot.UncountedCollected.Select(CreateListItem).ToList();

                WriteOverlayOutput(snapshot);
                PersistRunState(snapshot);
            });
        }

        private void RefreshKingdoms()
        {
            if (_kingdomSelect == null)
                return;

            var kingdoms = _repo.GetKingdoms(_state.Settings.IncludePostGameKingdoms);
            var current = _kingdomSelect.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(current))
                current = _state.CurrentKingdom;

            _kingdomSelect.ItemsSource = kingdoms;
            _kingdomSelect.SelectedItem =
                kingdoms.FirstOrDefault(kingdom => string.Equals(kingdom, current, StringComparison.OrdinalIgnoreCase)) ??
                kingdoms.FirstOrDefault(kingdom => string.Equals(kingdom, "Cascade", StringComparison.OrdinalIgnoreCase)) ??
                kingdoms.FirstOrDefault();
        }

        private void RefreshMoonSelect()
        {
            if (_moonSelect == null || _state.CurrentKingdom.Length == 0)
                return;

            _moonSelect.ItemsSource = _repo
                .GetCollectionCandidates(_state.CurrentKingdom, _state.Settings)
                .OrderBy(moon => moon.English)
                .Select(CreateListItem)
                .ToList();
            _moonSelect.SelectedIndex = 0;
        }

        private void LoadSavedRunState()
        {
            try
            {
                var savedState = _stateStore.Load(AppPaths.RunStatePath);
                if (savedState == null)
                    return;

                _stateStore.Restore(_state, savedState);
                _writeOverlayEnabled = savedState.WriteOverlay;
                _outputWriter.OutputPath = string.IsNullOrWhiteSpace(savedState.OverlayOutputPath)
                    ? AppPaths.PendingOutputPath
                    : savedState.OverlayOutputPath;
            }
            catch
            {
                _writeOverlayEnabled = true;
                _outputWriter.OutputPath = AppPaths.PendingOutputPath;
            }
        }

        private void PersistRunState(GameStateSnapshot snapshot)
        {
            try
            {
                _stateStore.Save(
                    AppPaths.RunStatePath,
                    snapshot,
                    _writeOverlayEnabled,
                    _outputWriter.OutputPath);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not save run state: {ex.Message}");
            }
        }

        private void EnqueueReview(AmbiguousOcrResult result)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var signature = CreateReviewSignature(result);
                if (!_reviewSignatures.Add(signature))
                    return;

                _reviewQueue.Enqueue(result);

                if (_activeReview == null)
                    ShowNextReview();
            });
        }

        private void ShowNextReview()
        {
            if (_reviewQueue.Count == 0)
            {
                _activeReview = null;

                if (_reviewPromptText != null)
                    _reviewPromptText.Text = "No ambiguous reads";

                if (_reviewSelect != null)
                    _reviewSelect.ItemsSource = null;

                return;
            }

            _activeReview = _reviewQueue.Dequeue();

            if (_reviewPromptText != null)
                _reviewPromptText.Text = $"{DescribeReviewType(_activeReview.Type)} read: {_activeReview.Text}";

            if (_reviewSelect != null)
            {
                _reviewSelect.ItemsSource = _activeReview.Candidates
                    .Select(candidate => new ReviewCandidateItem(candidate, FormatCandidate(candidate)))
                    .ToList();
                _reviewSelect.SelectedIndex = 0;
            }
        }

        private void CompleteActiveReview()
        {
            if (_activeReview != null)
                _reviewSignatures.Remove(CreateReviewSignature(_activeReview));

            _activeReview = null;
            ShowNextReview();
        }

        private void ApplyReview(OcrRegionType type, Moon moon)
        {
            switch (type)
            {
                case OcrRegionType.Talkatoo:
                    _state.AddPending(moon);
                    SetStatus($"Added {moon.English}");
                    break;

                case OcrRegionType.MoonGet:
                case OcrRegionType.StoryMoon:
                    var outcome = _state.MarkCollected(moon);
                    SetStatus(outcome == CollectionOutcome.Uncounted
                        ? $"Tracked wrong moon: {moon.English}"
                        : $"Collected {moon.English}");
                    break;
            }
        }

        private static string CreateReviewSignature(AmbiguousOcrResult result)
        {
            var ids = string.Join(",", result.Candidates.Select(candidate => candidate.Moon.Id));
            return $"{result.Type}|{result.Text}|{ids}";
        }

        private static string DescribeReviewType(OcrRegionType type)
        {
            return type switch
            {
                OcrRegionType.Talkatoo => "Talkatoo",
                OcrRegionType.MoonGet => "Moon collected",
                OcrRegionType.StoryMoon => "Story moon",
                _ => "OCR"
            };
        }

        private string FormatCandidate(OcrMatchCandidate candidate)
        {
            return $"{FormatMoon(candidate.Moon)} - {candidate.Score:P0}";
        }

        private void WriteOverlayOutput(GameStateSnapshot snapshot)
        {
            if (_writeOverlayCheck?.IsChecked != true)
                return;

            try
            {
                _outputWriter.WritePending(snapshot);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not write overlay file: {ex.Message}");
            }
        }

        private Moon? GetSelectedMoon()
        {
            return _moonSelect?.SelectedItem is MoonListItem item ? item.Moon : null;
        }

        private Moon? GetSelectedStateMoon()
        {
            return (_pendingList?.SelectedItem as MoonListItem)?.Moon ??
                   (_collectedList?.SelectedItem as MoonListItem)?.Moon ??
                   (_uncountedList?.SelectedItem as MoonListItem)?.Moon;
        }

        private void WireExclusiveListSelection()
        {
            if (_pendingList != null)
            {
                _pendingList.SelectionChanged += (_, _) =>
                {
                    if (_pendingList.SelectedItem != null)
                    {
                        if (_collectedList != null) _collectedList.SelectedItem = null;
                        if (_uncountedList != null) _uncountedList.SelectedItem = null;
                    }
                };
            }

            if (_collectedList != null)
            {
                _collectedList.SelectionChanged += (_, _) =>
                {
                    if (_collectedList.SelectedItem != null)
                    {
                        if (_pendingList != null) _pendingList.SelectedItem = null;
                        if (_uncountedList != null) _uncountedList.SelectedItem = null;
                    }
                };
            }

            if (_uncountedList != null)
            {
                _uncountedList.SelectionChanged += (_, _) =>
                {
                    if (_uncountedList.SelectedItem != null)
                    {
                        if (_pendingList != null) _pendingList.SelectedItem = null;
                        if (_collectedList != null) _collectedList.SelectedItem = null;
                    }
                };
            }
        }

        private MoonListItem CreateListItem(Moon moon)
        {
            return new MoonListItem(moon, FormatMoon(moon));
        }

        private string FormatMoon(Moon moon)
        {
            return MoonDisplay.Format(moon, _state.Settings.OutputLanguage);
        }

        private void SetStatus(string text)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_statusText != null)
                    _statusText.Text = text;
            });
        }

        private sealed record MoonListItem(Moon Moon, string Label)
        {
            public override string ToString() => Label;
        }

        private sealed record ReviewCandidateItem(OcrMatchCandidate Candidate, string Label)
        {
            public override string ToString() => Label;
        }
    }
}

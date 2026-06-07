using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        private TextBlock? _countedCountText;
        private TextBlock? _actualCountText;
        private TextBlock? _requirementText;
        private TextBlock? _moonCountText;
        private TextBlock? _commandFeedbackText;
        private TextBlock? _reviewPromptText;
        private ListBox? _moonList;
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
        private TextBox? _moonNumberText;
        private TabControl? _mainTabs;
        private bool _processorRunning;
        private bool _writeOverlayEnabled = true;
        private string _captureDeviceId = string.Empty;
        private bool _updatingLists;
        private bool _dragStarted;
        private bool _suppressListClick;
        private Avalonia.Point _dragStartPoint;
        private ListBox? _dragSourceList;
        private ManualMoonTarget _dragSourceTarget;
        private Moon? _dragMoon;
        private ListBoxItem? _dragVisualItem;
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
            inputSelect.SelectedItem = devices.FirstOrDefault(device => device.Id == _captureDeviceId);
            inputSelect.SelectionChanged += (_, _) =>
            {
                if (inputSelect.SelectedItem is VideoDevice device)
                {
                    _captureDeviceId = device.Id;
                    PersistRunState(_state.CreateSnapshot());
                }
            };

            // Update Preview button
            Button updatePreview = this.GetControl<Button>("btnUpdatePreview");
            updatePreview.Click += StartPreview;
            this.GetControl<Button>("btnSettingsUpdatePreview").Click += StartPreview;

            _kingdomSelect = this.GetControl<ComboBox>("cbKingdomSelect");
            _kingdomSelect.SelectionChanged += (_, _) =>
            {
                if (_kingdomSelect.SelectedItem is KingdomListItem item)
                {
                    _state.SetKingdom(item.Kingdom);
                    RefreshMoonSelect();
                    RefreshMoonList();
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
                    RefreshMoonList();
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
                RefreshMoonList();
                _state.NotifySettingsChanged();
            };

            var woodedBeforeLakeCheck = this.GetControl<CheckBox>("chkWoodedBeforeLake");
            woodedBeforeLakeCheck.IsChecked = _state.Settings.WoodedBeforeLake;
            woodedBeforeLakeCheck.IsCheckedChanged += (_, _) =>
            {
                _state.Settings.WoodedBeforeLake = woodedBeforeLakeCheck.IsChecked == true;
                RefreshKingdoms();
                _state.NotifySettingsChanged();
            };

            var seasideBeforeSnowCheck = this.GetControl<CheckBox>("chkSeasideBeforeSnow");
            seasideBeforeSnowCheck.IsChecked = _state.Settings.SeasideBeforeSnow;
            seasideBeforeSnowCheck.IsCheckedChanged += (_, _) =>
            {
                _state.Settings.SeasideBeforeSnow = seasideBeforeSnowCheck.IsChecked == true;
                RefreshKingdoms();
                _state.NotifySettingsChanged();
            };

            ConfigureHotkeySelect(
                "cbFocusMoonHotkey",
                _state.Settings.FocusMoonNumberHotkey,
                key => _state.Settings.FocusMoonNumberHotkey = key);
            ConfigureHotkeySelect(
                "cbPendingHotkey",
                _state.Settings.MoveToPendingHotkey,
                key => _state.Settings.MoveToPendingHotkey = key);
            ConfigureHotkeySelect(
                "cbCountedHotkey",
                _state.Settings.MoveToCountedHotkey,
                key => _state.Settings.MoveToCountedHotkey = key);
            ConfigureHotkeySelect(
                "cbWrongHotkey",
                _state.Settings.MoveToWrongHotkey,
                key => _state.Settings.MoveToWrongHotkey = key);
            ConfigureHotkeySelect(
                "cbRemoveHotkey",
                _state.Settings.RemoveMoonHotkey,
                key => _state.Settings.RemoveMoonHotkey = key);

            // Get image preview control
            _previewImage = this.FindControl<Image>("imgPreview");
            _countedCountText = this.FindControl<TextBlock>("txtCountedCount");
            _actualCountText = this.FindControl<TextBlock>("txtActualCount");
            _requirementText = this.FindControl<TextBlock>("txtRequirement");
            _moonCountText = this.FindControl<TextBlock>("txtMoonCount");
            _commandFeedbackText = this.FindControl<TextBlock>("txtCommandFeedback");
            _moonList = this.FindControl<ListBox>("lstMoonList");
            _pendingList = this.FindControl<ListBox>("lstPending");
            _collectedList = this.FindControl<ListBox>("lstCollected");
            _uncountedList = this.FindControl<ListBox>("lstUncounted");
            _moonSelect = this.FindControl<ComboBox>("cbMoonSelect");
            _reviewPromptText = this.FindControl<TextBlock>("txtReviewPrompt");
            _reviewSelect = this.FindControl<ComboBox>("cbReviewSelect");
            _writeOverlayCheck = this.FindControl<CheckBox>("chkWriteOverlay");
            _overlayPathText = this.FindControl<TextBox>("txtOverlayPath");
            _moonNumberText = this.FindControl<TextBox>("txtMoonNumber");
            _mainTabs = this.FindControl<TabControl>("tabMain");

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

            WireListInteractions();
            WireCommandControls();
            AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

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

            if (_kingdomSelect.SelectedItem is KingdomListItem initialKingdom)
                _state.SetKingdom(initialKingdom.Kingdom);

            RefreshMoonSelect();
            RefreshMoonList();
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
                _processorRunning = false;

            _currentDevice = selected;
            _captureDeviceId = selected.Id;
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

                UpdateKingdomHeader(snapshot.CurrentKingdom);

                if (_pendingList != null)
                {
                    _updatingLists = true;
                    _pendingList.ItemsSource = snapshot.Pending.Select(CreateListItem).ToList();
                    _pendingList.SelectedItem = null;
                    _updatingLists = false;
                }

                if (_collectedList != null)
                {
                    _updatingLists = true;
                    _collectedList.ItemsSource = snapshot.Collected.Select(CreateListItem).ToList();
                    _collectedList.SelectedItem = null;
                    _updatingLists = false;
                }

                if (_uncountedList != null)
                {
                    _updatingLists = true;
                    _uncountedList.ItemsSource = snapshot.UncountedCollected.Select(CreateListItem).ToList();
                    _uncountedList.SelectedItem = null;
                    _updatingLists = false;
                }

                UpdateMoonListHighlights(snapshot);
                WriteOverlayOutput(snapshot);
                PersistRunState(snapshot);
            });
        }

        private void RefreshKingdoms()
        {
            if (_kingdomSelect == null)
                return;

            var kingdoms = _repo.GetKingdoms(_state.Settings);
            var current = (_kingdomSelect.SelectedItem as KingdomListItem)?.Kingdom;
            if (string.IsNullOrWhiteSpace(current))
                current = _state.CurrentKingdom;

            var items = kingdoms.Select(CreateKingdomListItem).ToList();
            _kingdomSelect.ItemsSource = items;
            _kingdomSelect.SelectedItem =
                items.FirstOrDefault(item => string.Equals(item.Kingdom, current, StringComparison.OrdinalIgnoreCase)) ??
                items.FirstOrDefault(item => string.Equals(item.Kingdom, "Cascade", StringComparison.OrdinalIgnoreCase)) ??
                items.FirstOrDefault();
        }

        private KingdomListItem CreateKingdomListItem(string kingdom)
        {
            var requirement = KingdomRoute.GetRequirement(kingdom);
            var label = _state.Settings.IncludePostGameKingdoms
                ? kingdom
                : $"{kingdom} ({requirement})";
            return new KingdomListItem(kingdom, label);
        }

        private void RefreshMoonSelect()
        {
            if (_moonSelect == null || _state.CurrentKingdom.Length == 0)
                return;

            _moonSelect.ItemsSource = _repo
                .GetCollectionCandidates(_state.CurrentKingdom, _state.Settings)
                .Select(CreateListItem)
                .ToList();
            _moonSelect.SelectedIndex = 0;
        }

        private void RefreshMoonList()
        {
            if (_moonList == null || _state.CurrentKingdom.Length == 0)
                return;

            _updatingLists = true;
            var moons = _repo
                .GetCollectionCandidates(_state.CurrentKingdom, _state.Settings)
                .Select(CreateListItem)
                .ToList();
            _moonList.ItemsSource = moons;
            if (_moonCountText != null)
                _moonCountText.Text = $"{moons.Count} moons";
            UpdateMoonListHighlights(_state.CreateSnapshot());
            _updatingLists = false;
        }

        private void UpdateMoonListHighlights(GameStateSnapshot snapshot)
        {
            if (_moonList?.ItemsSource is not IEnumerable<MoonListItem> items ||
                _moonList.SelectedItems == null)
                return;

            var tracked = snapshot.Pending
                .Concat(snapshot.Collected)
                .Concat(snapshot.UncountedCollected)
                .ToList();

            _moonList.SelectedItems.Clear();
            foreach (var item in items.Where(item =>
                         tracked.Any(moon => SameMoon(moon, item.Moon))))
            {
                _moonList.SelectedItems.Add(item);
            }
        }

        private void UpdateKingdomHeader(string kingdom)
        {
            if (_requirementText == null)
                return;

            var requirement = KingdomRoute.GetRequirement(kingdom);
            _requirementText.Text = _state.Settings.IncludePostGameKingdoms
                ? "Postgame mode"
                : requirement.ToString();
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
                _captureDeviceId = savedState.CaptureDeviceId;
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
                    _outputWriter.OutputPath,
                    _captureDeviceId);
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

        private void WireCommandControls()
        {
            this.GetControl<Button>("btnCommandPending").Click += (_, _) =>
                ApplyMoonNumberCommand(ManualMoonTarget.Pending);
            this.GetControl<Button>("btnCommandCollected").Click += (_, _) =>
                ApplyMoonNumberCommand(ManualMoonTarget.Collected);
            this.GetControl<Button>("btnCommandWrong").Click += (_, _) =>
                ApplyMoonNumberCommand(ManualMoonTarget.Uncounted);
            this.GetControl<Button>("btnCommandRemove").Click += (_, _) =>
                ApplyMoonNumberCommand(ManualMoonTarget.All);
        }

        private void ConfigureHotkeySelect(string controlName, string currentKey, Action<string> update)
        {
            var select = this.GetControl<ComboBox>(controlName);
            var keys = GetAssignableHotkeys();
            var selected = ParseHotkey(currentKey, keys[0]);
            select.ItemsSource = keys;
            select.SelectedItem = keys.Contains(selected) ? selected : keys[0];
            select.SelectionChanged += (_, _) =>
            {
                if (select.SelectedItem is Key key)
                {
                    update(key.ToString());
                    _state.NotifySettingsChanged();
                }
            };
        }

        private static List<Key> GetAssignableHotkeys()
        {
            return Enumerable.Range('A', 26)
                .Select(value => Enum.Parse<Key>(((char)value).ToString()))
                .Concat(Enumerable.Range(1, 12).Select(value => Enum.Parse<Key>($"F{value}")))
                .ToList();
        }

        private static Key ParseHotkey(string value, Key fallback)
        {
            return Enum.TryParse<Key>(value, true, out var key) ? key : fallback;
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (_mainTabs?.SelectedIndex != 0)
                return;

            if (e.Source is TextBox textBox && !ReferenceEquals(textBox, _moonNumberText))
                return;

            if (!ReferenceEquals(e.Source, _moonNumberText) &&
                e.Key == ParseHotkey(_state.Settings.FocusMoonNumberHotkey, Key.M))
            {
                _moonNumberText?.Focus();
                _moonNumberText?.SelectAll();
                e.Handled = true;
                return;
            }

            if (_moonNumberText?.IsFocused != true)
                return;

            if (e.Key == ParseHotkey(_state.Settings.MoveToPendingHotkey, Key.P) ||
                e.Key == Key.Enter)
            {
                ApplyMoonNumberCommand(ManualMoonTarget.Pending);
                e.Handled = true;
            }
            else if (e.Key == ParseHotkey(_state.Settings.MoveToCountedHotkey, Key.C))
            {
                ApplyMoonNumberCommand(ManualMoonTarget.Collected);
                e.Handled = true;
            }
            else if (e.Key == ParseHotkey(_state.Settings.MoveToWrongHotkey, Key.W))
            {
                ApplyMoonNumberCommand(ManualMoonTarget.Uncounted);
                e.Handled = true;
            }
            else if (e.Key == ParseHotkey(_state.Settings.RemoveMoonHotkey, Key.X) ||
                     e.Key == Key.Delete)
            {
                ApplyMoonNumberCommand(ManualMoonTarget.All);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _moonNumberText.Text = string.Empty;
                SetCommandFeedback("Moon command cleared");
                e.Handled = true;
            }
        }

        private void ApplyMoonNumberCommand(ManualMoonTarget target)
        {
            if (_moonNumberText == null ||
                !int.TryParse(_moonNumberText.Text, out var moonNumber))
            {
                SetCommandFeedback("Enter a moon number first");
                _moonNumberText?.Focus();
                return;
            }

            var candidates = _repo
                .GetCollectionCandidates(_state.CurrentKingdom, _state.Settings)
                .Where(moon => moon.Id == moonNumber)
                .ToList();

            var moon = candidates.FirstOrDefault(candidate =>
                    candidate.Kingdom.Equals(_state.CurrentKingdom, StringComparison.OrdinalIgnoreCase)) ??
                candidates.FirstOrDefault();

            if (moon == null)
            {
                SetCommandFeedback($"Moon #{moonNumber} is not in {_state.CurrentKingdom}");
                _moonNumberText.SelectAll();
                return;
            }

            switch (target)
            {
                case ManualMoonTarget.Pending:
                    _state.MoveToPending(moon);
                    SetCommandFeedback($"#{moonNumber} {FormatMoon(moon)} -> pending");
                    break;

                case ManualMoonTarget.Collected:
                    _state.MoveToCollected(moon);
                    SetCommandFeedback($"#{moonNumber} {FormatMoon(moon)} -> counted");
                    break;

                case ManualMoonTarget.Uncounted:
                    _state.MoveToUncounted(moon);
                    SetCommandFeedback($"#{moonNumber} {FormatMoon(moon)} -> wrong");
                    break;

                case ManualMoonTarget.All:
                    _state.Remove(moon);
                    SetCommandFeedback($"#{moonNumber} {FormatMoon(moon)} removed");
                    break;
            }

            _moonNumberText.Focus();
            _moonNumberText.SelectAll();
        }

        private void SetCommandFeedback(string text)
        {
            if (_commandFeedbackText != null)
                _commandFeedbackText.Text = text;
        }

        private void WireListInteractions()
        {
            WireInteractiveList(_moonList, ManualMoonTarget.All);
            WireInteractiveList(_pendingList, ManualMoonTarget.Pending);
            WireInteractiveList(_collectedList, ManualMoonTarget.Collected);
            WireInteractiveList(_uncountedList, ManualMoonTarget.Uncounted);
        }

        private void WireInteractiveList(ListBox? list, ManualMoonTarget target)
        {
            if (list == null)
                return;

            list.AddHandler(PointerPressedEvent, (_, e) =>
            {
                _dragMoon = null;
                _dragVisualItem = null;

                if (e.Source is Control sourceControl &&
                    FindAncestor<ListBoxItem>(sourceControl) is { DataContext: MoonListItem item } listItem)
                {
                    if (e.GetCurrentPoint(list).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
                    {
                        if (target != ManualMoonTarget.All)
                        {
                            _state.Remove(item.Moon);
                            SetStatus($"Removed {FormatMoon(item.Moon)}");
                        }

                        ClearListPointerState();
                        e.Handled = true;
                        return;
                    }

                    _dragMoon = item.Moon;
                    _dragVisualItem = listItem;
                }

                _dragSourceList = list;
                _dragSourceTarget = target;
                _dragStartPoint = e.GetPosition(list);
                _dragStarted = false;
                e.Pointer.Capture(list);
                e.Handled = true;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            list.AddHandler(PointerMovedEvent, (_, e) =>
            {
                if (_dragSourceList != list || _dragStarted)
                    return;

                var delta = e.GetPosition(list) - _dragStartPoint;
                if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
                    return;

                if (_dragMoon == null)
                    return;

                _dragStarted = true;
                _suppressListClick = true;
                if (_dragVisualItem != null)
                    _dragVisualItem.Opacity = 0.45;
                SetCommandFeedback($"Dragging {FormatMoon(_dragMoon)}");
                e.Handled = true;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            list.AddHandler(PointerReleasedEvent, (_, e) =>
            {
                e.Pointer.Capture(null);

                if (_dragStarted && _dragMoon != null)
                {
                    if (TryGetDropTarget(e.GetPosition(this), out var dropTarget))
                    {
                        MoveMoonToTarget(_dragMoon, _dragSourceTarget, dropTarget);
                    }

                    ClearListPointerState();
                    e.Handled = true;
                    return;
                }

                if (_suppressListClick)
                {
                    ClearListPointerState();
                    return;
                }

                if (_updatingLists || _dragStarted)
                {
                    ClearListPointerState();
                    return;
                }

                if (_dragMoon == null)
                {
                    ClearListPointerState();
                    return;
                }

                HandleListClick(target, _dragMoon);
                ClearListPointerState();
                e.Handled = true;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        private void ClearListPointerState()
        {
            if (_dragVisualItem != null)
                _dragVisualItem.Opacity = 1;

            _dragMoon = null;
            _dragVisualItem = null;
            _dragSourceList = null;
            _dragStarted = false;
            _suppressListClick = false;
        }

        private bool TryGetDropTarget(Avalonia.Point point, out ManualMoonTarget target)
        {
            object? current = this.InputHitTest(point);
            while (current != null)
            {
                if (ReferenceEquals(current, _moonList))
                {
                    target = ManualMoonTarget.All;
                    return true;
                }

                if (ReferenceEquals(current, _pendingList))
                {
                    target = ManualMoonTarget.Pending;
                    return true;
                }

                if (ReferenceEquals(current, _collectedList))
                {
                    target = ManualMoonTarget.Collected;
                    return true;
                }

                if (ReferenceEquals(current, _uncountedList))
                {
                    target = ManualMoonTarget.Uncounted;
                    return true;
                }

                current = (current as Control)?.Parent;
            }

            target = ManualMoonTarget.All;
            return false;
        }

        private void HandleListClick(ManualMoonTarget source, Moon moon)
        {
            switch (source)
            {
                case ManualMoonTarget.All:
                    _state.MoveToPending(moon);
                    SetStatus($"Added {FormatMoon(moon)} to pending");
                    break;

                case ManualMoonTarget.Pending:
                    _state.MoveToCollected(moon);
                    SetStatus($"Collected {FormatMoon(moon)}");
                    break;

                case ManualMoonTarget.Collected:
                case ManualMoonTarget.Uncounted:
                    _state.MoveToPending(moon);
                    SetStatus($"Moved {FormatMoon(moon)} to pending");
                    break;
            }
        }

        private void MoveMoonToTarget(Moon moon, ManualMoonTarget source, ManualMoonTarget target)
        {
            if (source == target)
            {
                SetCommandFeedback($"{FormatMoon(moon)} stayed in {DescribeTarget(target)}");
                return;
            }

            switch (target)
            {
                case ManualMoonTarget.All:
                    if (source != ManualMoonTarget.All)
                    {
                        _state.Remove(moon);
                        SetStatus($"Removed {FormatMoon(moon)}");
                    }
                    break;

                case ManualMoonTarget.Pending:
                    _state.MoveToPending(moon);
                    SetStatus($"Moved {FormatMoon(moon)} to pending");
                    break;

                case ManualMoonTarget.Collected:
                    _state.MoveToCollected(moon);
                    SetStatus($"Moved {FormatMoon(moon)} to collected");
                    break;

                case ManualMoonTarget.Uncounted:
                    _state.MoveToUncounted(moon);
                    SetStatus($"Moved {FormatMoon(moon)} to wrong moons");
                    break;
            }

            SetCommandFeedback($"Moved {FormatMoon(moon)} to {DescribeTarget(target)}");
        }

        private static string DescribeTarget(ManualMoonTarget target)
        {
            return target switch
            {
                ManualMoonTarget.Pending => "pending",
                ManualMoonTarget.Collected => "counted",
                ManualMoonTarget.Uncounted => "wrong",
                _ => "kingdom moons"
            };
        }

        private static T? FindAncestor<T>(Control control)
            where T : Control
        {
            Control? current = control;
            while (current != null)
            {
                if (current is T match)
                    return match;

                current = current.Parent as Control;
            }

            return null;
        }

        private MoonListItem CreateListItem(Moon moon)
        {
            return new MoonListItem(moon, $"{moon.Id}. {FormatMoon(moon)}");
        }

        private string FormatMoon(Moon moon)
        {
            return MoonDisplay.Format(moon, _state.Settings.OutputLanguage);
        }

        private static bool SameMoon(Moon left, Moon right)
        {
            return left.Id == right.Id &&
                left.Kingdom.Equals(right.Kingdom, StringComparison.OrdinalIgnoreCase);
        }

        private void SetStatus(string text)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SetCommandFeedback(text);
            });
        }

        private sealed record MoonListItem(Moon Moon, string Label)
        {
            public override string ToString() => Label;
        }

        private sealed record KingdomListItem(string Kingdom, string Label)
        {
            public override string ToString() => Label;
        }

        private sealed record ReviewCandidateItem(OcrMatchCandidate Candidate, string Label)
        {
            public override string ToString() => Label;
        }

        private enum ManualMoonTarget
        {
            All,
            Pending,
            Collected,
            Uncounted
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.Ocr;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aviscribe.UI
{
    public partial class MainWindow : Avalonia.Controls.Window
    {
        private IVideoCapture? _video;
        private readonly IVideoProvider _videoProvider;
        private readonly IAppDiagnostics _diagnostics;
        private readonly MoonRepository _repo;
        private readonly GameState _state = new();
        private readonly RunStateStore _stateStore;
        private readonly RunOutputWriter _outputWriter = new();
        private readonly Dictionary<string, List<AmbiguousOcrResult>> _reviewsByKingdom =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reviewSignatures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Bitmap> _moonImageCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CaptureCropSettings> _captureCropsByDevice =
            new(StringComparer.Ordinal);
        private readonly object _captureConfigurationLock = new();
        private readonly RawFrameSnapshotBroker _snapshotBroker = new();
        private readonly SemaphoreSlim _captureLifecycleGate = new(1, 1);
        private readonly CancellationTokenSource _closingCancellation = new();

        private FrameProcessor? _processor;
        private OnnxOcrService? _ocrService;
        private DiagnosticsWindow? _diagnosticsWindow;
        private string _processorCaptureDeviceId = string.Empty;
        private AmbiguousOcrResult? _activeReview;
        private Bitmap? _previewBitmap;

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
        private TextBlock? _cropSummaryText;
        private TextBlock? _selectedCaptureSourceText;
        private VideoDevice? _selectedCaptureSource;
        private IVideoCapture? _preparedCapture;
        private TabControl? _mainTabs;
        private bool _processorRunning;
        private bool _writeOverlayEnabled = true;
        private string _captureDeviceId = string.Empty;
        private CaptureSourceSelection _captureSourceSelection = new();
        private IReadOnlyList<VideoDevice> _allCaptureSources = [];
        private bool _updatingLists;
        private bool _dragStarted;
        private bool _suppressListClick;
        private Avalonia.Point _dragStartPoint;
        private ListBox? _dragSourceList;
        private ManualMoonTarget _dragSourceTarget;
        private Moon? _dragMoon;
        private ListBoxItem? _dragVisualItem;
        private int _previewRequested;
        private int _sourceWidth;
        private int _sourceHeight;

        public MainWindow()
            : this(new DesignVideoProvider(), NullAppDiagnostics.Instance)
        {
        }

        public MainWindow(
            IVideoProvider provider,
            IAppDiagnostics? diagnostics = null)
        {
            InitializeComponent();

            _videoProvider = provider;
            _diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
            _repo = MoonRepository.LoadDefault();
            _stateStore = new RunStateStore(_repo);
            LoadSavedRunState();
            NormalizeLanguageSettings();
            _diagnostics.DebugEnabled = _state.Settings.DebugLogging;
            _outputWriter.Language = _state.Settings.OutputLanguage;
            InitControls();
            InitFrameProcessor();
            _state.Changed += (_, _) => UpdateRunState();
            UpdateRunState();
        }

        private void NormalizeLanguageSettings()
        {
            if (!GameLanguageCatalog.IsSupportedInputLanguage(_state.Settings.InputLanguage))
                _state.Settings.InputLanguage = GameLanguage.ChineseTraditional;

            var outputLanguages = _repo.GetAvailableLanguages();
            if (outputLanguages.Contains(_state.Settings.OutputLanguage))
                return;

            _state.Settings.OutputLanguage = outputLanguages.Contains(GameLanguage.English)
                ? GameLanguage.English
                : outputLanguages.FirstOrDefault();
        }

        private void InitControls()
        {
            _selectedCaptureSourceText =
                this.GetControl<TextBlock>("txtSelectedCaptureSource");
            ApplyCaptureDevices(_videoProvider.GetDevices());
            this.GetControl<Button>("btnChooseCaptureSource").Click +=
                ChooseCaptureSource;

            // Update Preview button
            Button updatePreview = this.GetControl<Button>("btnUpdatePreview");
            updatePreview.Click += StartPreview;
            this.GetControl<Button>("btnSettingsUpdatePreview").Click += StartPreview;
            this.GetControl<Button>("btnCropGameplay").Click += OpenCropWindow;

            _kingdomSelect = this.GetControl<ComboBox>("cbKingdomSelect");
            _kingdomSelect.SelectionChanged += (_, _) =>
            {
                if (_kingdomSelect.SelectedItem is KingdomListItem item)
                {
                    _state.SetKingdom(item.Kingdom);
                    RefreshMoonSelect();
                    RefreshMoonList();
                    ShowNextReview();
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
            var inputLanguageItems = Enum.GetValues<GameLanguage>()
                .Select(language => new ComboBoxItem
                {
                    Content = language,
                    IsEnabled = GameLanguageCatalog.IsSupportedInputLanguage(language)
                })
                .ToList();
            _inputLanguageSelect.ItemsSource = inputLanguageItems;
            _inputLanguageSelect.SelectedItem = inputLanguageItems.Single(item =>
                Equals(item.Content, _state.Settings.InputLanguage));
            _inputLanguageSelect.SelectionChanged += (_, _) =>
            {
                if (_inputLanguageSelect.SelectedItem is ComboBoxItem { Content: GameLanguage language })
                {
                    _state.Settings.InputLanguage = language;
                    if (_processor != null)
                        RecreateFrameProcessor();
                    _state.NotifySettingsChanged();
                }
            };

            _outputLanguageSelect = this.GetControl<ComboBox>("cbOutputLanguageSelect");
            var outputLanguageItems = _repo.GetAvailableLanguages()
                .Select(language => new ComboBoxItem { Content = language })
                .ToList();
            _outputLanguageSelect.ItemsSource = outputLanguageItems;
            _outputLanguageSelect.SelectedItem = outputLanguageItems.Single(item =>
                Equals(item.Content, _state.Settings.OutputLanguage));
            _outputLanguageSelect.SelectionChanged += (_, _) =>
            {
                if (_outputLanguageSelect.SelectedItem is ComboBoxItem { Content: GameLanguage language })
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

            var showPendingImagesCheck = this.GetControl<CheckBox>("chkShowPendingImages");
            showPendingImagesCheck.IsChecked = _state.Settings.ShowPendingMoonImages;
            showPendingImagesCheck.IsCheckedChanged += (_, _) =>
            {
                _state.Settings.ShowPendingMoonImages = showPendingImagesCheck.IsChecked == true;
                _state.NotifySettingsChanged();
            };

            var debugLoggingCheck =
                this.GetControl<CheckBox>("chkDebugLogging");
            debugLoggingCheck.IsChecked = _state.Settings.DebugLogging;
            debugLoggingCheck.IsCheckedChanged += (_, _) =>
            {
                var enabled = debugLoggingCheck.IsChecked == true;
                _state.Settings.DebugLogging = enabled;
                _diagnostics.DebugEnabled = enabled;
                _diagnostics.Information(
                    enabled
                        ? "Debug logging enabled."
                        : "Debug logging disabled.");
                _state.NotifySettingsChanged();
            };
            this.GetControl<Button>("btnOpenDiagnostics").Click +=
                OpenDiagnostics;

            var ocrModeSelect = this.GetControl<ComboBox>("cbOcrModeSelect");
            var ocrModes = new[]
            {
                new OcrModeListItem(OcrMode.Cpu, "CPU (compatible default)"),
                new OcrModeListItem(OcrMode.WebGpu, "GPU (WebGPU)")
            };
            ocrModeSelect.ItemsSource = ocrModes;
            ocrModeSelect.SelectedItem = ocrModes.First(item => item.Mode == _state.Settings.OcrMode);
            ocrModeSelect.SelectionChanged += (_, _) =>
            {
                if (ocrModeSelect.SelectedItem is not OcrModeListItem item ||
                    item.Mode == _state.Settings.OcrMode)
                    return;
                _state.Settings.OcrMode = item.Mode;
                RecreateFrameProcessor();
                _state.NotifySettingsChanged();
                var status = _ocrService?.RuntimeStatus;
                SetStatus(status?.IsFallback == true
                    ? $"GPU OCR unavailable; using CPU: {status.FallbackReason}"
                    : $"OCR provider changed to {status?.ActiveProvider ?? item.Name}");
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
            _cropSummaryText = this.FindControl<TextBlock>("txtCropSummary");
            _mainTabs = this.FindControl<TabControl>("tabMain");
            UpdateCropSummary();

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

            this.GetControl<Button>("btnResetKingdom").Click += async (_, _) =>
            {
                if (!await ConfirmResetRunAsync())
                    return;

                _reviewsByKingdom.Clear();
                _reviewSignatures.Clear();
                _activeReview = null;
                _state.ResetRun();
                ShowNextReview();
                SetStatus("Run reset");
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

        private void ApplyCaptureDevices(IReadOnlyList<VideoDevice> devices)
        {
            _allCaptureSources = devices;
            _selectedCaptureSource = _captureSourceSelection.Restore(devices) ??
                devices.FirstOrDefault(source => source.IsAvailable);
            if (_selectedCaptureSource != null)
                SelectCaptureSource(_selectedCaptureSource, persist: false);
            else if (_selectedCaptureSourceText != null)
                _selectedCaptureSourceText.Text = "No compatible capture sources found";
        }

        private async void ChooseCaptureSource(object? sender, RoutedEventArgs args)
        {
            try
            {
                var devices = await _videoProvider.RefreshAsync(
                    _closingCancellation.Token);
                _allCaptureSources = devices;
                var selectableSources = devices.Where(source =>
                    source.IsAvailable ||
                    !devices.Any(candidate =>
                        candidate.Kind == source.Kind && candidate.IsAvailable));
                var chooser = new CaptureSourcePickerWindow(
                    selectableSources.ToArray(),
                    _selectedCaptureSource?.Id);
                var selected = await chooser.ShowDialog<VideoDevice?>(this);
                if (selected == null)
                    return;

                IVideoCapture? preparedCapture = null;
                try
                {
                    if (selected.RequiresInteractiveSelection)
                    {
                        SetStatus($"Choose a {SourceKindLabel(selected.Kind).ToLowerInvariant()}");
                        preparedCapture = await _videoProvider.OpenCaptureAsync(
                            selected.Id,
                            cancellationToken: _closingCancellation.Token);
                        selected = preparedCapture.Device;
                    }

                    await ReplacePreparedCaptureAsync(
                        preparedCapture,
                        _closingCancellation.Token);
                    preparedCapture = null;
                }
                finally
                {
                    if (preparedCapture != null)
                        await preparedCapture.DisposeAsync();
                }

                SelectCaptureSource(selected, persist: true);
                SetStatus($"Selected {selected.Name}");
            }
            catch (OperationCanceledException)
                when (_closingCancellation.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                SetStatus("Window selection cancelled");
            }
            catch (Exception ex)
            {
                _diagnostics.Error("Could not choose a capture source.", ex);
                SetStatus($"Could not choose a capture source: {ex.Message}");
            }
        }

        private void SelectCaptureSource(VideoDevice selected, bool persist)
        {
            _selectedCaptureSource = selected;
            _captureSourceSelection.Select(selected);
            _captureDeviceId = selected.Id;
            if (_selectedCaptureSourceText != null)
                _selectedCaptureSourceText.Text =
                    $"{SourceKindLabel(selected.Kind)} — {selected.Name}";
            UpdateCropSummary();
            if (persist)
                PersistRunState(_state.CreateSnapshot());
        }

        private static string SourceKindLabel(CaptureSourceKind kind) =>
            kind == CaptureSourceKind.Window ? "Window" : "Video device";

        private async Task ReplacePreparedCaptureAsync(
            IVideoCapture? capture,
            CancellationToken cancellationToken)
        {
            await _captureLifecycleGate
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var previous = _preparedCapture;
                _preparedCapture = null;
                if (previous != null)
                    await previous.DisposeAsync().ConfigureAwait(false);
                _preparedCapture = capture;
            }
            finally
            {
                _captureLifecycleGate.Release();
            }
        }

        private void InitFrameProcessor()
        {
            var matcher = new MoonMatcher(
                _repo,
                _state.Settings.InputLanguage,
                _state.Settings.OutputLanguage
            );

            var ocr = new OnnxOcrService(
                AppPaths.OcrModelPath,
                AppPaths.CharsetPath,
                _state.Settings.OcrMode,
                _diagnostics);
            _ocrService = ocr;
            var detector = LoadTextPresenceDetector();

            _processor = new FrameProcessor(
                ocr,
                matcher,
                _state,
                detector,
                GetCropForDevice(_captureDeviceId),
                _diagnostics);
            _processorCaptureDeviceId = _captureDeviceId;
            _processor.AmbiguousMatchReceived += (_, result) =>
            {
                var kingdom = _state.CurrentKingdom;
                EnqueueReview(kingdom, result);
            };
        }

        private void RecreateFrameProcessor()
        {
            var wasRunning = _processorRunning;
            var previous = _processor;

            previous?.Stop();
            InitFrameProcessor();

            if (wasRunning)
                _processor?.Start();

            previous?.Dispose();
        }

        private ITextPresenceDetector LoadTextPresenceDetector()
        {
            return new HeuristicTextPresenceDetector();
        }

        private void OnFrame(VideoFrame frame)
        {
            VideoFrame? ownedFrame = frame;
            try
            {
                Volatile.Write(ref _sourceWidth, frame.Frame.Width);
                Volatile.Write(ref _sourceHeight, frame.Frame.Height);

                _snapshotBroker.Offer(frame);

                if (Interlocked.Exchange(ref _previewRequested, 0) != 0)
                {
                    try
                    {
                        UpdatePreview(frame.Frame);
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Could not update preview: {ex.Message}");
                    }
                }

                var processor = _processor;
                if (processor != null)
                {
                    processor.PushFrame(frame);
                    ownedFrame = null;
                }
            }
            finally
            {
                ownedFrame?.Dispose();
            }
        }

        private async void StartPreview(object? sender, RoutedEventArgs args)
        {
            if (_selectedCaptureSource is not VideoDevice selected)
            {
                SetStatus("Select a capture source first");
                return;
            }

            Interlocked.Exchange(ref _previewRequested, 1);
            try
            {
                await EnsureCaptureStartedAsync(
                    selected,
                    _closingCancellation.Token);
            }
            catch (OperationCanceledException)
                when (_closingCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SetStatus($"Could not start capture: {ex.Message}");
            }
        }

        private async Task EnsureCaptureStartedAsync(
            VideoDevice selected,
            CancellationToken cancellationToken)
        {
            await _captureLifecycleGate
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            IVideoCapture? preparedCapture = null;
            try
            {
                if (_preparedCapture == null &&
                    _currentDevice?.Id == selected.Id &&
                    _video?.State == CaptureState.Running &&
                    _processorRunning)
                {
                    return;
                }

                preparedCapture = _preparedCapture;
                _preparedCapture = null;
                if (preparedCapture != null &&
                    !string.Equals(
                        preparedCapture.Device.Id,
                        selected.Id,
                        StringComparison.Ordinal))
                {
                    await preparedCapture.DisposeAsync().ConfigureAwait(false);
                    preparedCapture = null;
                }

                await StopCaptureCoreAsync(
                    "Capture source changed",
                    cancellationToken).ConfigureAwait(false);

                _currentDevice = selected;
                _captureDeviceId = selected.Id;
                if (_processor == null ||
                    !string.Equals(
                        _processorCaptureDeviceId,
                        selected.Id,
                        StringComparison.Ordinal))
                {
                    RecreateFrameProcessor();
                }
                else
                {
                    _processor.UpdateCrop(GetCropForDevice(selected.Id));
                }

                var capture = preparedCapture ??
                    await _videoProvider.OpenCaptureAsync(
                        selected.Id,
                        cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                preparedCapture = null;
                _currentDevice = capture.Device;
                _selectedCaptureSource = capture.Device;
                _video = capture;
                capture.FrameReceived += OnFrame;
                capture.CaptureFailed += OnCaptureFailed;
                capture.StateChanged += OnCaptureStateChanged;

                _processor?.Start();
                _processorRunning = true;
                try
                {
                    await capture
                        .StartAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    await StopCaptureCoreAsync(
                        "Capture failed to start",
                        CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (_selectedCaptureSourceText != null)
                        _selectedCaptureSourceText.Text =
                            $"{SourceKindLabel(capture.Device.Kind)} — " +
                            capture.Device.Name;
                    UpdateCropSummary();
                });
                PersistRunState(_state.CreateSnapshot());
                SetStatus(
                    $"Watching {capture.Device.Name} at {capture.SelectedFormat}");
                _diagnostics.Information(
                    $"Capture started: {capture.Device.Name}; " +
                    $"{capture.SelectedFormat}; backend {capture.Device.Backend}.");
            }
            finally
            {
                try
                {
                    if (preparedCapture != null)
                        await preparedCapture.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _captureLifecycleGate.Release();
                }
            }
        }

        private async Task StopCaptureAsync(
            string reason,
            CancellationToken cancellationToken = default)
        {
            await _captureLifecycleGate
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await StopCaptureCoreAsync(reason, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _captureLifecycleGate.Release();
            }
        }

        private async Task StopCaptureCoreAsync(
            string reason,
            CancellationToken cancellationToken)
        {
            _snapshotBroker.Cancel(new OperationCanceledException(reason));
            _diagnostics.Debug($"Stopping capture: {reason}.");

            var capture = _video;
            _video = null;
            var preparedCapture = _preparedCapture;
            _preparedCapture = null;
            try
            {
                if (capture != null)
                {
                    capture.FrameReceived -= OnFrame;
                    capture.CaptureFailed -= OnCaptureFailed;
                    capture.StateChanged -= OnCaptureStateChanged;
                    try
                    {
                        await capture
                            .StopAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        await capture.DisposeAsync().ConfigureAwait(false);
                    }
                }

            }
            finally
            {
                try
                {
                    if (preparedCapture != null)
                        await preparedCapture.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _processor?.Stop();
                    _processorRunning = false;
                    _currentDevice = null;
                }
            }
        }

        private void OnCaptureFailed(
            object? sender,
            CaptureErrorEventArgs args)
        {
            _snapshotBroker.Cancel(
                args.Exception ?? new IOException(args.Message));
            _diagnostics.Error(
                args.DeviceDisconnected
                    ? $"Capture device disconnected: {args.Message}"
                    : args.Message,
                args.Exception);
            SetStatus(args.DeviceDisconnected
                ? $"Capture device disconnected: {args.Message}"
                : args.Message);
        }

        private void OnCaptureStateChanged(
            object? sender,
            CaptureStateChangedEventArgs args)
        {
            _diagnostics.Debug(
                $"Capture state changed from {args.Previous} to {args.Current}.");
            if (args.Current == CaptureState.Faulted)
                SetStatus("Capture entered a faulted state");
        }

        private void UpdatePreview(Mat source)
        {
            if (source.Empty())
                return;

            var crop = GetCropForDevice(_captureDeviceId).Resolve(source.Width, source.Height);
            using var cropped = new Mat(source, crop);
            Cv2.ImEncode(".png", cropped, out var buffer);

            using var stream = new MemoryStream(buffer);
            var bitmap = new Bitmap(stream);

            Dispatcher.UIThread.Post(() =>
            {
                var oldBitmap = _previewBitmap;
                _previewBitmap = bitmap;
                _previewImage!.Source = bitmap;
                oldBitmap?.Dispose();
            });
        }

        private async void OpenCropWindow(object? sender, RoutedEventArgs args)
        {
            if (_selectedCaptureSource is not VideoDevice selected)
            {
                SetStatus("Select a capture source before cropping gameplay");
                return;
            }
            if (!selected.IsAvailable)
            {
                SetStatus(selected.UnavailableReason);
                return;
            }

            var window = new GameplayCropWindow(
                GetCropForDevice(selected.Id),
                RequestRawSnapshotAsync);
            var result = await window.ShowDialog<CaptureCropSettings?>(this);
            if (result == null)
                return;

            lock (_captureConfigurationLock)
                _captureCropsByDevice[selected.Id] = result.Clone();

            _captureDeviceId = selected.Id;
            UpdateCropSummary();
            PersistRunState(_state.CreateSnapshot());
            if (_currentDevice?.Id == selected.Id && _processorRunning)
                _processor?.UpdateCrop(result);

            Interlocked.Exchange(ref _previewRequested, 1);
            _diagnostics.Information(
                $"Gameplay crop updated for {selected.Name}: " +
                $"{result.X},{result.Y} {result.Width}x{result.Height}.");
            SetStatus("Gameplay crop applied");
        }

        private async Task<Bitmap?> RequestRawSnapshotAsync(
            CancellationToken cancellationToken)
        {
            if (_selectedCaptureSource is not VideoDevice selected)
                return null;

            await EnsureCaptureStartedAsync(selected, cancellationToken);
            using var snapshot = await _snapshotBroker.RequestAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            Cv2.ImEncode(".png", snapshot.Frame, out var buffer);
            using var stream = new MemoryStream(buffer);
            return new Bitmap(stream);
        }

        private CaptureCropSettings GetCropForDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return CaptureCropSettings.Default;

            lock (_captureConfigurationLock)
            {
                return _captureCropsByDevice.TryGetValue(deviceId, out var crop)
                    ? crop.Clone()
                    : CaptureCropSettings.Default;
            }
        }

        private IReadOnlyDictionary<string, CaptureCropSettings> GetCaptureCropSnapshot()
        {
            lock (_captureConfigurationLock)
            {
                return _captureCropsByDevice.ToDictionary(
                    item => item.Key,
                    item => item.Value.Clone(),
                    StringComparer.Ordinal);
            }
        }

        private void UpdateCropSummary()
        {
            if (_cropSummaryText == null)
                return;

            var crop = GetCropForDevice(_captureDeviceId);
            _cropSummaryText.Text =
                $"Crop: X {crop.X}, Y {crop.Y}, {crop.Width} × {crop.Height} " +
                $"(source {crop.SourceWidth} × {crop.SourceHeight})";
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
                    _pendingList.ItemsSource = snapshot.Pending.Select(CreatePendingListItem).ToList();
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
                foreach (var review in _stateStore.RestoreReviews(savedState))
                {
                    GetReviews(review.Kingdom).Add(review.Result);
                    _reviewSignatures.Add(CreateReviewSignature(review.Kingdom, review.Result));
                }
                _writeOverlayEnabled = savedState.WriteOverlay;
                _captureDeviceId = savedState.CaptureDeviceId;
                var selectedIds = new Dictionary<CaptureSourceKind, string>();
                foreach (var item in savedState.CaptureSourceIdsByKind ?? new Dictionary<string, string>())
                {
                    if (Enum.TryParse<CaptureSourceKind>(item.Key, ignoreCase: true, out var kind) &&
                        !string.IsNullOrWhiteSpace(item.Value))
                        selectedIds[kind] = item.Value;
                }
                if (selectedIds.Count == 0 && !string.IsNullOrWhiteSpace(savedState.CaptureDeviceId))
                    selectedIds[savedState.CaptureSourceKind] = savedState.CaptureDeviceId;
                _captureSourceSelection = new CaptureSourceSelection(savedState.CaptureSourceKind, selectedIds);
                lock (_captureConfigurationLock)
                {
                    _captureCropsByDevice.Clear();
                    foreach (var item in savedState.CaptureCropsByDevice ??
                        new Dictionary<string, CaptureCropSettings>())
                    {
                        if (item.Value != null)
                            _captureCropsByDevice[item.Key] = item.Value.Clone();
                    }
                }
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
                    _captureDeviceId,
                    GetCaptureCropSnapshot(),
                    _captureSourceSelection.Kind,
                    _captureSourceSelection.Snapshot(),
                    GetReviewSnapshot());
            }
            catch (Exception ex)
            {
                _diagnostics.Error("Could not save run state.", ex);
                SetStatus($"Could not save run state: {ex.Message}");
            }
        }

        private void EnqueueReview(string kingdom, AmbiguousOcrResult result)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (string.IsNullOrWhiteSpace(kingdom))
                    return;

                var signature = CreateReviewSignature(kingdom, result);
                if (!_reviewSignatures.Add(signature))
                    return;

                GetReviews(kingdom).Add(result);
                PersistRunState(_state.CreateSnapshot());

                if (_state.CurrentKingdom.Equals(kingdom, StringComparison.OrdinalIgnoreCase))
                    ShowNextReview();
            });
        }

        private void ShowNextReview()
        {
            var reviews = GetReviews(_state.CurrentKingdom);
            if (reviews.Count == 0)
            {
                _activeReview = null;

                if (_reviewPromptText != null)
                    _reviewPromptText.Text = "No ambiguous reads";

                if (_reviewSelect != null)
                    _reviewSelect.ItemsSource = null;

                return;
            }

            _activeReview = reviews[0];

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
            {
                var kingdom = _state.CurrentKingdom;
                GetReviews(kingdom).Remove(_activeReview);
                _reviewSignatures.Remove(CreateReviewSignature(kingdom, _activeReview));
            }

            _activeReview = null;
            ShowNextReview();
            PersistRunState(_state.CreateSnapshot());
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

        private static string CreateReviewSignature(string kingdom, AmbiguousOcrResult result)
        {
            var ids = string.Join(",", result.Candidates.Select(candidate =>
                $"{candidate.Moon.Kingdom}:{candidate.Moon.Id}"));
            return $"{kingdom}|{result.Type}|{result.Text}|{ids}";
        }

        private List<AmbiguousOcrResult> GetReviews(string kingdom)
        {
            if (!_reviewsByKingdom.TryGetValue(kingdom, out var reviews))
            {
                reviews = new List<AmbiguousOcrResult>();
                _reviewsByKingdom[kingdom] = reviews;
            }

            return reviews;
        }

        private IReadOnlyList<KingdomAmbiguousReview> GetReviewSnapshot()
        {
            return _reviewsByKingdom
                .SelectMany(item => item.Value.Select(review =>
                    new KingdomAmbiguousReview(item.Key, review)))
                .ToList();
        }

        private async Task<bool> ConfirmResetRunAsync()
        {
            var confirmation = new Avalonia.Controls.Window
            {
                Title = "Reset Run?",
                Width = 420,
                Height = 170,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var resetButton = new Button { Content = "Reset Run" };
            var cancelButton = new Button { Content = "Cancel" };
            resetButton.Click += (_, _) => confirmation.Close(true);
            cancelButton.Click += (_, _) => confirmation.Close(false);
            confirmation.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "This clears moon state and ambiguous reviews for every kingdom. This cannot be undone.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, resetButton }
                    }
                }
            };

            return await confirmation.ShowDialog<bool>(this);
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

        private MoonListItem CreatePendingListItem(Moon moon)
        {
            return new MoonListItem(
                moon,
                $"{moon.Id}. {FormatMoon(moon)}",
                _state.Settings.ShowPendingMoonImages ? GetMoonImage(moon) : null);
        }

        private Bitmap? GetMoonImage(Moon moon)
        {
            var key = $"{moon.Kingdom}/{moon.Id}.png";
            if (_moonImageCache.TryGetValue(key, out var cached))
                return cached;

            try
            {
                var uri = new Uri($"avares://Aviscribe.UI/Assets/moons/{key}");
                using var stream = AssetLoader.Open(uri);
                var image = new Bitmap(stream);
                _moonImageCache[key] = image;
                return image;
            }
            catch
            {
                return null;
            }
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

        private void OpenDiagnostics(
            object? sender,
            RoutedEventArgs args)
        {
            if (_diagnosticsWindow != null)
            {
                _diagnosticsWindow.Activate();
                return;
            }

            _diagnosticsWindow = new DiagnosticsWindow(
                _diagnostics,
                CreateDiagnosticsSnapshot);
            _diagnosticsWindow.Closed += (_, _) =>
                _diagnosticsWindow = null;
            _diagnosticsWindow.Show(this);
        }

        private DiagnosticsSnapshot CreateDiagnosticsSnapshot()
        {
            var capture = _video;
            var device = _currentDevice ?? _selectedCaptureSource;
            var captureDevice = device == null
                ? "No capture device selected"
                : $"{device.Name} ({device.Backend})";
            var captureState = capture == null
                ? CaptureState.Stopped.ToString()
                : $"{capture.State}; {capture.SelectedFormat}";

            var width = Volatile.Read(ref _sourceWidth);
            var height = Volatile.Read(ref _sourceHeight);
            var sourceAndCrop = "No source frame received";
            if (width > 0 && height > 0)
            {
                var crop = GetCropForDevice(device?.Id ?? _captureDeviceId)
                    .Resolve(width, height);
                sourceAndCrop =
                    $"{width} × {height}; crop " +
                    $"({crop.X}, {crop.Y}) {crop.Width} × {crop.Height}; " +
                    "normalized to 1920 × 1080";
            }

            return new DiagnosticsSnapshot(
                captureDevice,
                captureState,
                sourceAndCrop,
                _state.Settings.OcrMode == OcrMode.WebGpu ? "GPU (WebGPU)" : "CPU",
                FormatOcrRuntimeStatus());
        }

        private string FormatOcrRuntimeStatus()
        {
            var status = _ocrService?.RuntimeStatus;
            if (status == null)
                return "OCR session is not initialized";
            var active = $"{status.ActiveProvider} ({status.ActiveDevice})";
            return status.IsFallback ? $"{active}; fallback: {status.FallbackReason}" : active;
        }

        private void SetStatus(string text)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SetCommandFeedback(text);
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _diagnostics.Information("Aviscribe is shutting down.");
            _diagnosticsWindow?.Close();
            _diagnosticsWindow = null;
            _closingCancellation.Cancel();
            _snapshotBroker.Cancel(
                new OperationCanceledException("The application was closed."));
            try
            {
                StopCaptureAsync("The application was closed.")
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
            }

            _processor?.Dispose();
            _snapshotBroker.Dispose();
            _previewBitmap?.Dispose();
            foreach (var bitmap in _moonImageCache.Values)
                bitmap.Dispose();
            _closingCancellation.Dispose();
            _captureLifecycleGate.Dispose();

            base.OnClosed(e);
        }

        private sealed record MoonListItem(Moon Moon, string Label, Bitmap? Image = null)
        {
            public bool HasImage => Image != null;

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

        private sealed record OcrModeListItem(OcrMode Mode, string Name)
        {
            public override string ToString() => Name;
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

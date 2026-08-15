using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Aviscribe.Core.Capture;
using System;
using System.Threading;
using System.Threading.Tasks;
using CvRect = OpenCvSharp.Rect;

namespace Aviscribe.UI
{
    public partial class GameplayCropWindow : Window
    {
        private readonly Func<CancellationToken, Task<Bitmap?>>? _snapshotProvider;
        private readonly CaptureCropSettings _initialSelection;
        private CropSelectionControl? _canvas;
        private NumericUpDown? _cropX;
        private NumericUpDown? _cropY;
        private NumericUpDown? _cropWidth;
        private NumericUpDown? _cropHeight;
        private TextBlock? _sourceSize;
        private TextBlock? _status;
        private bool _updatingNumbers;
        private bool _hasFrame;
        private bool _closed;
        private CancellationTokenSource? _refreshCancellation;

        public GameplayCropWindow()
            : this(CaptureCropSettings.Default, null)
        {
        }

        public GameplayCropWindow(
            CaptureCropSettings initialSelection,
            Func<CancellationToken, Task<Bitmap?>>? snapshotProvider)
        {
            _initialSelection = initialSelection.Clone();
            _snapshotProvider = snapshotProvider;
            InitializeComponent();
            WireControls();
        }

        private void WireControls()
        {
            _canvas = this.GetControl<CropSelectionControl>("cropCanvas");
            _cropX = this.GetControl<NumericUpDown>("numCropX");
            _cropY = this.GetControl<NumericUpDown>("numCropY");
            _cropWidth = this.GetControl<NumericUpDown>("numCropWidth");
            _cropHeight = this.GetControl<NumericUpDown>("numCropHeight");
            _sourceSize = this.GetControl<TextBlock>("txtSourceSize");
            _status = this.GetControl<TextBlock>("txtCropStatus");

            _canvas.SelectionChanged += (_, selection) => UpdateNumbers(selection);
            this.GetControl<CheckBox>("chkShowGuides").IsCheckedChanged += (_, _) =>
            {
                _canvas.ShowScanGuides =
                    this.GetControl<CheckBox>("chkShowGuides").IsChecked == true;
                _canvas.InvalidateVisual();
            };

            _cropX.ValueChanged += (_, _) => ApplyNumbers(NumberDriver.Position);
            _cropY.ValueChanged += (_, _) => ApplyNumbers(NumberDriver.Position);
            _cropWidth.ValueChanged += (_, _) => ApplyNumbers(NumberDriver.Width);
            _cropHeight.ValueChanged += (_, _) => ApplyNumbers(NumberDriver.Height);

            this.GetControl<Button>("btnRefreshFrame").Click += async (_, _) => await RefreshFrameAsync();
            this.GetControl<Button>("btnResetCrop").Click += (_, _) => ResetCrop();
            this.GetControl<Button>("btnCancel").Click += (_, _) => Close(null);
            this.GetControl<Button>("btnApply").Click += (_, _) =>
            {
                Close(_hasFrame ? _canvas.Selection : _initialSelection.Clone());
            };

            _canvas.KeyDown += OnCanvasKeyDown;
            Opened += async (_, _) => await RefreshFrameAsync();
            Closed += (_, _) =>
            {
                _closed = true;
                _refreshCancellation?.Cancel();
                _refreshCancellation?.Dispose();
                _refreshCancellation = null;
            };
        }

        private async Task RefreshFrameAsync()
        {
            if (_snapshotProvider == null)
                return;

            var previousCancellation = _refreshCancellation;
            var refreshCancellation = new CancellationTokenSource();
            _refreshCancellation = refreshCancellation;
            previousCancellation?.Cancel();
            previousCancellation?.Dispose();

            var button = this.GetControl<Button>("btnRefreshFrame");
            button.IsEnabled = false;
            _status!.Text = "Waiting for the next camera frame…";
            try
            {
                var bitmap = await _snapshotProvider(refreshCancellation.Token);
                if (bitmap == null)
                {
                    _status.Text = "No frame was received. Check the selected capture source.";
                    return;
                }
                if (_closed)
                {
                    bitmap.Dispose();
                    return;
                }

                var selection = _hasFrame ? _canvas!.Selection : _initialSelection;
                _canvas!.SetFrame(bitmap, selection);
                _hasFrame = true;
                _sourceSize!.Text = $"Source: {bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}";
                _status.Text = "Drag the crop, use the steppers, or press arrow keys to fine tune.";
                _canvas.Focus();
            }
            catch (TimeoutException)
            {
                _status.Text = "Timed out waiting for a camera frame.";
            }
            catch (OperationCanceledException)
            {
                if (!_closed && ReferenceEquals(_refreshCancellation, refreshCancellation))
                    _status.Text = "Frame refresh cancelled.";
            }
            catch (Exception ex)
            {
                _status.Text = $"Could not refresh the frame: {ex.Message}";
            }
            finally
            {
                if (ReferenceEquals(_refreshCancellation, refreshCancellation))
                {
                    _refreshCancellation = null;
                    refreshCancellation.Dispose();
                    if (!_closed)
                        button.IsEnabled = true;
                }
            }
        }

        private void ResetCrop()
        {
            if (!_hasFrame)
                return;

            var current = _canvas!.Selection;
            _canvas.SetSelection(CaptureCropSettings.CreateLargestCentered(
                current.SourceWidth,
                current.SourceHeight));
        }

        private void UpdateNumbers(CaptureCropSettings selection)
        {
            _updatingNumbers = true;
            try
            {
                _cropX!.Maximum = Math.Max(0, selection.SourceWidth - selection.Width);
                _cropY!.Maximum = Math.Max(0, selection.SourceHeight - selection.Height);
                _cropWidth!.Maximum = selection.SourceWidth;
                _cropHeight!.Maximum = selection.SourceHeight;
                _cropX.Value = selection.X;
                _cropY.Value = selection.Y;
                _cropWidth.Value = selection.Width;
                _cropHeight.Value = selection.Height;
            }
            finally
            {
                _updatingNumbers = false;
            }
        }

        private void ApplyNumbers(NumberDriver driver)
        {
            if (_updatingNumbers || !_hasFrame)
                return;

            var current = _canvas!.Selection;
            var sourceWidth = current.SourceWidth;
            var sourceHeight = current.SourceHeight;
            var maxScale = Math.Min(
                sourceWidth / CaptureCropSettings.AspectWidth,
                sourceHeight / CaptureCropSettings.AspectHeight);
            var requestedScale = driver == NumberDriver.Height
                ? (int)Math.Round((double)(_cropHeight!.Value ?? current.Height) /
                    CaptureCropSettings.AspectHeight)
                : (int)Math.Round((double)(_cropWidth!.Value ?? current.Width) /
                    CaptureCropSettings.AspectWidth);
            var scale = Math.Clamp(requestedScale, 1, Math.Max(1, maxScale));
            var width = scale * CaptureCropSettings.AspectWidth;
            var height = scale * CaptureCropSettings.AspectHeight;
            var x = Math.Clamp(
                (int)(_cropX!.Value ?? current.X),
                0,
                Math.Max(0, sourceWidth - width));
            var y = Math.Clamp(
                (int)(_cropY!.Value ?? current.Y),
                0,
                Math.Max(0, sourceHeight - height));

            _canvas.SetSelection(new CaptureCropSettings
            {
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                X = x,
                Y = y,
                Width = width,
                Height = height
            });
        }

        private void OnCanvasKeyDown(object? sender, KeyEventArgs e)
        {
            if (!_hasFrame)
                return;

            var amount = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            var dx = e.Key switch
            {
                Key.Left => -amount,
                Key.Right => amount,
                _ => 0
            };
            var dy = e.Key switch
            {
                Key.Up => -amount,
                Key.Down => amount,
                _ => 0
            };
            if (dx == 0 && dy == 0)
                return;

            var current = _canvas!.Selection;
            var x = Math.Clamp(current.X + dx, 0, current.SourceWidth - current.Width);
            var y = Math.Clamp(current.Y + dy, 0, current.SourceHeight - current.Height);
            _canvas.SetSelection(new CaptureCropSettings
            {
                SourceWidth = current.SourceWidth,
                SourceHeight = current.SourceHeight,
                X = x,
                Y = y,
                Width = current.Width,
                Height = current.Height
            });
            e.Handled = true;
        }

        private enum NumberDriver
        {
            Position,
            Width,
            Height
        }
    }
}

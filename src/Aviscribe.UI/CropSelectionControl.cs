using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Aviscribe.Core.Capture;
using Aviscribe.Core.KingdomDetection;
using Aviscribe.Core.Ocr;
using System;
using System.Collections.Generic;
using CvRect = OpenCvSharp.Rect;

namespace Aviscribe.UI
{
    public sealed class CropSelectionControl : Control
    {
        private const double HandleRadius = 7;
        private static readonly IBrush OutsideBrush =
            new SolidColorBrush(Color.FromArgb(155, 0, 0, 0));
        private static readonly Pen SelectionPen = new(Brushes.White, 2);

        private Bitmap? _frame;
        private CaptureCropSettings _selection = CaptureCropSettings.Default;
        private DragHandle _dragHandle;
        private Point _dragStartSource;
        private CvRect _dragStartBounds;

        public event EventHandler<CaptureCropSettings>? SelectionChanged;

        public bool ShowScanGuides { get; set; } = true;

        public CaptureCropSettings Selection => _selection.Clone();

        public void SetFrame(Bitmap frame, CaptureCropSettings selection)
        {
            var oldFrame = _frame;
            _frame = frame;
            var resolved = selection.Resolve(frame.PixelSize.Width, frame.PixelSize.Height);
            _selection = CaptureCropSettings.FromRect(
                frame.PixelSize.Width,
                frame.PixelSize.Height,
                resolved);
            oldFrame?.Dispose();
            InvalidateVisual();
            RaiseSelectionChanged();
        }

        public void SetSelection(CaptureCropSettings selection)
        {
            if (_frame == null)
            {
                _selection = selection.Clone();
                InvalidateVisual();
                return;
            }

            var resolved = selection.Resolve(_frame.PixelSize.Width, _frame.PixelSize.Height);
            _selection = CaptureCropSettings.FromRect(
                _frame.PixelSize.Width,
                _frame.PixelSize.Height,
                resolved);
            InvalidateVisual();
            RaiseSelectionChanged();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(Brushes.Black, Bounds);
            if (_frame == null)
                return;

            var imageBounds = GetImageBounds();
            context.DrawImage(
                _frame,
                new Rect(0, 0, _frame.PixelSize.Width, _frame.PixelSize.Height),
                imageBounds);

            var selectionBounds = SourceToDisplay(new CvRect(
                _selection.X,
                _selection.Y,
                _selection.Width,
                _selection.Height));
            DrawOutsideMask(context, imageBounds, selectionBounds);

            if (ShowScanGuides)
                DrawScanGuides(context, selectionBounds);

            context.DrawRectangle(null, SelectionPen, selectionBounds);
            foreach (var point in HandlePoints(selectionBounds))
                context.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), point, HandleRadius, HandleRadius);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_frame == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            var point = e.GetPosition(this);
            var selectionBounds = SourceToDisplay(new CvRect(
                _selection.X,
                _selection.Y,
                _selection.Width,
                _selection.Height));
            _dragHandle = HitTestHandle(point, selectionBounds);
            if (_dragHandle == DragHandle.None && selectionBounds.Contains(point))
                _dragHandle = DragHandle.Move;
            if (_dragHandle == DragHandle.None)
                return;

            _dragStartSource = DisplayToSource(point);
            _dragStartBounds = new CvRect(_selection.X, _selection.Y, _selection.Width, _selection.Height);
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_frame == null ||
                _dragHandle == DragHandle.None ||
                !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var sourcePoint = DisplayToSource(e.GetPosition(this));
            var next = _dragHandle == DragHandle.Move
                ? MoveSelection(sourcePoint)
                : ResizeSelection(sourcePoint);
            _selection = CaptureCropSettings.FromRect(
                _frame.PixelSize.Width,
                _frame.PixelSize.Height,
                next);
            InvalidateVisual();
            RaiseSelectionChanged();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_dragHandle == DragHandle.None)
                return;

            _dragHandle = DragHandle.None;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _frame?.Dispose();
            _frame = null;
            base.OnDetachedFromVisualTree(e);
        }

        private CvRect MoveSelection(Point sourcePoint)
        {
            var dx = (int)Math.Round(sourcePoint.X - _dragStartSource.X);
            var dy = (int)Math.Round(sourcePoint.Y - _dragStartSource.Y);
            var x = Math.Clamp(
                _dragStartBounds.X + dx,
                0,
                _frame!.PixelSize.Width - _dragStartBounds.Width);
            var y = Math.Clamp(
                _dragStartBounds.Y + dy,
                0,
                _frame.PixelSize.Height - _dragStartBounds.Height);
            return new CvRect(x, y, _dragStartBounds.Width, _dragStartBounds.Height);
        }

        private CvRect ResizeSelection(Point sourcePoint)
        {
            var sourceWidth = _frame!.PixelSize.Width;
            var sourceHeight = _frame.PixelSize.Height;
            var left = _dragHandle is DragHandle.Left or DragHandle.TopLeft or DragHandle.BottomLeft;
            var right = _dragHandle is DragHandle.Right or DragHandle.TopRight or DragHandle.BottomRight;
            var top = _dragHandle is DragHandle.Top or DragHandle.TopLeft or DragHandle.TopRight;
            var bottom = _dragHandle is DragHandle.Bottom or DragHandle.BottomLeft or DragHandle.BottomRight;
            var corner = (left || right) && (top || bottom);

            if (corner)
            {
                var anchorX = left ? _dragStartBounds.Right : _dragStartBounds.X;
                var anchorY = top ? _dragStartBounds.Bottom : _dragStartBounds.Y;
                var rawWidth = Math.Abs(anchorX - sourcePoint.X);
                var rawHeight = Math.Abs(anchorY - sourcePoint.Y);
                var scale = (int)Math.Floor(Math.Min(
                    rawWidth / CaptureCropSettings.AspectWidth,
                    rawHeight / CaptureCropSettings.AspectHeight));
                var maxWidth = left ? anchorX : sourceWidth - anchorX;
                var maxHeight = top ? anchorY : sourceHeight - anchorY;
                scale = ClampScale(scale, maxWidth, maxHeight);
                var width = scale * CaptureCropSettings.AspectWidth;
                var height = scale * CaptureCropSettings.AspectHeight;
                return new CvRect(
                    left ? anchorX - width : anchorX,
                    top ? anchorY - height : anchorY,
                    width,
                    height);
            }

            if (left || right)
            {
                var anchorX = left ? _dragStartBounds.Right : _dragStartBounds.X;
                var centerY = _dragStartBounds.Y + _dragStartBounds.Height / 2.0;
                var rawWidth = Math.Abs(anchorX - sourcePoint.X);
                var maxWidth = left ? anchorX : sourceWidth - anchorX;
                var maxHeight = 2 * Math.Min(centerY, sourceHeight - centerY);
                var scale = ClampScale(
                    (int)Math.Round(rawWidth / CaptureCropSettings.AspectWidth),
                    maxWidth,
                    maxHeight);
                var width = scale * CaptureCropSettings.AspectWidth;
                var height = scale * CaptureCropSettings.AspectHeight;
                return new CvRect(
                    left ? anchorX - width : anchorX,
                    (int)Math.Round(centerY - height / 2.0),
                    width,
                    height);
            }

            var verticalAnchorY = top ? _dragStartBounds.Bottom : _dragStartBounds.Y;
            var centerX = _dragStartBounds.X + _dragStartBounds.Width / 2.0;
            var rawHeightOnly = Math.Abs(verticalAnchorY - sourcePoint.Y);
            var horizontalRoom = 2 * Math.Min(centerX, sourceWidth - centerX);
            var verticalRoom = top ? verticalAnchorY : sourceHeight - verticalAnchorY;
            var verticalScale = ClampScale(
                (int)Math.Round(rawHeightOnly / CaptureCropSettings.AspectHeight),
                horizontalRoom,
                verticalRoom);
            var finalWidth = verticalScale * CaptureCropSettings.AspectWidth;
            var finalHeight = verticalScale * CaptureCropSettings.AspectHeight;
            return new CvRect(
                (int)Math.Round(centerX - finalWidth / 2.0),
                top ? verticalAnchorY - finalHeight : verticalAnchorY,
                finalWidth,
                finalHeight);
        }

        private static int ClampScale(int requestedScale, double maximumWidth, double maximumHeight)
        {
            var maximumScale = Math.Max(
                1,
                (int)Math.Floor(Math.Min(
                    maximumWidth / CaptureCropSettings.AspectWidth,
                    maximumHeight / CaptureCropSettings.AspectHeight)));
            var minimumScale = Math.Min(10, maximumScale);
            return Math.Clamp(requestedScale, minimumScale, maximumScale);
        }

        private Rect GetImageBounds()
        {
            var scale = Math.Min(
                Bounds.Width / _frame!.PixelSize.Width,
                Bounds.Height / _frame.PixelSize.Height);
            var width = _frame.PixelSize.Width * scale;
            var height = _frame.PixelSize.Height * scale;
            return new Rect(
                (Bounds.Width - width) / 2,
                (Bounds.Height - height) / 2,
                width,
                height);
        }

        private Rect SourceToDisplay(CvRect source)
        {
            var imageBounds = GetImageBounds();
            var scale = imageBounds.Width / _frame!.PixelSize.Width;
            return new Rect(
                imageBounds.X + source.X * scale,
                imageBounds.Y + source.Y * scale,
                source.Width * scale,
                source.Height * scale);
        }

        private Point DisplayToSource(Point display)
        {
            var imageBounds = GetImageBounds();
            var scale = _frame!.PixelSize.Width / imageBounds.Width;
            return new Point(
                Math.Clamp((display.X - imageBounds.X) * scale, 0, _frame.PixelSize.Width),
                Math.Clamp((display.Y - imageBounds.Y) * scale, 0, _frame.PixelSize.Height));
        }

        private static void DrawOutsideMask(
            DrawingContext context,
            Rect imageBounds,
            Rect selectionBounds)
        {
            context.FillRectangle(
                OutsideBrush,
                new Rect(imageBounds.X, imageBounds.Y, imageBounds.Width, selectionBounds.Y - imageBounds.Y));
            context.FillRectangle(
                OutsideBrush,
                new Rect(imageBounds.X, selectionBounds.Bottom, imageBounds.Width, imageBounds.Bottom - selectionBounds.Bottom));
            context.FillRectangle(
                OutsideBrush,
                new Rect(imageBounds.X, selectionBounds.Y, selectionBounds.X - imageBounds.X, selectionBounds.Height));
            context.FillRectangle(
                OutsideBrush,
                new Rect(selectionBounds.Right, selectionBounds.Y, imageBounds.Right - selectionBounds.Right, selectionBounds.Height));
        }

        private static void DrawScanGuides(DrawingContext context, Rect selectionBounds)
        {
            foreach (var guide in OcrReferenceLayout.Guides)
            {
                var color = GuideColor(guide.Type);
                var detectionPen = new Pen(
                    new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)),
                    1);
                var ocrPen = new Pen(new SolidColorBrush(color), 2);
                context.DrawRectangle(
                    null,
                    detectionPen,
                    MapGuide(guide.DetectionBounds, selectionBounds));
                context.DrawRectangle(
                    null,
                    ocrPen,
                    MapGuide(guide.OcrBounds, selectionBounds));
            }

            var kingdomPen = new Pen(
                new SolidColorBrush(Color.FromArgb(190, 117, 224, 134)),
                1);
            context.DrawRectangle(
                null,
                kingdomPen,
                MapGuide(TemplateKingdomDetector.IconSearchBounds, selectionBounds));
            context.DrawRectangle(
                null,
                kingdomPen,
                MapGuide(TemplateKingdomDetector.HudUnderlineBounds, selectionBounds));
        }

        private static Rect MapGuide(CvRect guide, Rect selection)
        {
            return new Rect(
                selection.X + guide.X / (double)OcrReferenceLayout.Width * selection.Width,
                selection.Y + guide.Y / (double)OcrReferenceLayout.Height * selection.Height,
                guide.Width / (double)OcrReferenceLayout.Width * selection.Width,
                guide.Height / (double)OcrReferenceLayout.Height * selection.Height);
        }

        private static Color GuideColor(OcrRegionType type)
        {
            return type switch
            {
                OcrRegionType.Talkatoo => Color.FromRgb(255, 190, 70),
                OcrRegionType.MoonGet => Color.FromRgb(65, 205, 255),
                OcrRegionType.StoryMoon => Color.FromRgb(255, 95, 190),
                _ => Colors.White
            };
        }

        private static IReadOnlyList<Point> HandlePoints(Rect bounds)
        {
            var centerX = bounds.X + bounds.Width / 2;
            var centerY = bounds.Y + bounds.Height / 2;
            return
            [
                bounds.TopLeft,
                new Point(centerX, bounds.Y),
                bounds.TopRight,
                new Point(bounds.X, centerY),
                new Point(bounds.Right, centerY),
                bounds.BottomLeft,
                new Point(centerX, bounds.Bottom),
                bounds.BottomRight
            ];
        }

        private static DragHandle HitTestHandle(Point point, Rect bounds)
        {
            var handles = new[]
            {
                DragHandle.TopLeft,
                DragHandle.Top,
                DragHandle.TopRight,
                DragHandle.Left,
                DragHandle.Right,
                DragHandle.BottomLeft,
                DragHandle.Bottom,
                DragHandle.BottomRight
            };
            var points = HandlePoints(bounds);
            for (var index = 0; index < points.Count; index++)
            {
                var dx = point.X - points[index].X;
                var dy = point.Y - points[index].Y;
                if (dx * dx + dy * dy <= 12 * 12)
                    return handles[index];
            }

            return DragHandle.None;
        }

        private void RaiseSelectionChanged()
        {
            SelectionChanged?.Invoke(this, _selection.Clone());
        }

        private enum DragHandle
        {
            None,
            Move,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }
    }
}

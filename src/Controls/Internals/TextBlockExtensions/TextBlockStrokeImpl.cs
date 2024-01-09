using CoolControls.WinUI3.Utils.Graphics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Toolkit.Uwp.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI;
using Windows.UI.Text;
using WinRT;

namespace CoolControls.WinUI3.Controls.Internals.TextBlockExtensions
{
    internal class TextBlockStrokeImpl : IDisposable
    {
        private const float padding = 10;
        private readonly DeviceHolder deviceHolder;
        private bool disposedValue;
        private WeakReference<TextBlock> weakTextBlock;
        private TextBlockProperties textBlockProps;
        private float strokeThickness = 2f;
        private int drawSessionId;
        private int updateSessionId;

        private SpriteVisual strokeVisual;
        private SpriteVisual changingPreviewVisual;
        private CompositionSurfaceBrush? previewBrush;
        private ContainerVisual rootVisual;
        private SpriteVisual? alphaMaskVisualForCapture;
        private CompositionSurfaceBrush? strokeBrush;
        private CompositionMaskBrush? maskBrush;
        private CompositionVirtualDrawingSurface? compositionStrokeSurface;
        private ExpressionAnimation? textVisibleExpression;
        private ExpressionAnimation? strokeVisibleExpression;
        private ExpressionAnimation? previewVisibleExpression;
        private CompositionBrush? strokeFillBrush;
        private CompositionPropertySet propSet;

        internal TextBlockStrokeImpl(TextBlock textBlock, DeviceHolder deviceHolder)
        {
            this.deviceHolder = deviceHolder;
            this.deviceHolder.GraphicsDevice.RenderingDeviceReplaced += GraphicsDevice_RenderingDeviceReplaced;

            weakTextBlock = new WeakReference<TextBlock>(textBlock);

            textBlock.Loaded += (new WeakEventListener<TextBlock, object, RoutedEventArgs>(textBlock)
            {
                OnEventAction = (i, s, a) => UpdateStroke(true),
                OnDetachAction = i => textBlock.Loaded -= i.OnEvent
            }).OnEvent;

            textBlock.Unloaded += (new WeakEventListener<TextBlock, object, RoutedEventArgs>(textBlock)
            {
                OnEventAction = (i, s, a) => UpdateStroke(true),
                OnDetachAction = i => textBlock.Unloaded -= i.OnEvent
            }).OnEvent;

            textBlock.LayoutUpdated += (new WeakEventListener<TextBlock, object?, object>(textBlock)
            {
                OnEventAction = (i, s, a) => UpdateStroke(false),
                OnDetachAction = i => textBlock.LayoutUpdated -= i.OnEvent
            }).OnEvent;

            strokeVisual = deviceHolder.Compositor.CreateSpriteVisual();
            strokeVisual.Offset = new System.Numerics.Vector3(-padding, -padding, 0);

            changingPreviewVisual = deviceHolder.Compositor.CreateSpriteVisual();

            rootVisual = deviceHolder.Compositor.CreateContainerVisual();
            rootVisual.Children.InsertAtTop(changingPreviewVisual);
            rootVisual.Children.InsertAtTop(strokeVisual);

            propSet = deviceHolder.Compositor.CreatePropertySet();
            propSet.InsertScalar("StrokeThickness", strokeThickness);
        }

        internal CompositionBrush? StrokeBrush
        {
            get => strokeFillBrush;
            set
            {
                if (strokeFillBrush != value)
                {
                    strokeFillBrush = value;

                    if (maskBrush != null)
                    {
                        maskBrush.Source = value;

                        UpdateStroke(true);
                    }
                }
            }
        }

        internal float StrokeThickness
        {
            get => strokeThickness;
            set
            {
                if (strokeThickness != value)
                {
                    if (value < 0) throw new ArgumentException(nameof(StrokeThickness));
                    strokeThickness = value;
                    propSet.InsertScalar("StrokeThickness", strokeThickness);

                    UpdateStroke(true);
                }
            }
        }

        internal Visual StrokeVisual => rootVisual;

        internal async Task UpdatePreviewAsync(FrameworkElement? element)
        {
            var oldSurface = previewBrush?.Surface;

            if (element != null && element.IsLoaded)
            {
                var sessionId = (updateSessionId += 1);

                var size = element.ActualSize;
                if (size.X == 0 || size.Y == 0) return;

                var visual = element.GetVisualInternal();

                if (previewBrush == null)
                {
                    previewBrush = deviceHolder.Compositor.CreateSurfaceBrush();
                    previewBrush.HorizontalAlignmentRatio = 0;
                    previewBrush.VerticalAlignmentRatio = 0;
                    previewBrush.Stretch = CompositionStretch.None;
                    previewBrush.SnapToPixels = true;
                    changingPreviewVisual.Brush = previewBrush;
                }

                changingPreviewVisual.Size = size;

                var scale = (float)element.XamlRoot.RasterizationScale;

                var surface = await CompositionHelpers.CaptureAsync(
                    deviceHolder.GraphicsDevice,
                    visual,
                    size,
                    scale * 96);

                var oldSurface2 = previewBrush.Surface;
                previewBrush.Surface = surface;
                previewBrush.Scale = new Vector2(1 / scale, 1 / scale);

                if (sessionId == updateSessionId)
                {
                    try
                    {
                        if (oldSurface2 != null && oldSurface2 != oldSurface)
                        {
                            oldSurface2.As<IDisposable>().Dispose();
                        }
                    }
                    catch { }
                }
            }

            try
            {
                if (oldSurface != null)
                {
                    oldSurface.As<IDisposable>().Dispose();
                }
            }
            catch { }
        }

        internal event EventHandler? UpdatePreviewRequest;

        private void GraphicsDevice_RenderingDeviceReplaced(Microsoft.UI.Composition.CompositionGraphicsDevice sender, Microsoft.UI.Composition.RenderingDeviceReplacedEventArgs args)
        {
            if (previewBrush != null)
            {
                var oldSurface = previewBrush.Surface;
                previewBrush.Surface = null;
                try
                {
                    oldSurface.As<IDisposable>().Dispose();
                }
                catch { }
            }

            UpdateStroke(true);
        }

        private void UpdateStroke(bool force)
        {
            if (weakTextBlock != null && weakTextBlock.TryGetTarget(out var textBlock))
            {
                if (force)
                {
                    textBlockProps = TextBlockProperties.Create(textBlock);
                }
                else
                {
                    var newProp = TextBlockProperties.Create(textBlock);
                    if (textBlockProps == newProp) return;
                    textBlockProps = newProp;
                }
                var sessionId = (drawSessionId += 1);
                _ = DrawStrokeAsync(textBlock, textBlockProps, sessionId);
            }
        }

        private async Task DrawStrokeAsync(TextBlock textBlock, TextBlockProperties textBlockProps, long drawSessionId)
        {
            if (textBlock.IsLoaded && strokeFillBrush != null && strokeThickness > 0)
            {
                var actualWidth = textBlockProps.ActualWidth;
                var actualHeight = textBlockProps.ActualHeight;

                if (actualWidth == 0 || actualHeight == 0) return;

                var compositor = deviceHolder.Compositor;

                if (alphaMaskVisualForCapture == null)
                {
                    alphaMaskVisualForCapture = compositor.CreateSpriteVisual();
                    alphaMaskVisualForCapture.Brush = textBlock.GetAlphaMask();
                }

                alphaMaskVisualForCapture.Size = new System.Numerics.Vector2((float)actualWidth, (float)actualHeight);

                using (var surface = await CompositionHelpers.CaptureToDirect3DSurfaceAsync(
                    deviceHolder.GraphicsDevice,
                    alphaMaskVisualForCapture,
                    alphaMaskVisualForCapture.Size,
                    (float)(textBlock.XamlRoot.RasterizationScale * 96)))
                {
                    if (surface == null) return;
                    if (drawSessionId != this.drawSessionId) return;

                    using (var locker = await deviceHolder.LockAsync())
                    {
                        if (drawSessionId != this.drawSessionId) return;

                        DrawSurfaceToStrokeVisual(textBlock, in textBlockProps, surface);
                    }
                }
            }
        }

        private void DrawSurfaceToStrokeVisual(TextBlock textBlock, in TextBlockProperties textBlockProps, IDirect3DSurface surface)
        {
            if (surface == null) return;

            var compositor = deviceHolder.Compositor;

            if (compositionStrokeSurface == null)
            {
                compositionStrokeSurface = deviceHolder.GraphicsDevice.CreateVirtualDrawingSurface(
                    new Windows.Graphics.SizeInt32(0, 0),
                    Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
            }

            if (strokeBrush == null)
            {
                strokeBrush = compositor.CreateSurfaceBrush(compositionStrokeSurface);
                strokeBrush.HorizontalAlignmentRatio = 0;
                strokeBrush.VerticalAlignmentRatio = 0;
                strokeBrush.Stretch = CompositionStretch.None;
            }

            if (maskBrush == null)
            {
                maskBrush = compositor.CreateMaskBrush();
                maskBrush.Source = strokeFillBrush;
                maskBrush.Mask = strokeBrush;

                strokeVisual.Brush = maskBrush;
            }

            var textBlockVisual = textBlock.GetVisualInternal();

            if (textVisibleExpression == null)
            {
                textVisibleExpression = compositor.CreateExpressionAnimation($"abs(strokeVisual.Size.X - visual.Size.X - {padding * 2}) + abs(strokeVisual.Size.Y - visual.Size.Y - {padding * 2}) <= 5 || propSet.StrokeThickness == 0 ? 1 : 0");
                textVisibleExpression.SetReferenceParameter("strokeVisual", strokeVisual);
                textVisibleExpression.SetReferenceParameter("visual", textBlockVisual);
                textVisibleExpression.SetReferenceParameter("propSet", propSet);

                textBlockVisual.StartAnimation("Opacity", textVisibleExpression);
            }

            if (strokeVisibleExpression == null)
            {
                strokeVisibleExpression = compositor.CreateExpressionAnimation($"abs(strokeVisual.Size.X - visual.Size.X - {padding * 2}) + abs(strokeVisual.Size.Y - visual.Size.Y - {padding * 2}) <= 5 && propSet.StrokeThickness > 0 ? 1 : 0");
                strokeVisibleExpression.SetReferenceParameter("strokeVisual", strokeVisual);
                strokeVisibleExpression.SetReferenceParameter("visual", textBlockVisual);
                strokeVisibleExpression.SetReferenceParameter("propSet", propSet);

                strokeVisual.StartAnimation("Opacity", strokeVisibleExpression);
            }

            if (previewVisibleExpression == null)
            {
                previewVisibleExpression = compositor.CreateExpressionAnimation($"abs(strokeVisual.Size.X - visual.Size.X - {padding * 2}) + abs(strokeVisual.Size.Y - visual.Size.Y - {padding * 2}) <= 5 || propSet.StrokeThickness == 0 ? 0 : 1");
                previewVisibleExpression.SetReferenceParameter("strokeVisual", strokeVisual);
                previewVisibleExpression.SetReferenceParameter("visual", textBlockVisual);
                previewVisibleExpression.SetReferenceParameter("propSet", propSet);

                changingPreviewVisual.StartAnimation("Opacity", previewVisibleExpression);
            }

            var scale = (float)textBlock.XamlRoot.RasterizationScale;
            using (var canvasBitmap = CanvasBitmap.CreateFromDirect3D11Surface(deviceHolder.CanvasDevice, surface, scale * 96))
            {
                var surfaceSize = canvasBitmap.SizeInPixels;
                var visualSize = new Vector2((float)textBlockProps.ActualWidth, (float)textBlockProps.ActualHeight);

                compositionStrokeSurface.Resize(new Windows.Graphics.SizeInt32((int)(surfaceSize.Width + padding * 2), (int)(surfaceSize.Height + padding * 2)));

                using (var ds = CanvasComposition.CreateDrawingSession(
                    compositionStrokeSurface,
                    new Windows.Foundation.Rect(0, 0, surfaceSize.Width + padding * 2, surfaceSize.Height + padding * 2),
                    scale * 96))
                {
                    ds.Clear(Color.FromArgb(0, 255, 255, 255));

                    DrawEffect(ds, canvasBitmap);
                }

                strokeBrush.Scale = new Vector2(1 / scale, 1 / scale);
                strokeVisual.Size = new System.Numerics.Vector2(visualSize.X + padding * 2, visualSize.Y + padding * 2);
                UpdatePreviewRequest?.Invoke(this, EventArgs.Empty);
            }
        }

        private void DrawEffect(CanvasDrawingSession ds, CanvasBitmap canvasBitmap)
        {
            var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

            if (strokeThickness < 3.5f)
            {
                DrawEffectSmallSize(ds, canvasBitmap);
            }
            else
            {
                DrawEffectLargeSize(ds, canvasBitmap);
            }

            var duration = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);
            System.Diagnostics.Debug.WriteLine("DrawEffect duration: {0}", duration);
        }

        private void DrawEffectSmallSize(CanvasDrawingSession ds, CanvasBitmap canvasBitmap)
        {
            var thicknessIntPart = (int)strokeThickness;
            var thicknessFloatPart = strokeThickness - thicknessIntPart;

            using (var commandList = new CanvasCommandList(ds))
            {
                using (var tmpDs = commandList.CreateDrawingSession())
                {
                    tmpDs.DrawImage(canvasBitmap, 0, 0);
                    for (float i = 0; i < thicknessIntPart; i += 0.5f)
                    {
                        DrawImageRound(tmpDs, canvasBitmap, i);
                    }

                    if (thicknessFloatPart >= 0.1f)
                    {
                        DrawImageRound(tmpDs, canvasBitmap, thicknessFloatPart);
                    }
                }

                using var effect = new CompositeEffect()
                {
                    Mode = CanvasComposite.DestinationOut,
                    Sources =
                    {
                        commandList,
                        canvasBitmap
                    }
                };

                ds.DrawImage(effect, padding, padding);
            }

            static void DrawImageRound(CanvasDrawingSession _ds, CanvasBitmap _canvasBitmap, float _offset)
            {
                _ds.DrawImage(_canvasBitmap, -_offset, -_offset);
                _ds.DrawImage(_canvasBitmap, 0, -_offset);
                _ds.DrawImage(_canvasBitmap, _offset, -_offset);
                _ds.DrawImage(_canvasBitmap, -_offset, 0);
                _ds.DrawImage(_canvasBitmap, _offset, 0);
                _ds.DrawImage(_canvasBitmap, -_offset, _offset);
                _ds.DrawImage(_canvasBitmap, 0, _offset);
                _ds.DrawImage(_canvasBitmap, _offset, _offset);
            }
        }

        private void DrawEffectLargeSize(CanvasDrawingSession ds, CanvasBitmap canvasBitmap)
        {
            var blurAmount = strokeThickness;

            using var effect1 = new ShadowEffect()
            {
                ShadowColor = Color.FromArgb(255, 0, 0, 0),
                BlurAmount = blurAmount,
                Source = canvasBitmap,
            };

            using var effect2 = new ColorMatrixEffect
            {
                ColorMatrix = new Matrix5x4
                {

#pragma warning disable format

                    M11 = 1, M12 = 0, M13 = 0, M14 = 0,
                    M21 = 0, M22 = 1, M23 = 0, M24 = 0,
                    M31 = 0, M32 = 0, M33 = 1, M34 = 0,
                    M41 = 0, M42 = 0, M43 = 0, M44 = 20f,
                    M51 = 0, M52 = 0, M53 = 0, M54 = -2

#pragma warning disable format 
                },

                Source = effect1,
            };

            using var effect3 = new CompositeEffect()
            {
                Mode = CanvasComposite.DestinationOut,
                Sources =
                {
                    effect2,
                    canvasBitmap
                }
            };

            ds.DrawImage(effect3, padding, padding);
        }


        public void Dispose()
        {
            if (!disposedValue)
            {
                // TODO: Dispose resources

                disposedValue = true;
            }
        }

        private record struct TextBlockProperties(
            double ActualWidth,
            double ActualHeight,
            FontFamily FontFamily,
            double FontSize,
            FontWeight FontWeight,
            FontStyle FontStyle,
            FontStretch FontStretch,
            Brush Foreground,
            Color? ForegroundColor)
        {
            internal static TextBlockProperties Create(TextBlock textBlock)
            {
                var actualSize = textBlock.ActualSize;

                return new TextBlockProperties(
                    actualSize.X,
                    actualSize.Y,
                    textBlock.FontFamily,
                    textBlock.FontSize,
                    textBlock.FontWeight,
                    textBlock.FontStyle,
                    textBlock.FontStretch,
                    textBlock.Foreground,
                    (textBlock.Foreground as SolidColorBrush)?.Color);
            }
        }
    }
}

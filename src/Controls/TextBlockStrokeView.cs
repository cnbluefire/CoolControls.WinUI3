using CoolControls.WinUI3.Controls.Internals.TextBlockExtensions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;

namespace CoolControls.WinUI3.Controls
{
    [ContentProperty(Name = "TextBlock")]
    public class TextBlockStrokeView : Control
    {
        public TextBlockStrokeView()
        {
            this.DefaultStyleKey = typeof(TextBlockStrokeView);

            BrushHost = new Rectangle()
            {
                Fill = StrokeBrush
            };

            BrushHostCanvas = new Canvas()
            {
                Width = 0,
                Height = 0,
                IsTabStop = false,
                IsHitTestVisible = false,
                Children =
                {
                    BrushHost
                }
            };

            BrushHostCanvas.GetVisualInternal().IsVisible = false;
        }

        private Canvas BrushHostCanvas;
        private Rectangle BrushHost;
        private Grid? LayoutRoot;
        private Border? TextBlockBorder;
        private TextBlockStrokeImpl? textBlockStroke;

        private CompositionVisualSurface? brushHostVisualSurface;
        private CompositionSurfaceBrush? actualStrokeBrush;

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            DisconnectStrokeVisual();

            if (brushHostVisualSurface != null)
            {
                brushHostVisualSurface.SourceVisual = null;
            }
            if (TextBlockBorder != null)
            {
                TextBlockBorder.SizeChanged -= TextBlockBorder_SizeChanged;
            }
            if (LayoutRoot != null)
            {
                LayoutRoot.Children.Remove(BrushHostCanvas);
            }

            LayoutRoot = (Grid)GetTemplateChild(nameof(LayoutRoot));
            TextBlockBorder = (Border)GetTemplateChild(nameof(TextBlockBorder));

            if (brushHostVisualSurface == null)
            {
                brushHostVisualSurface = Utils.Graphics.DeviceHolder.Instance.Compositor.CreateVisualSurface();
            }
            if (actualStrokeBrush == null)
            {
                actualStrokeBrush = Utils.Graphics.DeviceHolder.Instance.Compositor.CreateSurfaceBrush(brushHostVisualSurface);
            }
            brushHostVisualSurface.SourceVisual = BrushHost.GetVisualInternal();

            if (TextBlockBorder != null)
            {
                UpdateBrushHostSize(TextBlockBorder.ActualWidth, TextBlockBorder.ActualHeight);
                TextBlockBorder.SizeChanged += TextBlockBorder_SizeChanged;
            }
            if (LayoutRoot != null)
            {
                LayoutRoot.Children.Add(BrushHostCanvas);
            }

            ConnectStrokeVisual();
        }

        public Brush StrokeBrush
        {
            get { return (Brush)GetValue(StrokeBrushProperty); }
            set { SetValue(StrokeBrushProperty, value); }
        }

        public static readonly DependencyProperty StrokeBrushProperty =
            DependencyProperty.Register("StrokeBrush", typeof(Brush), typeof(TextBlockStrokeView), new PropertyMetadata(null, (s, a) =>
            {
                if (s is TextBlockStrokeView sender && !Equals(a.NewValue, a.OldValue))
                {
                    sender.BrushHost.Fill = a.NewValue as Brush;
                }
            }));



        public double StrokeThickness
        {
            get { return (double)GetValue(StrokeThicknessProperty); }
            set { SetValue(StrokeThicknessProperty, value); }
        }

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register("StrokeThickness", typeof(double), typeof(TextBlockStrokeView), new PropertyMetadata(2d, (s, a) =>
            {
                if (s is TextBlockStrokeView sender && !Equals(a.NewValue, a.OldValue))
                {
                    if (sender.textBlockStroke != null)
                    {
                        sender.textBlockStroke.StrokeThickness = Convert.ToSingle(a.NewValue);
                    }
                }
            }));



        public TextBlock TextBlock
        {
            get { return (TextBlock)GetValue(TextBlockProperty); }
            set { SetValue(TextBlockProperty, value); }
        }

        public static readonly DependencyProperty TextBlockProperty =
            DependencyProperty.Register("TextBlock", typeof(TextBlock), typeof(TextBlockStrokeView), new PropertyMetadata(null, (s, a) =>
            {
                if (s is TextBlockStrokeView sender && !Equals(a.NewValue, a.OldValue))
                {
                    sender.DisconnectStrokeVisual();

                    if (sender.textBlockStroke != null)
                    {
                        sender.textBlockStroke.UpdatePreviewRequest -= sender.TextBlockStroke_UpdatePreviewRequest;
                        sender.textBlockStroke?.Dispose();
                        sender.textBlockStroke = null;
                    }

                    if (a.NewValue is TextBlock textBlock)
                    {
                        sender.textBlockStroke = new TextBlockStrokeImpl(textBlock, Utils.Graphics.DeviceHolder.Instance);
                        sender.textBlockStroke.UpdatePreviewRequest += sender.TextBlockStroke_UpdatePreviewRequest;
                    }

                    sender.ConnectStrokeVisual();
                }
            }));

        private void TextBlockBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateBrushHostSize(e.NewSize.Width, e.NewSize.Height);
        }

        private void UpdateBrushHostSize(double width, double height)
        {
            if (BrushHost != null)
            {
                BrushHost.Width = width;
                BrushHost.Height = height;
            }

            if (brushHostVisualSurface != null)
            {
                brushHostVisualSurface.SourceSize = new System.Numerics.Vector2((float)width, (float)height);
            }
        }

        private async void TextBlockStroke_UpdatePreviewRequest(object? sender, EventArgs e)
        {
            await ((TextBlockStrokeImpl)sender!).UpdatePreviewAsync(TextBlockBorder);
        }

        private void ConnectStrokeVisual()
        {
            if (TextBlockBorder == null || textBlockStroke == null) return;

            ElementCompositionPreview.SetElementChildVisual(TextBlockBorder, textBlockStroke.StrokeVisual);

            if (actualStrokeBrush != null)
            {
                textBlockStroke.StrokeBrush = actualStrokeBrush;
            }
            textBlockStroke.StrokeThickness = (float)StrokeThickness;
        }

        private void DisconnectStrokeVisual()
        {
            if (TextBlockBorder == null) return;

            ElementCompositionPreview.SetElementChildVisual(TextBlockBorder, null);

            if (textBlockStroke != null)
            {
                textBlockStroke.StrokeBrush = null;
            }
        }


    }
}

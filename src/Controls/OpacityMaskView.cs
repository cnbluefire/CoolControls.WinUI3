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
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CoolControls.WinUI3.Controls
{
    [ContentProperty(Name = nameof(Child))]
    public class OpacityMaskView : Control
    {
        public OpacityMaskView()
        {
            this.DefaultStyleKey = typeof(OpacityMaskView);

            hostVisual = ElementCompositionPreview.GetElementVisual(this);
            compositor = hostVisual.Compositor;

            opacityMaskVisualSurface = compositor.CreateVisualSurface();
            opacityMaskVisualBrush = compositor.CreateSurfaceBrush(opacityMaskVisualSurface);
            opacityMaskVisualBrush.HorizontalAlignmentRatio = 0;
            opacityMaskVisualBrush.VerticalAlignmentRatio = 0;
            opacityMaskVisualBrush.Stretch = CompositionStretch.None;

            childVisualSurface = compositor.CreateVisualSurface();
            childVisualBrush = compositor.CreateSurfaceBrush(childVisualSurface);
            childVisualBrush.HorizontalAlignmentRatio = 0;
            childVisualBrush.VerticalAlignmentRatio = 0;
            childVisualBrush.Stretch = CompositionStretch.None;

            maskBrush = compositor.CreateMaskBrush();
            maskBrush.Mask = opacityMaskVisualBrush;
            maskBrush.Source = childVisualBrush;

            redirectVisual = compositor.CreateSpriteVisual();
            redirectVisual.RelativeSizeAdjustment = Vector2.One;
            redirectVisual.Brush = maskBrush;
        }

        private Grid? LayoutRoot;
        private Rectangle? OpacityMaskHost;
        private Border? ChildBorder;
        private Border? ChildOpacityBorder;
        private Canvas? ChildHost;
        private Canvas? OpacityMaskContainer;

        private Visual hostVisual;
        private Compositor compositor;

        private CompositionVisualSurface opacityMaskVisualSurface;
        private CompositionSurfaceBrush opacityMaskVisualBrush;

        private CompositionVisualSurface childVisualSurface;
        private CompositionSurfaceBrush childVisualBrush;

        private CompositionMaskBrush maskBrush;
        private SpriteVisual redirectVisual;

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            DetachVisuals();

            LayoutRoot = GetTemplateChild(nameof(LayoutRoot)) as Grid;
            OpacityMaskHost = GetTemplateChild(nameof(OpacityMaskHost)) as Rectangle;
            ChildBorder = GetTemplateChild(nameof(ChildBorder)) as Border;
            ChildOpacityBorder = GetTemplateChild(nameof(ChildOpacityBorder)) as Border;
            ChildHost = GetTemplateChild(nameof(ChildHost)) as Canvas;
            OpacityMaskContainer = GetTemplateChild(nameof(OpacityMaskContainer)) as Canvas;

            AttachVisuals();
        }

        public UIElement? Child
        {
            get { return (UIElement?)GetValue(ChildProperty); }
            set { SetValue(ChildProperty, value); }
        }

        public Brush? OpacityMask
        {
            get { return (Brush?)GetValue(OpacityMaskProperty); }
            set { SetValue(OpacityMaskProperty, value); }
        }

        public static readonly DependencyProperty ChildProperty =
            DependencyProperty.Register("Child", typeof(UIElement), typeof(OpacityMaskView), new PropertyMetadata(null));

        public static readonly DependencyProperty OpacityMaskProperty =
            DependencyProperty.Register("OpacityMask", typeof(Brush), typeof(OpacityMaskView), new PropertyMetadata(null));

        private void DetachVisuals()
        {
            if (LayoutRoot != null)
            {
                opacityMaskVisualSurface.SourceVisual = null;
                childVisualSurface.SourceVisual = null;

                LayoutRoot.SizeChanged -= LayoutRoot_SizeChanged;

                if (ChildBorder != null)
                {
                    ChildBorder.SizeChanged -= ChildBorder_SizeChanged;
                }

                if (ChildHost != null)
                {
                    ElementCompositionPreview.SetElementChildVisual(ChildHost, null);
                }
            }
        }

        private void AttachVisuals()
        {
            if (LayoutRoot != null)
            {
                LayoutRoot.SizeChanged += LayoutRoot_SizeChanged;

                if (OpacityMaskHost != null)
                {
                    opacityMaskVisualSurface.SourceVisual = ElementCompositionPreview.GetElementVisual(OpacityMaskHost);
                }

                if (ChildBorder != null)
                {
                    ChildBorder.SizeChanged += ChildBorder_SizeChanged;
                    childVisualSurface.SourceVisual = ElementCompositionPreview.GetElementVisual(ChildBorder);
                }

                if (ChildOpacityBorder != null)
                {
                    ElementCompositionPreview.GetElementVisual(ChildOpacityBorder).IsVisible = false;
                }
                if (OpacityMaskContainer != null)
                {
                    ElementCompositionPreview.GetElementVisual(OpacityMaskContainer).IsVisible = false;
                }

                if (ChildHost != null)
                {
                    ElementCompositionPreview.SetElementChildVisual(ChildHost, redirectVisual);
                }

                UpdateSize();
            }
        }

        private void ChildBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSize();
        }

        private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSize();
        }

        private void UpdateSize()
        {
            if (LayoutRoot != null)
            {
                if (OpacityMaskHost != null)
                {
                    OpacityMaskHost.Width = LayoutRoot.ActualWidth;
                    OpacityMaskHost.Height = LayoutRoot.ActualHeight;
                }

                opacityMaskVisualSurface.SourceSize = new Vector2((float)LayoutRoot.ActualWidth, (float)LayoutRoot.ActualHeight);

                if (ChildBorder != null)
                {
                    childVisualSurface.SourceSize = new Vector2((float)ChildBorder.ActualWidth, (float)ChildBorder.ActualHeight);
                }
            }
        }
    }
}

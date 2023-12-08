﻿using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace CoolControls.WinUI3.Controls
{
    internal class RibbedGlassVisualHelper : IDisposable
    {
        private const int MaxPoolSize = 100;
        private static volatile int instanceCount = 0;
        private static object globalLocker = new object();
        private static System.Collections.Concurrent.ConcurrentBag<ChildVisualEntry>? childVisualPool;

        private bool disposedValue;
        private Compositor compositor;
        private float blurAmount = 2;

        private Visual? sourceVisual;

        private CompositionPropertySet propSet;
        private ExpressionAnimation sourceVisualScalePropertyBind;
        private ExpressionAnimation visualSizePropertyBind;
        private ExpressionAnimation childVisualSizePropertyBind;
        private ExpressionAnimation visualSizeBind;
        private ExpressionAnimation childVisualSizeBind;
        private ExpressionAnimation glassMaskSizeBind;
        private ExpressionAnimation sourceVisualScaleBind;
        private ExpressionAnimation childVisualOffsetBind;
        private ExpressionAnimation childVisualBrushTransformBind;
        private ExpressionAnimation blurAmountBind;

        private CompositionVisualSurface sourceVisualSurface;
        private CompositionSurfaceBrush sourceVisualSurfaceBrush;
        private ContainerVisual rootChildVisual;
        private ContainerVisual childVisualsContainer;

        private SpriteVisual glassMaskVisual;
        private SpriteVisual glassMaskSurfaceVisual;
        private CompositionVisualSurface glassMaskSurface;
        private CompositionSurfaceBrush glassMaskBrush;

        private SpriteVisual scaledContentSurfaceVisual;
        private CompositionEffectBrush scaledContentEffectBrush;
        private CompositionEffectBrush scaledContentEffectBlurredBrush;
        private CompositionVisualSurface scaledContentSurface;

        private Stack<ChildVisualEntry> usedChildVisual;

        public RibbedGlassVisualHelper(Compositor compositor)
        {
            Interlocked.Increment(ref instanceCount);

            this.compositor = compositor;

            EnsurePropertiesAndBindings();
            EnsureVisualsContainer();
            EnsureSourceBrush();
            EnsureScaledContentSurface();
            EnsureGlassMaskVisual();

            rootChildVisual.Children.InsertAtTop(glassMaskVisual);

            usedChildVisual = new Stack<ChildVisualEntry>();
        }

        public Visual? SourceVisual
        {
            get => sourceVisual;
            set
            {
                if (sourceVisual != value)
                {
                    if (sourceVisual != null)
                    {
                        propSet.StopAnimation("SourceVisualSize");
                        visualSizePropertyBind.ClearParameter("visual");
                    }

                    sourceVisualSurface.SourceVisual = value;
                    sourceVisual = value;

                    if (sourceVisual != null)
                    {
                        visualSizePropertyBind.SetReferenceParameter("visual", sourceVisual);
                        propSet.StartAnimation("SourceVisualSize", visualSizePropertyBind);
                    }
                    var size = sourceVisual?.Size ?? Vector2.Zero;
                    UpdateSize(size.X, size.Y);
                }
            }
        }

        public Visual EffectVisual => rootChildVisual;

        public float ChildVisualWidth
        {
            get
            {
                if (propSet.TryGetScalar(nameof(ChildVisualWidth), out var value) == CompositionGetValueStatus.Succeeded)
                {
                    return value;
                }
                return 0;
            }
            set
            {
                propSet.InsertScalar(nameof(ChildVisualWidth), value);
            }
        }

        public float SourceVisualScale
        {
            get
            {
                if (propSet.TryGetScalar(nameof(SourceVisualScale), out var value) == CompositionGetValueStatus.Succeeded)
                {
                    return value;
                }
                return 0;
            }
            set
            {
                propSet.InsertScalar(nameof(SourceVisualScale), value);
            }
        }

        public float BlurAmount
        {
            get => blurAmount;
            set
            {
                if (blurAmount != value)
                {
                    blurAmount = value;
                    propSet.InsertScalar(nameof(BlurAmount), value);
                    UpdateScaledContentBlurState();
                }
            }
        }

        #region Ensure Resource

        [MemberNotNull(
            nameof(propSet),
            nameof(sourceVisualScalePropertyBind),
            nameof(visualSizePropertyBind),
            nameof(childVisualSizePropertyBind),
            nameof(visualSizeBind),
            nameof(childVisualSizeBind),
            nameof(glassMaskSizeBind),
            nameof(sourceVisualScaleBind),
            nameof(childVisualOffsetBind),
            nameof(childVisualBrushTransformBind),
            nameof(blurAmountBind))]
        private void EnsurePropertiesAndBindings()
        {
            propSet = compositor.CreatePropertySet();
            propSet.InsertScalar(nameof(SourceVisualScale), 0.25f);
            propSet.InsertScalar(nameof(ChildVisualWidth), 18f);
            propSet.InsertScalar(nameof(BlurAmount), blurAmount);

            propSet.InsertMatrix3x2("ContentTransform", Matrix3x2.Identity);
            propSet.InsertVector2("SourceVisualSize", Vector2.Zero);
            propSet.InsertVector2("ChildVisualSize", Vector2.Zero);
            propSet.InsertScalar("VisualCount", 0f);

            visualSizePropertyBind = compositor.CreateExpressionAnimation("visual.Size");

            sourceVisualScalePropertyBind = compositor.CreateExpressionAnimation("Matrix3x2.CreateFromScale(Vector2(this.Target.SourceVisualScale, 1f))");
            propSet.StartAnimation("ContentTransform", sourceVisualScalePropertyBind);

            childVisualSizePropertyBind = compositor.CreateExpressionAnimation("Vector2(this.Target.ChildVisualWidth + 1f, this.Target.SourceVisualSize.Y)");
            propSet.StartAnimation("ChildVisualSize", childVisualSizePropertyBind);

            visualSizeBind = compositor.CreateExpressionAnimation("prop.SourceVisualSize");
            visualSizeBind.SetReferenceParameter("prop", propSet);

            childVisualSizeBind = compositor.CreateExpressionAnimation("prop.ChildVisualSize");
            childVisualSizeBind.SetReferenceParameter("prop", propSet);

            glassMaskSizeBind = compositor.CreateExpressionAnimation("Vector2(prop.ChildVisualWidth, 1)");
            glassMaskSizeBind.SetReferenceParameter("prop", propSet);

            sourceVisualScaleBind = compositor.CreateExpressionAnimation("prop.ContentTransform");
            sourceVisualScaleBind.SetReferenceParameter("prop", propSet);

            childVisualOffsetBind = compositor.CreateExpressionAnimation("Vector3(this.Target._Index * prop.ChildVisualWidth, 0f, 0f)");
            childVisualOffsetBind.SetReferenceParameter("prop", propSet);

            childVisualBrushTransformBind = compositor.CreateExpressionAnimation("Matrix3x2.CreateFromTranslation(Vector2((prop.ChildVisualWidth / (1 - prop.SourceVisualScale) / prop.VisualCount - prop.ChildVisualWidth * prop.SourceVisualScale) * this.Target._Index, 0))");
            childVisualBrushTransformBind.SetReferenceParameter("prop", propSet);

            blurAmountBind = compositor.CreateExpressionAnimation("prop.BlurAmount");
            blurAmountBind.SetReferenceParameter("prop", propSet);
        }

        [MemberNotNull(
            nameof(sourceVisualSurface),
            nameof(sourceVisualSurfaceBrush))]
        private void EnsureSourceBrush()
        {
            sourceVisualSurface = compositor.CreateVisualSurface();

            sourceVisualSurface.StartAnimation("SourceSize", visualSizeBind);

            sourceVisualSurfaceBrush = compositor.CreateSurfaceBrush(sourceVisualSurface);
            sourceVisualSurfaceBrush.Stretch = CompositionStretch.None;
            sourceVisualSurfaceBrush.HorizontalAlignmentRatio = 0;
            sourceVisualSurfaceBrush.VerticalAlignmentRatio = 0;
        }

        [MemberNotNull(
            nameof(scaledContentSurfaceVisual),
            nameof(scaledContentEffectBrush),
            nameof(scaledContentEffectBlurredBrush),
            nameof(scaledContentSurface))]
        private void EnsureScaledContentSurface()
        {
            scaledContentSurfaceVisual = compositor.CreateSpriteVisual();
            scaledContentSurfaceVisual.StartAnimation("Size", visualSizeBind);

            var transformEffect = new Transform2DEffect()
            {
                Name = "TransformEffect",
                Source = new CompositionEffectSourceParameter("source"),
                TransformMatrix = Matrix3x2.Identity
            };

            scaledContentEffectBrush = compositor
                .CreateEffectFactory(transformEffect, new[] { "TransformEffect.TransformMatrix" })
                .CreateBrush();

            scaledContentEffectBrush.SetSourceParameter("source", sourceVisualSurfaceBrush);
            scaledContentEffectBrush.Properties.StartAnimation("TransformEffect.TransformMatrix", sourceVisualScaleBind);

            var blurredEffect = new GaussianBlurEffect()
            {
                Name = "BlurEffect",
                BlurAmount = 2,
                BorderMode = EffectBorderMode.Soft,
                Source = new Transform2DEffect()
                {
                    Name = "TransformEffect",
                    Source = new CompositionEffectSourceParameter("source"),
                    TransformMatrix = Matrix3x2.Identity
                }
            };

            scaledContentEffectBlurredBrush = compositor
                .CreateEffectFactory(blurredEffect, new[] { "TransformEffect.TransformMatrix", "BlurEffect.BlurAmount" })
                .CreateBrush();

            scaledContentEffectBlurredBrush.SetSourceParameter("source", sourceVisualSurfaceBrush);
            scaledContentEffectBlurredBrush.Properties.StartAnimation("TransformEffect.TransformMatrix", sourceVisualScaleBind);
            scaledContentEffectBlurredBrush.Properties.StartAnimation("BlurEffect.BlurAmount", blurAmountBind);

            scaledContentSurface = compositor.CreateVisualSurface();
            scaledContentSurface.SourceVisual = scaledContentSurfaceVisual;
            scaledContentSurface.StartAnimation("SourceSize", visualSizeBind);

            UpdateScaledContentBlurState();
        }

        [MemberNotNull(
            nameof(rootChildVisual),
            nameof(childVisualsContainer))]
        private void EnsureVisualsContainer()
        {
            rootChildVisual = compositor.CreateContainerVisual();
            rootChildVisual.Clip = compositor.CreateInsetClip();
            rootChildVisual.StartAnimation("Size", visualSizeBind);

            childVisualsContainer = compositor.CreateContainerVisual();
            childVisualsContainer.RelativeSizeAdjustment = Vector2.One;

            rootChildVisual.Children.InsertAtTop(childVisualsContainer);
        }

        [MemberNotNull(
            nameof(glassMaskSurfaceVisual),
            nameof(glassMaskSurface),
            nameof(glassMaskBrush),
            nameof(glassMaskVisual))]
        private void EnsureGlassMaskVisual()
        {
            glassMaskSurfaceVisual = compositor.CreateSpriteVisual();
            glassMaskSurfaceVisual.StartAnimation("Size", glassMaskSizeBind);

            var gradientBrush = compositor.CreateLinearGradientBrush();
            gradientBrush.StartPoint = new Vector2(0, 0);
            gradientBrush.EndPoint = new Vector2(1, 0);
            gradientBrush.MappingMode = CompositionMappingMode.Relative;
            gradientBrush.ExtendMode = CompositionGradientExtendMode.Clamp;
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0f, Color.FromArgb(20, 0, 0, 0)));
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.25f, Color.FromArgb(0, 0, 0, 0)));
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.75f, Color.FromArgb(0, 0, 0, 0)));
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(1f, Color.FromArgb(20, 0, 0, 0)));

            glassMaskSurfaceVisual.Brush = gradientBrush;

            glassMaskSurface = compositor.CreateVisualSurface();
            glassMaskSurface.SourceVisual = glassMaskSurfaceVisual;
            glassMaskSurface.StartAnimation("SourceSize", glassMaskSizeBind);

            glassMaskBrush = compositor.CreateSurfaceBrush(glassMaskSurface);
            glassMaskBrush.HorizontalAlignmentRatio = 0;
            glassMaskBrush.VerticalAlignmentRatio = 0;
            glassMaskBrush.Stretch = CompositionStretch.None;

            var maskTileEffect = new BorderEffect()
            {
                ExtendX = Microsoft.Graphics.Canvas.CanvasEdgeBehavior.Wrap,
                ExtendY = Microsoft.Graphics.Canvas.CanvasEdgeBehavior.Clamp,
                Source = new CompositionEffectSourceParameter("source"),
            };
            var maskTiledBrush = compositor.CreateEffectFactory(maskTileEffect).CreateBrush();
            maskTiledBrush.SetSourceParameter("source", glassMaskBrush);
            glassMaskVisual = compositor.CreateSpriteVisual();
            glassMaskVisual.StartAnimation("Size", visualSizeBind);
            glassMaskVisual.Brush = maskTiledBrush;
        }

        #endregion Ensure Resource


        private void UpdateScaledContentBlurState()
        {
            if (blurAmount > 0)
            {
                scaledContentSurfaceVisual.Brush = scaledContentEffectBlurredBrush;
            }
            else
            {
                scaledContentSurfaceVisual.Brush = scaledContentEffectBrush;
            }
        }


        internal void UpdateSize(double width, double height)
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(RibbedGlassVisualHelper));

            int visualCount = 0;

            var childVisualWidth = ChildVisualWidth;
            var sourceVisualScale = this.SourceVisualScale;

            if (width > 0 && height > 0 && sourceVisual != null)
            {
                visualCount = (int)Math.Ceiling(width / ChildVisualWidth) + 1;
            }

            propSet.InsertScalar("VisualCount", visualCount);

            while (usedChildVisual.Count > visualCount)
            {
                var entry = usedChildVisual.Pop();

                childVisualsContainer.Children.Remove(entry.Visual);
                entry.Visual.StopAnimation("Size");
                entry.Visual.StopAnimation("Offset");
                entry.Brush.StopAnimation("TransformMatrix");
                entry.Visual.Properties.InsertScalar("_Index", -1);
                entry.Brush.Properties.InsertScalar("_Index", -1);
                entry.Brush.Surface = null;

                ReturnChildVisualEntry(ref entry);
            }

            while (usedChildVisual.Count < visualCount)
            {
                var entry = RentChildVisualEntry(compositor);
                entry.Visual.Properties.InsertScalar("_Index", usedChildVisual.Count);
                entry.Brush.Properties.InsertScalar("_Index", usedChildVisual.Count);

                usedChildVisual.Push(entry);

                entry.Visual.StartAnimation("Size", childVisualSizeBind);
                entry.Visual.StartAnimation("Offset", childVisualOffsetBind);
                entry.Brush.StartAnimation("TransformMatrix", childVisualBrushTransformBind);
                childVisualsContainer.Children.InsertAtTop(entry.Visual);

                entry.Brush.Surface = scaledContentSurface;
            }
        }


        private static ChildVisualEntry CreateChildVisualEntry(Compositor compositor)
        {
            var visual = compositor.CreateSpriteVisual();

            var brush = compositor.CreateSurfaceBrush();
            brush.Stretch = CompositionStretch.None;
            brush.HorizontalAlignmentRatio = 0;
            brush.VerticalAlignmentRatio = 0;

            visual.Brush = brush;

            return new ChildVisualEntry(visual, brush);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                lock (globalLocker)
                {
                    var _instanceCount = Interlocked.Decrement(ref instanceCount);

                    sourceVisual = null;
                    UpdateSize(0, 0);

                    disposedValue = true;

                    rootChildVisual?.Children.RemoveAll();
                    rootChildVisual?.Dispose();
                    rootChildVisual = null!;

                    childVisualsContainer?.Children.RemoveAll();
                    childVisualsContainer?.Dispose();
                    childVisualsContainer = null!;

                    glassMaskVisual?.Dispose();
                    glassMaskVisual = null!;
                    glassMaskSurfaceVisual?.Dispose();
                    glassMaskSurfaceVisual = null!;
                    glassMaskSurface?.Dispose();
                    glassMaskSurface = null!;
                    glassMaskBrush?.Dispose();
                    glassMaskBrush = null!;

                    scaledContentSurface?.Dispose();
                    scaledContentSurface = null!;
                    scaledContentSurfaceVisual?.Dispose();
                    scaledContentSurfaceVisual = null!;
                    scaledContentEffectBrush?.Dispose();
                    scaledContentEffectBrush = null!;
                    scaledContentEffectBlurredBrush?.Dispose();
                    scaledContentEffectBlurredBrush = null!;

                    sourceVisualSurfaceBrush?.Dispose();
                    sourceVisualSurfaceBrush = null!;
                    sourceVisualSurface?.Dispose();
                    sourceVisualSurface = null!;

                    sourceVisualScalePropertyBind?.Dispose();
                    sourceVisualScalePropertyBind = null!;
                    visualSizePropertyBind?.Dispose();
                    visualSizePropertyBind = null!;
                    childVisualSizePropertyBind?.Dispose();
                    childVisualSizePropertyBind = null!;
                    visualSizeBind?.Dispose();
                    visualSizeBind = null!;
                    childVisualSizeBind?.Dispose();
                    childVisualSizeBind = null!;
                    sourceVisualScaleBind?.Dispose();
                    sourceVisualScaleBind = null!;
                    childVisualOffsetBind?.Dispose();
                    childVisualOffsetBind = null!;
                    childVisualBrushTransformBind?.Dispose();
                    childVisualBrushTransformBind = null!;
                    glassMaskSizeBind?.Dispose();
                    glassMaskSizeBind = null!;
                    blurAmountBind?.Dispose();
                    blurAmountBind = null!;

                    propSet?.Dispose();
                    propSet = null!;

                    if (_instanceCount == 0)
                    {
                        if (childVisualPool != null)
                        {
                            var arr = childVisualPool.ToArray();
                            childVisualPool = null;
                            if (disposing)
                            {

                                for (int i = 0; i < arr.Length; i++)
                                {
                                    arr[i].Dispose();
                                }
                            }
                        }
                    }

                }

            }
        }

        ~RibbedGlassVisualHelper()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }


        private static ChildVisualEntry RentChildVisualEntry(Compositor compositor)
        {
            var pool = childVisualPool;
            if (pool != null && pool.TryTake(out var value))
            {
                return value;
            }

            return CreateChildVisualEntry(compositor);
        }

        private static void ReturnChildVisualEntry(ref ChildVisualEntry? entry)
        {
            if (entry == null) return;

            var _entry = entry;
            entry = null;

            lock (globalLocker)
            {
                var pool = childVisualPool;
                if (pool == null)
                {
                    childVisualPool = new System.Collections.Concurrent.ConcurrentBag<ChildVisualEntry>();
                    pool = childVisualPool;
                }

                if (pool.Count < MaxPoolSize)
                {
                    pool.Add(_entry);
                    return;
                }
            }

            _entry.Dispose();
        }

        private class ChildVisualEntry : IDisposable
        {
            private bool disposedValue;

            public ChildVisualEntry(Visual visual, CompositionSurfaceBrush brush)
            {
                Visual = visual;
                Brush = brush;
            }

            public Visual Visual { get; private set; }

            public CompositionSurfaceBrush Brush { get; private set; }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    Visual?.Dispose();
                    Visual = null!;

                    Brush?.Dispose();
                    Brush = null!;

                    disposedValue = true;
                }
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }
    }
}

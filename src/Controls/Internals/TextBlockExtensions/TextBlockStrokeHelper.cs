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
using System.Collections;
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
    internal class TextBlockStrokeHelper : IDisposable
    {
        internal const float padding = 10;

        private bool disposedValue;

        private ICollection<IDisposable> disposableObjects;
        private WeakReference<TextBlock> weakTextBlock;
        private float strokeThickness = 0f;

        private Compositor compositor;
        private Queue<SpriteVisual> cachedVisuals;
        private ContainerVisual strokeVisualForSurface;
        private CompositionVisualSurface strokeVisualSurface;
        private CompositionSurfaceBrush strokeVisualSurfaceBrush;
        private CompositionEffectBrush strokeEffectBrush;

        private CompositionSurfaceBrush textBlockAlphaMask;
        private SpriteVisual alphaMaskSurfaceVisual;
        private CompositionVisualSurface alphaMaskSurface;
        private CompositionSurfaceBrush alphaMaskSurfaceBrush;

        private ContainerVisual rootVisual;
        private SpriteVisual strokeBrushVisual;
        private ExpressionAnimation sizeBind;
        private ExpressionAnimation surfaceSizeBind;

        private CompositionBrush? strokeFillBrush;

        public TextBlockStrokeHelper(TextBlock textBlock)
        {
            disposableObjects = new DisposableObjects();
            cachedVisuals = new Queue<SpriteVisual>();

            weakTextBlock = new WeakReference<TextBlock>(textBlock);

            textBlock.Loaded += (new WeakEventListener<TextBlock, object, RoutedEventArgs>(textBlock)
            {
                OnEventAction = (i, s, a) => UpdateStrokeState(),
                OnDetachAction = i => textBlock.Loaded -= i.OnEvent
            }).OnEvent;

            textBlock.Unloaded += (new WeakEventListener<TextBlock, object, RoutedEventArgs>(textBlock)
            {
                OnEventAction = (i, s, a) => UpdateStrokeState(),
                OnDetachAction = i => textBlock.Unloaded -= i.OnEvent
            }).OnEvent;

            textBlock.SizeChanged += (new WeakEventListener<TextBlock, object, SizeChangedEventArgs>(textBlock)
            {
                OnEventAction = (i, s, a) => UpdateStrokeState(),
                OnDetachAction = i => textBlock.SizeChanged -= i.OnEvent
            }).OnEvent;

            var textBlockVisual = textBlock.GetVisualInternal();
            compositor = textBlockVisual.Compositor;

            sizeBind = compositor.CreateExpressionAnimation("visual.Size")
                .AddToDisposableObjects(disposableObjects);
            sizeBind.SetReferenceParameter("visual", textBlockVisual);

            surfaceSizeBind = compositor.CreateExpressionAnimation($"Vector2(visual.Size.X + {padding * 2}, visual.Size.Y + {padding * 2})")
                .AddToDisposableObjects(disposableObjects);
            surfaceSizeBind.SetReferenceParameter("visual", textBlockVisual);

            strokeBrushVisual = compositor.CreateSpriteVisual()
                .AddToDisposableObjects(disposableObjects);
            strokeBrushVisual.Offset = new Vector3(-padding, -padding, 0);
            strokeBrushVisual.StartAnimation("Size", surfaceSizeBind);

            rootVisual = compositor.CreateContainerVisual()
                .AddToDisposableObjects(disposableObjects);
            rootVisual.StartAnimation("Size", sizeBind);

            textBlockAlphaMask = (CompositionSurfaceBrush)textBlock.GetAlphaMask();

            alphaMaskSurfaceVisual = compositor.CreateSpriteVisual()
                .AddToDisposableObjects(disposableObjects);
            alphaMaskSurfaceVisual.Brush = textBlockAlphaMask;
            alphaMaskSurfaceVisual.StartAnimation("Size", sizeBind);

            alphaMaskSurface = compositor.CreateVisualSurface()
                .AddToDisposableObjects(disposableObjects);
            alphaMaskSurface.SourceVisual = alphaMaskSurfaceVisual;
            alphaMaskSurface.StartAnimation("SourceSize", sizeBind);

            alphaMaskSurfaceBrush = compositor.CreateSurfaceBrush(alphaMaskSurface)
                .AddToDisposableObjects(disposableObjects);
            alphaMaskSurfaceBrush.HorizontalAlignmentRatio = 0;
            alphaMaskSurfaceBrush.VerticalAlignmentRatio = 0;
            alphaMaskSurfaceBrush.Stretch = CompositionStretch.None;

            strokeVisualForSurface = compositor.CreateContainerVisual()
                .AddToDisposableObjects(disposableObjects);
            strokeVisualForSurface.StartAnimation("Size", sizeBind);

            strokeVisualSurface = compositor.CreateVisualSurface()
                .AddToDisposableObjects(disposableObjects);
            strokeVisualSurface.SourceVisual = strokeVisualForSurface;
            strokeVisualSurface.StartAnimation("SourceSize", surfaceSizeBind);
            strokeVisualSurface.SourceOffset = new Vector2(-padding, -padding);

            strokeVisualSurfaceBrush = compositor.CreateSurfaceBrush(strokeVisualSurface)
                .AddToDisposableObjects(disposableObjects);
            strokeVisualSurfaceBrush.HorizontalAlignmentRatio = 0;
            strokeVisualSurfaceBrush.VerticalAlignmentRatio = 0;
            strokeVisualSurfaceBrush.Stretch = CompositionStretch.None;

            using var transform2dEffect = new Transform2DEffect()
            {
                TransformMatrix = Matrix3x2.CreateTranslation(padding, padding),
                Source = new CompositionEffectSourceParameter("alphaMask"),
            };

            using var compositeEffect = new CompositeEffect()
            {
                Mode = CanvasComposite.DestinationOut,
                Sources =
                {
                    new CompositionEffectSourceParameter("visualSurface"),
                    transform2dEffect
                }
            };

            using var alphaMaskEffect = new AlphaMaskEffect()
            {
                AlphaMask = compositeEffect,
                Source = new CompositionEffectSourceParameter("source"),
            };

            strokeEffectBrush = compositor.CreateEffectFactory(alphaMaskEffect).CreateBrush()
                .AddToDisposableObjects(disposableObjects);
            strokeEffectBrush.SetSourceParameter("visualSurface", strokeVisualSurfaceBrush);
            strokeEffectBrush.SetSourceParameter("alphaMask", alphaMaskSurfaceBrush);
            strokeEffectBrush.SetSourceParameter("source", null);

            strokeBrushVisual.Brush = strokeEffectBrush;

            UpdateStrokeState(true);
        }

        internal CompositionBrush? StrokeBrush
        {
            get => strokeFillBrush;
            set
            {
                if (strokeFillBrush != value)
                {
                    strokeFillBrush = value;
                    strokeEffectBrush.SetSourceParameter("source", value);

                    UpdateStrokeState(true);
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

                    UpdateStrokeState(true);
                }
            }
        }

        public Visual StrokeVisual => rootVisual;

        private bool IsStrokeEnabled =>
            !disposedValue
            && strokeThickness > 0
            && strokeFillBrush != null
            && weakTextBlock.TryGetTarget(out var target)
            && target.IsLoaded
            && target.ActualWidth > 0
            && target.ActualHeight > 0;


        private void UpdateStrokeState(bool forceUpdate = false)
        {
            if (IsStrokeEnabled)
            {
                if (rootVisual.Children.Count == 0)
                {
                    rootVisual.Children.InsertAtTop(strokeBrushVisual);
                }
            }
            else if (!disposedValue)
            {
                rootVisual.Children.RemoveAll();
            }

            UpdateStrokeVisualSurface(forceUpdate);
        }

        private void UpdateStrokeVisualSurface(bool forceUpdate)
        {
            if (disposedValue) return;

            int visualCount = 0;

            var thickness = Math.Clamp(strokeThickness, 0, 6);

            if (IsStrokeEnabled)
            {
                visualCount = (int)Math.Ceiling(thickness) * 8;
            }

            if (!forceUpdate && visualCount == strokeVisualForSurface.Children.Count) return;

            while (strokeVisualForSurface.Children.Count > visualCount)
            {
                var visual = (SpriteVisual)strokeVisualForSurface.Children.First();
                strokeVisualForSurface.Children.Remove(visual);
                visual.StopAnimation("Size");
                visual.Brush = null;
                cachedVisuals.Enqueue(visual);
            }

            while (strokeVisualForSurface.Children.Count < visualCount)
            {
                SpriteVisual visual;
                if (cachedVisuals.Count > 0)
                {
                    visual = cachedVisuals.Dequeue();
                }
                else
                {
                    visual = compositor.CreateSpriteVisual().AddToDisposableObjects(disposableObjects);
                    visual.StartAnimation("Size", sizeBind);
                    visual.Brush = alphaMaskSurfaceBrush;
                }

                strokeVisualForSurface.Children.InsertAtTop(visual);
            }

            int index = 0;
            foreach (var visual in strokeVisualForSurface.Children)
            {
                var offset = Math.Min(index / 8f, thickness);

                switch (index % 8)
                {
                    case 0: visual.Offset = new Vector3(-offset, -offset, 0); break;
                    case 1: visual.Offset = new Vector3(0, -offset, 0); break;
                    case 2: visual.Offset = new Vector3(offset, -offset, 0); break;

                    case 3: visual.Offset = new Vector3(-offset, 0, 0); break;
                    case 4: visual.Offset = new Vector3(offset, 0, 0); break;

                    case 5: visual.Offset = new Vector3(-offset, offset, 0); break;
                    case 6: visual.Offset = new Vector3(0, offset, 0); break;
                    case 7: visual.Offset = new Vector3(offset, offset, 0); break;
                }

                index++;
            }
        }

        public void Dispose()
        {
            if (!disposedValue)
            {



                disposedValue = true;
            }
        }
    }

    file class DisposableObjects : ICollection<IDisposable>, IDisposable
    {
        private bool disposedValue;

        private List<IDisposable> objects = new List<IDisposable>();

        public void Add(IDisposable item)
        {
            objects.Add(item);
        }

        #region NotImplemented

        public int Count => throw new NotImplementedException();

        public bool IsReadOnly => throw new NotImplementedException();

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Contains(IDisposable item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(IDisposable[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        #endregion NotImplemented

        public void Dispose()
        {
            if (!disposedValue)
            {
                var objects = Interlocked.Exchange(ref this.objects, null!);

                for (int i = objects.Count - 1; i >= 0; i--)
                {
                    objects[i].Dispose();
                }

                disposedValue = true;
            }
        }

        public IEnumerator<IDisposable> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public bool Remove(IDisposable item)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }

    file static class DisposableObjectsExtensions
    {
        public static T AddToDisposableObjects<T>(this T obj, ICollection<IDisposable> disposableObjects) where T : IDisposable
        {
            disposableObjects.Add(obj);
            return obj;
        }
    }
}

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoolControls.WinUI3.Utils.Graphics
{
    internal class DeviceHolder : IDisposable
    {
        private static object globalLocker = new object();
        private static DeviceHolder? instance;

        internal static DeviceHolder Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (globalLocker)
                    {
                        if (instance == null)
                        {
                            instance = new DeviceHolder(
                                CompositionTarget.GetCompositorForCurrentThread(),
                                DispatcherQueue.GetForCurrentThread(),
                                false);
                        }
                    }
                }
                return instance;
            }
        }

        private readonly Compositor compositor;
        private readonly DispatcherQueue dispatcherQueue;
        private CanvasDevice canvasDevice;
        private DeviceLostHelper deviceLostHelper;
        private CompositionGraphicsDevice graphicsDevice;
        private object locker = new object();
        private bool disposedValue;
        private SemaphoreSlim drawLocker;

        public DeviceHolder(Compositor compositor, DispatcherQueue dispatcherQueue, bool forceSoftwareRenderer = false)
        {
            this.canvasDevice = new CanvasDevice(forceSoftwareRenderer);
            this.deviceLostHelper = new DeviceLostHelper(this.canvasDevice);
            this.deviceLostHelper.DeviceLost += DeviceLostHelper_DeviceLost;
            this.compositor = compositor;
            this.dispatcherQueue = dispatcherQueue;
            ForceSoftwareRenderer = forceSoftwareRenderer;

            graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, canvasDevice);
            drawLocker = new SemaphoreSlim(1, 1);
        }

        public bool ForceSoftwareRenderer { get; }

        public Compositor Compositor => compositor;

        public CanvasDevice CanvasDevice => canvasDevice;

        public CompositionGraphicsDevice GraphicsDevice => graphicsDevice;

        public async Task<IDisposable> LockAsync()
        {
            await drawLocker.WaitAsync();
            return new DrawLocker(drawLocker);
        }

        private void DeviceLostHelper_DeviceLost(object? sender, EventArgs e)
        {
            ((DeviceLostHelper)sender!).DeviceLost -= DeviceLostHelper_DeviceLost;
            dispatcherQueue.TryEnqueue(() =>
            {
                lock (locker)
                {

                    var deviceLostHelper = this.deviceLostHelper;
                    var oldCanvasDevice = this.canvasDevice;

                    this.canvasDevice = new CanvasDevice(ForceSoftwareRenderer);
                    this.deviceLostHelper = new DeviceLostHelper(canvasDevice);
                    this.deviceLostHelper.DeviceLost += DeviceLostHelper_DeviceLost;

                    CanvasComposition.SetCanvasDevice(graphicsDevice, this.canvasDevice);

                    oldCanvasDevice.Dispose();
                    deviceLostHelper.Dispose();
                }
            });
        }


        public void Dispose()
        {
            lock (locker)
            {
                if (!disposedValue)
                {
                    if (deviceLostHelper != null)
                    {
                        deviceLostHelper.DeviceLost -= DeviceLostHelper_DeviceLost;
                        deviceLostHelper.Dispose();

                        graphicsDevice?.Dispose();
                        graphicsDevice = null!;

                        canvasDevice?.Dispose();
                        canvasDevice = null!;
                    }


                    disposedValue = true;
                }
            }
        }

        private class DrawLocker : IDisposable
        {
            private SemaphoreSlim drawLocker;

            public DrawLocker(SemaphoreSlim drawLocker)
            {
                this.drawLocker = drawLocker;
            }

            public void Dispose()
            {
                var d = this.drawLocker;
                this.drawLocker = null!;
                d?.Release();
            }
        }

    }
}

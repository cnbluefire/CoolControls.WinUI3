using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using WinRT;

namespace CoolControls.WinUI3.Utils.Graphics
{
    internal class DeviceLostHelper : IDisposable
    {
        private bool disposedValue;
        private IDirect3DDevice direct3DDevice;
        private Windows.Win32.System.Threading.PTP_WAIT_CALLBACK callback;
        private EventWaitHandle waitHandle;
        private PTP_WAIT threadPoolWait;
        private uint cookie;
        private uint cookie2;
        private bool deviceLostRaised;
        private object locker = new object();

        internal unsafe DeviceLostHelper(IDirect3DDevice direct3DDevice)
        {
            this.direct3DDevice = direct3DDevice ?? throw new ArgumentNullException(nameof(direct3DDevice));

            callback = new Windows.Win32.System.Threading.PTP_WAIT_CALLBACK(Callback);

            threadPoolWait = Windows.Win32.PInvoke.CreateThreadpoolWait(callback, (void*)0, default(TP_CALLBACK_ENVIRON_V3));
            waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

            Windows.Win32.PInvoke.SetThreadpoolWait(threadPoolWait, waitHandle.SafeWaitHandle, null);

            var d3d11Device = GetInterface<Windows.Win32.Graphics.Direct3D11.ID3D11Device4>(direct3DDevice);
            d3d11Device.RegisterDeviceRemovedEvent((HANDLE)waitHandle.SafeWaitHandle.DangerousGetHandle(), out cookie);

            try
            {
                var dxgiDevice = GetInterface<Windows.Win32.Graphics.Dxgi.IDXGIDevice>(direct3DDevice);
                dxgiDevice.GetAdapter(out var adapter);
                var adapter1 = (Windows.Win32.Graphics.Dxgi.IDXGIAdapter1)adapter;
                Windows.Win32.Graphics.Dxgi.DXGI_ADAPTER_DESC1 desc = default;
                adapter1.GetDesc1(&desc);
                if ((desc.Flags & (uint)Windows.Win32.Graphics.Dxgi.DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) == 0)
                {
                    try
                    {
                        var iid = typeof(Windows.Win32.Graphics.Dxgi.IDXGIFactory7).GUID;
                        adapter1.GetParent(&iid, out var ppParent);
                        var factory = (Windows.Win32.Graphics.Dxgi.IDXGIFactory7)ppParent;
                        factory.RegisterAdaptersChangedEvent((HANDLE)waitHandle.SafeWaitHandle.DangerousGetHandle(), out cookie2);
                    }
                    catch { }
                }
            }
            catch { }
        }

        internal unsafe bool IsAdaptersChanged()
        {
            var dxgiDevice = GetInterface<Windows.Win32.Graphics.Dxgi.IDXGIDevice>(direct3DDevice);
            dxgiDevice.GetAdapter(out var adapter);
            var iid = typeof(Windows.Win32.Graphics.Dxgi.IDXGIFactory1).GUID;
            adapter.GetParent(&iid, out var ppParent);
            return ((Windows.Win32.Graphics.Dxgi.IDXGIFactory1)ppParent).IsCurrent();
        }

        internal bool IsDeviceLost()
        {
            return GetDeviceRemovedReason() < 0;
        }

        private unsafe int GetDeviceRemovedReason()
        {
            try
            {
                GetInterface<Windows.Win32.Graphics.Direct3D11.ID3D11Device>(direct3DDevice).GetDeviceRemovedReason();
                return 0;
            }
            catch (Exception ex)
            {
                return ex.HResult;
            }
        }

        private static unsafe T GetInterface<T>(IDirect3DDevice direct3DDevice) where T : class
        {
            var type = typeof(T);
            if (!type.IsInterface || type.GetCustomAttribute<ComImportAttribute>() == null) throw new ArgumentException(nameof(T));

            var dxgiDeviceAccess = direct3DDevice.As<Windows.Win32.System.WinRT.Direct3D11.IDirect3DDxgiInterfaceAccess>();

            var guid = typeof(T).GUID;
            dxgiDeviceAccess.GetInterface(&guid, out var _d3d11Device);
            return (T)_d3d11Device;
        }

        private unsafe void Callback(PTP_CALLBACK_INSTANCE Instance, void* Context, PTP_WAIT Wait, uint WaitResult)
        {
            lock (locker)
            {
                if (!deviceLostRaised)
                {
                    deviceLostRaised = true;
                    DeviceLost?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler? DeviceLost;

        public unsafe void Dispose()
        {
            if (!disposedValue)
            {
                Windows.Win32.PInvoke.CloseThreadpoolWait(threadPoolWait);
                var direct3DDevice = this.direct3DDevice;
                this.direct3DDevice = null!;

                if (direct3DDevice != null)
                {
                    try
                    {
                        var d3d11Device = GetInterface<Windows.Win32.Graphics.Direct3D11.ID3D11Device4>(direct3DDevice);
                        d3d11Device.UnregisterDeviceRemoved(cookie);
                    }
                    catch { }

                    try
                    {
                        var dxgiDevice = GetInterface<Windows.Win32.Graphics.Dxgi.IDXGIDevice>(direct3DDevice);
                        dxgiDevice.GetAdapter(out var adapter);

                        var iid = typeof(Windows.Win32.Graphics.Dxgi.IDXGIFactory7).GUID;
                        adapter.GetParent(&iid, out var ppParent);
                        var factory = (Windows.Win32.Graphics.Dxgi.IDXGIFactory7)ppParent;
                        factory.UnregisterAdaptersChangedEvent(cookie2);
                    }
                    catch { }

                    waitHandle?.Dispose();
                }

                disposedValue = true;
            }
        }
    }
}

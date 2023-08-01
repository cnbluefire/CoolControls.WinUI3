using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoolControls.WinUI3.Utils
{
    internal static class VersionHelper
    {
        private static bool? isWindows11OrGreater;

        internal static bool IsWindows11OrGreater() =>
            isWindows11OrGreater ??= Environment.OSVersion.Version >= new Version(10, 0, 22000, 0);
    }
}

using CoolControls.WinUI3.Utils;
using CoolControls.WinUI3.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CoolControls.WinUI3
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private IReadOnlyList<NavigationItemModel>? navigationItems;

        public MainWindow()
        {
            this.InitializeComponent();

            if (VersionHelper.IsWindows11OrGreater())
            {
                SystemBackdrop = new MicaBackdrop();
            }
            else
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }

            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(TitleBarGrid);

            var dpi = GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(800 * dpi / 96), (int)(600 * dpi / 96)));
        }

        private IReadOnlyList<NavigationItemModel> NavigationItems => (navigationItems ??= new List<NavigationItemModel>()
        {
            new NavigationItemModel("OpacityMaskView", "OpacityMaskView", typeof(OpacityMaskViewPage)),
            new NavigationItemModel("AutoScrollView", "AutoScrollView", typeof(AutoScrollViewPage)),
        });

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationItemModel item)
            {
                MainFrame.Navigate(item.PageType);
            }
        }

        private void LayoutRoot_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavigationItems[0];
        }

        private bool IsPaneToggleButtonVisible(NavigationViewDisplayMode mode)
        {
            return mode == NavigationViewDisplayMode.Minimal;
        }

        private bool IsPaneOpen(NavigationViewDisplayMode mode, bool isPaneOpen)
        {
            return mode == NavigationViewDisplayMode.Expanded ? true : isPaneOpen;
        }

        private Thickness MainFrameMargin(NavigationViewDisplayMode mode) => mode switch
        {
            NavigationViewDisplayMode.Expanded => new Thickness(0),
            _ => new Thickness(0, 20, 0, 0)
        };

        private Thickness TitleBarMargin(NavigationViewDisplayMode mode) => mode switch
        {
            NavigationViewDisplayMode.Expanded => new Thickness(240, 0, 0, 0),
            _ => new Thickness(48, 0, 0, 0)
        };

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint hWnd);
    }

    public record class NavigationItemModel(string Name, string DisplayName, Type PageType);
}

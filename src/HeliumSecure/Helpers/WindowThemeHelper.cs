using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HeliumSecure.Helpers;

public static class WindowThemeHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    public static void EnableDarkTitleBar(Window window)
    {
        if (window.IsLoaded)
        {
            ApplyDarkMode(window);
        }
        else
        {
            window.SourceInitialized += (s, e) => ApplyDarkMode(window);
        }
    }

    private static void ApplyDarkMode(Window window)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            nint handle = helper.Handle;
            if (handle == nint.Zero) return;

            int trueValue = 1;
            // Try standard attribute 20 (Win10 20H1+ and Win11)
            int result = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
            if (result != 0)
            {
                // Fallback to attribute 19
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueValue, sizeof(int));
            }
        }
        catch
        {
            // Fail gracefully if running on older OS
        }
    }
}

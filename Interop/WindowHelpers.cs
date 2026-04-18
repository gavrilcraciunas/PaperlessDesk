using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT;

namespace PaperlessDesktop.Interop;

/// <summary>
/// Interop helpers for file picker integration with WinUI windows.
/// </summary>
public static class WindowNative
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public static IntPtr GetWindowHandle(Window window)
    {
        try
        {
            // Method 1: Try WinRT As<T> pattern for IWindowNative
            return window.As<IWindowNative>().WindowHandle;
        }
        catch (Exception ex)
        {
            Logger.Warn($"IWindowNative failed ({ex.Message}), using GetActiveWindow");
            // Fallback to GetActiveWindow
            var hwnd = GetActiveWindow();
            Logger.Info($"GetActiveWindow returned: {hwnd}");
            return hwnd;
        }
    }
}

// COM interface for getting window handle from WinRT window
[ComImport]
[Guid("EECDBF0E-BAE9-4CB6-A68E-9598E1CB57CF")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowNative
{
    IntPtr WindowHandle { get; }
}

/// <summary>
/// Initializes WinRT file pickers to work with a specific window handle (desktop interop).
/// </summary>
public static class InitializeWithWindow
{
    public static void Initialize(object picker, IntPtr hwnd)
    {
        if (picker is null || hwnd == IntPtr.Zero)
        {
            Logger.Warn("InitializeWithWindow: picker is null or hwnd is zero");
            return;
        }

        try
        {
            // Use reflection to call IInitializeWithWindow.Initialize
            var type = picker.GetType();
            var iInitializeWithWindow = type.GetInterface("IInitializeWithWindow");

            if (iInitializeWithWindow == null)
            {
                Logger.Warn("IInitializeWithWindow interface not found on picker object");
                return;
            }

            var method = iInitializeWithWindow.GetMethod("Initialize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(IntPtr) }, null);

            if (method == null)
            {
                Logger.Warn("Initialize method not found on IInitializeWithWindow");
                return;
            }

            method.Invoke(picker, new object[] { hwnd });
            Logger.Info($"Successfully initialized file picker with window handle {hwnd}");
        }
        catch (Exception ex)
        {
            Logger.Error($"InitializeWithWindow failed: {ex.Message}");
            throw new InvalidOperationException($"Failed to initialize file picker: {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
}

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
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetActiveWindow();

    public static IntPtr GetWindowHandle(Window window)
    {
        try
        {
            // Use the XAML root to get the window handle
            return window.As<IWindowNative>().WindowHandle;
        }
        catch
        {
            Logger.Warn("GetWindowHandle: Unable to get XAML root window handle");
            // Fallback - return zero and let file picker handle it
            return IntPtr.Zero;
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

// COM interface for initializing with window
[ComImport]
[Guid("b4d7f884-2b8a-43e7-a6d8-74381a50696f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithWindow
{
    void Initialize(IntPtr hwnd);
}

/// <summary>
/// Initializes WinRT file pickers to work with a specific window handle (desktop interop).
/// </summary>
public static class InitializeWithWindow
{
    public static void Initialize(object picker, IntPtr hwnd)
    {
        try
        {
            if (picker is IInitializeWithWindow initWindow)
            {
                initWindow.Initialize(hwnd);
                Logger.Info($"Successfully initialized file picker with window handle {hwnd}");
            }
            else
            {
                Logger.Warn($"Picker does not implement IInitializeWithWindow");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"InitializeWithWindow failed: {ex.Message}. File picker may still work.");
        }
    }
}

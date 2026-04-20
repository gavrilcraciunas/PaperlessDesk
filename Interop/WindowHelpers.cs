using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using PaperlessDesktop.Shared;

namespace PaperlessDesktop.Interop;

/// <summary>
/// Thin wrapper over the official WinRT.Interop APIs for getting native window handles.
/// </summary>
public static class WindowNative
{
    public static IntPtr GetWindowHandle(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Logger.Info($"✓ Got window handle: {hwnd}");
            return hwnd;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get window handle: {ex.Message}");
            return IntPtr.Zero;
        }
    }
}

/// <summary>
/// Thin wrapper over the official WinRT.Interop API for initializing pickers with a window handle.
/// </summary>
public static class InitializeWithWindow
{
    public static void Initialize(object picker, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            Logger.Warn("InitializeWithWindow: hwnd is zero, skipping");
            return;
        }

        try
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            Logger.Info($"✓ Initialized picker with hwnd={hwnd}");
        }
        catch (Exception ex)
        {
            Logger.Error($"InitializeWithWindow failed: {ex.Message}");
        }
    }
}

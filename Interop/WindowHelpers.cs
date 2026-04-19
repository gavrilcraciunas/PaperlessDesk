using System;
using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using WinRT;
using PaperlessDesktop.Shared;

namespace PaperlessDesktop.Interop;

/// <summary>
/// Interop helpers for file picker integration with WinUI windows.
/// Uses advanced techniques to extract native window handle from WinUI Window object.
/// </summary>
public static class WindowNative
{
    // P/Invoke declarations for window enumeration as fallback
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Store the window handle we're looking for (populated before enum)
    private static string? _windowTitleToFind = null;
    private static IntPtr _foundWindowHandle = IntPtr.Zero;

    public static IntPtr GetWindowHandle(Window window)
    {
        try
        {
            Logger.Info("Attempting to get window handle from Window object");

            if (window == null)
            {
                Logger.Warn("Window is null");
                return IntPtr.Zero;
            }

            // Log the window type for debugging
            var windowType = window.GetType();
            Logger.Info($"Window type: {windowType.FullName}");

            // Method 1: Try COM interop
            Logger.Info("Method 1: Trying COM interop approach...");
            var hwndCom = MarshalHelper.GetWindowHandle(window);
            if (hwndCom != IntPtr.Zero)
            {
                Logger.Info($"✓ Got window handle via COM interop: {hwndCom}");
                return hwndCom;
            }

            // Method 2: Try to get the underlying native window through COM object
            Logger.Info("Method 2: Trying to get underlying COM object...");
            try
            {
                // The Window object wraps a COM object, we can get the IUnknown
                IntPtr punk = Marshal.GetIUnknownForObject(window);
                if (punk != IntPtr.Zero)
                {
                    Logger.Info($"Got IUnknown pointer: {punk}");
                    // Try to query for a window handle interface
                    // This is a long shot but worth trying
                    Marshal.Release(punk);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Underlying COM approach failed: {ex.Message}");
            }

            // Method 3: Try getting through XamlRoot
            Logger.Info("Method 3: Trying through XamlRoot...");
            try
            {
                if (window.Content != null && window.Content.XamlRoot != null)
                {
                    var xamlRoot = window.Content.XamlRoot;
                    Logger.Info($"Got XamlRoot: {xamlRoot}");
                    // XamlRoot doesn't directly expose window handle either, but try casting it
                    var xamlRootUnk = Marshal.GetIUnknownForObject(xamlRoot);
                    Logger.Info($"XamlRoot IUnknown: {xamlRootUnk}");
                    if (xamlRootUnk != IntPtr.Zero)
                        Marshal.Release(xamlRootUnk);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"XamlRoot approach failed: {ex.Message}");
            }

            // Method 4: Enumerate all windows and try to find ours by title/class
            Logger.Info("Method 4: Trying window enumeration...");
            try
            {
                // Get the window title to match
                _windowTitleToFind = window.Title ?? "PaperlessDesktop";
                _foundWindowHandle = IntPtr.Zero;

                Logger.Info($"Looking for window with title containing: '{_windowTitleToFind}'");

                // Enumerate all windows
                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        // Get window title
                        int titleLen = GetWindowTextLength(hWnd);
                        if (titleLen > 0)
                        {
                            var title = new System.Text.StringBuilder(titleLen + 1);
                            GetWindowText(hWnd, title, title.Capacity);

                            // Get class name
                            var className = new System.Text.StringBuilder(256);
                            GetClassName(hWnd, className, className.Capacity);

                            var titleStr = title.ToString();
                            var classStr = className.ToString();

                            Logger.Info($"  Window: '{titleStr}' (class: '{classStr}')");

                            // Look for our window - check if title matches and class is Windows.UI.Xaml or similar
                            if (!string.IsNullOrEmpty(titleStr) &&
                                (titleStr.Contains(_windowTitleToFind) || 
                                 titleStr.Contains("PaperlessDesktop") ||
                                 titleStr.Contains("Paperless")))
                            {
                                Logger.Info($"  ✓ Found matching window!");
                                _foundWindowHandle = hWnd;
                                return false; // Stop enumeration
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors during enumeration
                    }

                    return true; // Continue enumeration
                }, IntPtr.Zero);

                if (_foundWindowHandle != IntPtr.Zero)
                {
                    Logger.Info($"✓ Got window handle via enumeration: {_foundWindowHandle}");
                    return _foundWindowHandle;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Window enumeration failed: {ex.Message}");
            }

            Logger.Warn("Could not get valid window handle via any method");
            return IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get window handle: {ex.GetType().Name}: {ex.Message}");
            return IntPtr.Zero;
        }
    }
}

/// <summary>
/// Helper class for marshaling WinRT objects to COM interfaces
/// </summary>
internal static class MarshalHelper
{
    public static IntPtr GetWindowHandle(object window)
    {
        if (window == null)
            return IntPtr.Zero;

        try
        {
            // Method 1: Try casting using the documented IWindowNative interface
            var nativeWindow = window.As<IWindowNative>();
            return nativeWindow.WindowHandle;
        }
        catch (Exception ex1)
        {
            Logger.Warn($"As<IWindowNative>() failed: {ex1.GetType().Name}");

            // Method 2: Try using Marshal.QueryInterface on IUnknown
            try
            {
                IntPtr punk = Marshal.GetIUnknownForObject(window);
                if (punk == IntPtr.Zero)
                {
                    Logger.Warn("Marshal.GetIUnknownForObject returned null");
                    return IntPtr.Zero;
                }

                Guid iWindowNativeGuid = new Guid("EECDBF0E-BAE9-4CB6-A68E-9598E1CB57CF");
                int hresult = Marshal.QueryInterface(punk, ref iWindowNativeGuid, out IntPtr ppv);

                if (hresult == 0 && ppv != IntPtr.Zero)
                {
                    try
                    {
                        // Successfully got IWindowNative interface
                        // WindowHandle property is at vtable offset 3
                        IntPtr[] vtable = new IntPtr[4];
                        Marshal.PtrToStructure(ppv, vtable);

                        if (vtable[3] != IntPtr.Zero)
                        {
                            var getWindowHandleDelegate = Marshal.GetDelegateForFunctionPointer<GetWindowHandleDelegate>(vtable[3]);
                            IntPtr hwnd = IntPtr.Zero;
                            getWindowHandleDelegate(ppv, ref hwnd);
                            return hwnd;
                        }
                    }
                    catch (Exception ex2)
                    {
                        Logger.Warn($"Vtable method invocation failed: {ex2.GetType().Name}: {ex2.Message}");
                    }
                    finally
                    {
                        if (ppv != IntPtr.Zero)
                            Marshal.Release(ppv);
                    }
                }
                else
                {
                    Logger.Warn($"QueryInterface for IWindowNative returned hresult={hresult:X8}");
                }

                if (punk != IntPtr.Zero)
                    Marshal.Release(punk);
            }
            catch (Exception ex3)
            {
                Logger.Warn($"Marshal-based approach failed: {ex3.GetType().Name}: {ex3.Message}");
            }

            return IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetWindowHandleDelegate(IntPtr pThis, ref IntPtr hwnd);
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
        if (picker == null)
        {
            Logger.Warn("InitializeWithWindow: picker is null");
            return;
        }

        if (hwnd == IntPtr.Zero)
        {
            Logger.Warn("InitializeWithWindow: hwnd is zero, skipping initialization");
            return;
        }

        try
        {
            Logger.Info($"InitializeWithWindow: Attempting to initialize with hwnd={hwnd}");

            // Try using Marshal.QueryInterface
            try
            {
                Logger.Info("Attempting Marshal.QueryInterface approach...");
                IntPtr punk = Marshal.GetIUnknownForObject(picker);
                if (punk == IntPtr.Zero)
                {
                    Logger.Warn("Marshal.GetIUnknownForObject returned null");
                    return;
                }

                Guid iid = new Guid("b4d7f884-2b8a-43e7-a6d8-74381a50696f"); // IInitializeWithWindow
                int hr = Marshal.QueryInterface(punk, ref iid, out IntPtr ppv);

                if (hr == 0 && ppv != IntPtr.Zero)
                {
                    try
                    {
                        Logger.Info("Successfully queried IInitializeWithWindow interface");

                        // Call Initialize method (at vtable offset 3)
                        IntPtr[] vtable = new IntPtr[4];
                        Marshal.PtrToStructure(ppv, vtable);

                        if (vtable[3] != IntPtr.Zero)
                        {
                            var initializeDelegate = Marshal.GetDelegateForFunctionPointer<InitializeDelegate>(vtable[3]);
                            initializeDelegate(ppv, hwnd);
                            Logger.Info($"✓ Successfully initialized with window handle {hwnd}");
                            return;
                        }
                        else
                        {
                            Logger.Warn("Initialize method pointer is null");
                        }
                    }
                    finally
                    {
                        if (ppv != IntPtr.Zero)
                            Marshal.Release(ppv);
                    }
                }
                else
                {
                    Logger.Info($"QueryInterface for IInitializeWithWindow returned hresult={hr:X8}");
                    Logger.Info("This WinAppSDK version may not support IInitializeWithWindow on pickers");
                }

                if (punk != IntPtr.Zero)
                    Marshal.Release(punk);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Marshal.QueryInterface approach failed: {ex.GetType().Name}: {ex.Message}");
            }

            Logger.Warn("Could not initialize picker with window - will attempt to use picker without explicit window context");
        }
        catch (Exception ex)
        {
            Logger.Error($"InitializeWithWindow unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void InitializeDelegate(IntPtr pThis, IntPtr hwnd);
}

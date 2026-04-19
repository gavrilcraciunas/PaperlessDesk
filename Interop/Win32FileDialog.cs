using System;
using System.Runtime.InteropServices;
using System.Text;
using PaperlessDesktop.Shared;

namespace PaperlessDesktop.Interop;

/// <summary>
/// Win32-based file dialog implementation as fallback for file pickers.
/// Uses native Windows file open dialogs via P/Invoke when WinRT pickers fail.
/// </summary>
public static class Win32FileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct OpenFileNameA
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public IntPtr lpstrFile;
        public uint nMaxFile;
        public IntPtr lpstrFileTitle;
        public uint nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }

    public const uint OFN_FILEMUSTEXIST = 0x1000;
    public const uint OFN_PATHMUSTEXIST = 0x0800;
    public const uint OFN_ALLOWMULTISELECT = 0x0200;
    public const uint OFN_HIDEREADONLY = 0x0004;

    [DllImport("Comdlg32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool GetOpenFileNameA(ref OpenFileNameA ofn);

    public static string[] OpenFileDialog(IntPtr ownerHandle, string title = "Open File", string filter = "All Files|*.*")
    {
        try
        {
            Logger.Info($"Win32FileDialog.OpenFileDialog: title={title}");

            // Convert filter string from "PDF|*.pdf|All|*.*" format to null-separated format
            string filterStr = filter.Replace("|", "\0") + "\0\0";
            IntPtr filterPtr = Marshal.StringToHGlobalAnsi(filterStr);

            // Allocate buffer for file names (65KB should be enough for multiple files)
            IntPtr fileBuffer = Marshal.AllocHGlobal(65536);

            // Allocate buffer for file title
            IntPtr titleBuffer = Marshal.AllocHGlobal(256);

            try
            {
                var ofn = new OpenFileNameA();
                ofn.lStructSize = (uint)Marshal.SizeOf<OpenFileNameA>();
                ofn.hwndOwner = ownerHandle;
                ofn.lpstrTitle = Marshal.StringToHGlobalAnsi(title);
                ofn.lpstrFilter = filterPtr;
                ofn.nFilterIndex = 1;
                ofn.lpstrFile = fileBuffer;
                ofn.nMaxFile = 65536;
                ofn.lpstrFileTitle = titleBuffer;
                ofn.nMaxFileTitle = 256;
                ofn.Flags = OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY;

                Logger.Info("Calling GetOpenFileNameA...");
                if (GetOpenFileNameA(ref ofn))
                {
                    // Read the file names from the buffer
                    // With OFN_ALLOWMULTISELECT, format is: [dir]\0[file1]\0[file2]\0\0 (if same dir)
                    // or [fullpath1]\0[fullpath2]\0\0 (if different dirs)
                    string filesString = Marshal.PtrToStringAnsi(ofn.lpstrFile);
                    var parts = filesString.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

                    List<string> files = new();

                    if (parts.Length == 1)
                    {
                        // Single file selected - it's a full path
                        files.Add(parts[0]);
                    }
                    else if (parts.Length > 1)
                    {
                        // Check if first part is a directory
                        if (System.IO.Directory.Exists(parts[0]))
                        {
                            // Multiple files in same directory: first part is directory
                            var directory = parts[0];
                            for (int i = 1; i < parts.Length; i++)
                            {
                                files.Add(System.IO.Path.Combine(directory, parts[i]));
                            }
                        }
                        else
                        {
                            // Multiple files with full paths
                            files.AddRange(parts);
                        }
                    }

                    Logger.Info($"✓ Win32FileDialog succeeded, returned {files.Count} file(s)");
                    return files.ToArray();
                }
                else
                {
                    Logger.Info("Win32FileDialog was cancelled by user");
                    return Array.Empty<string>();
                }
            }
            finally
            {
                // Free allocated memory
                if (filterPtr != IntPtr.Zero) Marshal.FreeHGlobal(filterPtr);
                if (fileBuffer != IntPtr.Zero) Marshal.FreeHGlobal(fileBuffer);
                if (titleBuffer != IntPtr.Zero) Marshal.FreeHGlobal(titleBuffer);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Win32FileDialog.OpenFileDialog failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}

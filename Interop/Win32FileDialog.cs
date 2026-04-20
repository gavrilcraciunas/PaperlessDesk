using System;
using System.Runtime.InteropServices;
using PaperlessDesktop.Shared;

namespace PaperlessDesktop.Interop;

/// <summary>
/// Win32-based file dialog implementation as fallback for file pickers.
/// Uses native Windows file open dialogs via P/Invoke when WinRT pickers fail.
/// </summary>
public static class Win32FileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public IntPtr lpstrFile;
        public uint nMaxFile;
        public IntPtr lpstrFileTitle;
        public uint nMaxFileTitle;
        public string? lpstrInitialDir;
        public string lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }

    public const uint OFN_FILEMUSTEXIST   = 0x00001000;
    public const uint OFN_PATHMUSTEXIST   = 0x00000800;
    public const uint OFN_ALLOWMULTISELECT = 0x00000200;
    public const uint OFN_HIDEREADONLY    = 0x00000004;
    public const uint OFN_EXPLORER        = 0x00080000;

    [DllImport("Comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    public static string[] OpenFileDialog(IntPtr ownerHandle, string title = "Open File", string filter = "All Files|*.*")
    {
        try
        {
            Logger.Info($"Win32FileDialog.OpenFileDialog: title={title}");

            // Convert "PDF|*.pdf|All|*.*" to null-separated "PDF\0*.pdf\0All\0*.*\0\0"
            string filterStr = filter.Replace("|", "\0") + "\0";

            const int bufferSize = 65536;
            IntPtr fileBuffer = Marshal.AllocHGlobal(bufferSize * 2); // Unicode = 2 bytes/char
            // Zero the buffer
            for (int i = 0; i < bufferSize * 2; i++)
                Marshal.WriteByte(fileBuffer, i, 0);

            try
            {
                var ofn = new OpenFileName();
                ofn.lStructSize = (uint)Marshal.SizeOf<OpenFileName>();
                ofn.hwndOwner = ownerHandle;
                ofn.lpstrTitle = title;
                ofn.lpstrFilter = filterStr;
                ofn.nFilterIndex = 1;
                ofn.lpstrFile = fileBuffer;
                ofn.nMaxFile = (uint)bufferSize;
                ofn.Flags = OFN_EXPLORER | OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY;

                Logger.Info("Calling GetOpenFileNameW...");
                if (GetOpenFileNameW(ref ofn))
                {
                    // Parse the double-null-terminated Unicode string
                    var parts = new List<string>();
                    int offset = 0;
                    while (offset < bufferSize * 2)
                    {
                        string s = Marshal.PtrToStringUni(fileBuffer + offset) ?? "";
                        if (s.Length == 0) break;
                        parts.Add(s);
                        offset += (s.Length + 1) * 2; // advance past string + null terminator (Unicode)
                    }

                    List<string> files = new();

                    if (parts.Count == 1)
                    {
                        files.Add(parts[0]);
                    }
                    else if (parts.Count > 1)
                    {
                        // First part is directory, rest are filenames
                        var directory = parts[0];
                        for (int i = 1; i < parts.Count; i++)
                            files.Add(System.IO.Path.Combine(directory, parts[i]));
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
                Marshal.FreeHGlobal(fileBuffer);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Win32FileDialog.OpenFileDialog failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}

# PaperlessDesktop - Manual Testing Guide

## Quick Start Testing Guide

### 📍 Location of Test Files
Test files are located at: `C:\Users\Gavril\PaperlessDesktop\TestFiles\`

Currently available:
- `test.txt` (42 bytes) - Simple text file
- `test1.png` (70 bytes) - Minimal PNG image

### 🧪 Test Scenario 1: Basic File Operations

**Objective**: Verify file picker and file list management

**Steps**:
1. Open PaperlessDesktop application
2. Click "Add Files" button
3. Navigate to `TestFiles` directory
4. Select `test1.png` file
5. Verify file appears in the list
6. Check that file count displays "1 file"

**Expected Results**:
- ✅ File picker opens
- ✅ File appears in list
- ✅ File name displayed correctly
- ✅ File type identified as image
- ✅ Count updates to "1 file"

**What's being tested**:
- File picker interop (COM integration)
- File list display (XAML binding)
- File type detection
- File count calculation

---

### 🧪 Test Scenario 2: File Operations (Add Multiple, Remove, Clear)

**Objective**: Test file management operations

**Steps**:
1. Click "Add Files" multiple times and add different files
2. Select a file in the list and click "Remove" or use context menu
3. Verify file removed and count updated
4. Click "Clear All" to remove all files
5. Verify list is empty and count shows "0 files"

**Expected Results**:
- ✅ Multiple files can be added
- ✅ Files display correctly
- ✅ Remove operation works
- ✅ Clear operation works
- ✅ Count always accurate

**What's being tested**:
- ObservableCollection binding
- File removal operations
- Clear all functionality
- Count calculation

---

### 🧪 Test Scenario 3: Status Messages & Progress

**Objective**: Verify UI feedback mechanisms

**Steps**:
1. Look at the status bar at the bottom
2. Add files and watch status change
3. During any longer operation, observe progress bar
4. After operation, verify status message updates

**Expected Results**:
- ✅ Status message updates appropriately
- ✅ Progress bar appears for long operations
- ✅ Progress bar shows percentage
- ✅ Messages are clear and informative

**What's being tested**:
- MainViewModel status property binding
- Progress bar functionality
- SetStatus() method in ViewModel
- Progress reporting from services

---

### 🧪 Test Scenario 4: Logging Verification

**Objective**: Confirm logging infrastructure is working

**Steps**:
1. Perform any operation (add file, process, etc.)
2. Open file explorer
3. Navigate to: `%APPDATA%\PaperlessDesktop\logs\`
4. Look for `app_YYYY-MM-DD.log` file
5. Open with text editor and examine contents

**Expected Results**:
- ✅ Log directory created automatically
- ✅ Log file created on first operation
- ✅ Log contains timestamped entries
- ✅ Operations logged with INFO level
- ✅ Errors logged with ERROR level

**Log Entry Format**:
```
[2026-04-18 12:34:56] [INFO] Starting operation XYZ
[2026-04-18 12:34:57] [ERROR] Operation failed: reason
```

**What's being tested**:
- Logger.cs file creation
- Daily log rotation
- Log level tracking
- Error logging with exceptions

---

### 🧪 Test Scenario 5: Error Handling

**Objective**: Verify error handling and user feedback

**Steps**:

#### Error Test 5a: Invalid File Operation
1. Add a valid file to the list
2. Try to perform operation (if available)
3. If error occurs, verify error message displays
4. Check that application remains stable
5. Verify error logged to log file

#### Error Test 5b: Tool Not Found (if applicable)
1. Try to perform OCR or DOCX conversion
2. If tool not installed, verify error message:
   "Tool 'ocrmypdf' not found..."
3. Message should suggest installing tool
4. Verify app remains functional

**Expected Results**:
- ✅ Error dialog appears with clear message
- ✅ Error includes helpful information
- ✅ User can dismiss and continue
- ✅ Error logged for debugging
- ✅ App remains stable

**What's being tested**:
- Try-catch error handling
- User-friendly error messages
- Error logging infrastructure
- App stability under errors

---

### 🧪 Test Scenario 6: Recent Files & Persistence

**Objective**: Verify settings persistence and recent files

**Steps**:
1. Add a file to the list
2. Note the status bar shows 1 file
3. Close the application completely
4. Reopen PaperlessDesktop
5. Verify the file still appears in the list

**Expected Results**:
- ✅ Recent files automatically loaded
- ✅ File list preserved across sessions
- ✅ File count correct
- ✅ All file properties preserved

**What's being tested**:
- AppSettings.cs persistence
- Recent files storage
- Deserialization on app load
- LoadRecentFiles() in MainViewModel

---

### 🧪 Test Scenario 7: File Selection & Reordering

**Objective**: Test file selection and list manipulation

**Steps**:
1. Add 3+ files to the list
2. Click on a file to select it
3. Use "Move Up" and "Move Down" buttons to reorder
4. Observe order changes in list
5. Try selecting multiple files (Ctrl+Click or shift+click)

**Expected Results**:
- ✅ Single file can be selected
- ✅ Multiple files can be selected
- ✅ Move Up/Down buttons work
- ✅ Order updates in real-time
- ✅ Selection state is clear

**What's being tested**:
- File selection binding
- MoveUp() and MoveDown() methods
- ObservableCollection.Move()
- Selection visualization

---

### 📊 Logging - Where to Find Logs

**Log Directory**: `C:\Users\Gavril\AppData\Roaming\PaperlessDesktop\logs\`

**File Naming**: `app_YYYY-MM-DD.log` (daily rotation)
- Example: `app_2026-04-18.log`

**Log Levels**:
```
[INFO]   - Normal operations completed successfully
[WARN]   - Warning conditions (unusual but handled)
[ERROR]  - Error conditions (operations failed)
```

**Example Log Content**:
```
[2026-04-18 14:22:35] [INFO] Application started
[2026-04-18 14:22:45] [INFO] Loading recent files...
[2026-04-18 14:22:50] [INFO] File added: C:\Users\Gavril\test.pdf
[2026-04-18 14:23:10] [INFO] Starting merge operation
[2026-04-18 14:23:12] [INFO] Merge completed successfully
[2026-04-18 14:23:15] [ERROR] Failed to access tool: ocrmypdf
```

---

## 🎯 Advanced Testing (When Tools Available)

### If ocrmypdf is installed (OCR):
1. Add a scanned PDF or image-based PDF
2. Click "OCR PDF" (if available in UI)
3. Select output location
4. Wait for completion
5. Verify searchable PDF created
6. Check progress bar updates

### If python is installed (PDF to DOCX):
1. Add a PDF with text content
2. Click "PDF to DOCX" (if available in UI)
3. Select output location
4. Wait for completion
5. Verify Word document created
6. Open and verify content

### If Ghostscript is available (Compression):
1. Add a large PDF file
2. Click "Compress PDF" (if available in UI)
3. Note original file size
4. Compare compressed file size
5. Verify compression successful
6. Check logs for compression ratio

---

## 🔍 Debugging Tips

### In Visual Studio:
1. Set breakpoints in MainViewModel.cs or Services
2. Debug → Attach to Process → PaperlessDesktop.exe
3. Trigger operations to hit breakpoints
4. Watch variable states in Locals/Watch windows
5. Check Debug Output (View → Output) for Logger messages

### Check Logs in Real-Time:
```powershell
# PowerShell command to monitor logs
Get-Content "$env:APPDATA\PaperlessDesktop\logs\app_$(Get-Date -Format yyyy-MM-dd).log" -Wait
```

### Performance Monitoring:
1. Open Task Manager
2. Find PaperlessDesktop process
3. Monitor Memory and CPU during operations
4. Verify memory doesn't continuously increase (no leaks)
5. Note CPU usage peaks during operations

---

## ✅ Quick Verification Checklist

**Before Starting**: 
- [ ] Application launches without errors
- [ ] Main window appears correctly
- [ ] Status bar shows "Ready"
- [ ] File list is empty initially

**File Operations**:
- [ ] File picker opens on "Add Files" click
- [ ] Can select files from TestFiles directory
- [ ] Files display in list after adding
- [ ] File count updates correctly
- [ ] Can remove individual files
- [ ] Clear All removes all files

**UI Feedback**:
- [ ] Status messages appear and update
- [ ] Progress bar visible during operations (if applicable)
- [ ] Error messages clear and helpful
- [ ] No crashes or unhandled exceptions

**Logging**:
- [ ] Log directory created: `%APPDATA%\PaperlessDesktop\logs\`
- [ ] Log file created after first operation
- [ ] Log contains timestamped entries
- [ ] Operations appear with INFO level
- [ ] Errors appear with ERROR level

**Stability**:
- [ ] App remains responsive after operations
- [ ] Can continue using app after errors
- [ ] No memory warnings or performance issues
- [ ] Recent files persist across app restarts

---

## 📞 Troubleshooting

### App Won't Start
- [ ] Check Windows App Runtime is installed
- [ ] Verify .NET 8 SDK installed
- [ ] Check for Windows updates

### File Picker Won't Open
- [ ] Verify Windows App Runtime installed
- [ ] Check Interop/WindowHelpers.cs is present
- [ ] Try restarting application

### No Logs Created
- [ ] Perform an operation to trigger logging
- [ ] Check path: `%APPDATA%\PaperlessDesktop\logs\`
- [ ] Verify Shared/Logger.cs is compiled
- [ ] Check file permissions on AppData folder

### Tools Not Found
- [ ] Verify tools installed: ocrmypdf, python, Ghostscript
- [ ] Check PATH environment variable
- [ ] Try running `ocrmypdf --version` in PowerShell

---

## 🎓 What Each Component Tests

| Component | What It Tests | Test Scenario |
|-----------|---------------|---------------|
| MainWindow.xaml.cs | UI event handling, file picker | Scenario 1, 6 |
| MainViewModel.cs | Property binding, state management | Scenario 3, 4, 7 |
| FileItemViewModel.cs | File item display | Scenario 1, 7 |
| PdfCoreService.cs | PDF operations | Scenarios 5, 8, 9 |
| PdfConvertService.cs | Image/format conversion | Scenarios 8, 9 |
| ToolManager.cs | External tool execution | Advanced tests |
| Logger.cs | Logging functionality | Scenario 4 |
| WindowHelpers.cs | File picker interop | Scenario 1 |

---

**Prepared**: 2026-04-18  
**For**: PaperlessDesktop Functional Testing  
**Status**: Ready for Testing ✅

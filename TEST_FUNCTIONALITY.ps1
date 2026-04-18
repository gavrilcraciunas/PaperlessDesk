#!/usr/bin/env pwsh
<#
.SYNOPSIS
    PaperlessDesktop Functionality Test Script
.DESCRIPTION
    Automated tests for core functionality verification
#>

$ErrorActionPreference = "Continue"
$testDir = "C:\Users\Gavril\PaperlessDesktop\TestFiles"
$logDir = "$env:APPDATA\PaperlessDesktop\logs"

Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  PaperlessDesktop - Functionality Test Suite             ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Test 1: Application Running
Write-Host "TEST 1: Application Verification" -ForegroundColor Yellow
$appProcess = Get-Process | Where-Object {$_.ProcessName -eq "PaperlessDesktop"}
if ($appProcess) {
    Write-Host "  ✅ App is running (PID: $($appProcess[0].Id))" -ForegroundColor Green
    Write-Host "     Memory: $([math]::Round($appProcess[0].WorkingSet/1MB, 2)) MB"
} else {
    Write-Host "  ❌ App is NOT running" -ForegroundColor Red
}
Write-Host ""

# Test 2: Logging Infrastructure
Write-Host "TEST 2: Logging Infrastructure" -ForegroundColor Yellow
if (Test-Path $logDir) {
    Write-Host "  ✅ Log directory exists: $logDir" -ForegroundColor Green
    $logs = Get-ChildItem $logDir -File | Sort-Object LastWriteTime -Descending
    if ($logs) {
        Write-Host "     Found $(@($logs).Count) log file(s)"
        foreach ($log in $logs | Select-Object -First 3) {
            Write-Host "     📄 $($log.Name) - $(Get-Content $log -Tail 1)"
        }
    } else {
        Write-Host "  ⚠️  Log directory exists but is empty (operations haven't created logs yet)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ⚠️  Log directory not yet created - logs will be created on first operation" -ForegroundColor Yellow
    Write-Host "     Expected path: $logDir" -ForegroundColor Gray
}
Write-Host ""

# Test 3: Test Files
Write-Host "TEST 3: Test Files" -ForegroundColor Yellow
if (Test-Path $testDir) {
    $files = Get-ChildItem $testDir -File
    if ($files) {
        Write-Host "  ✅ Test files directory exists with $(@($files).Count) file(s):" -ForegroundColor Green
        foreach ($file in $files) {
            Write-Host "     📄 $($file.Name) - $($file.Length) bytes"
        }
    } else {
        Write-Host "  ⚠️  Test directory exists but is empty" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ⚠️  Test files directory not found at: $testDir" -ForegroundColor Yellow
}
Write-Host ""

# Test 4: Configuration Verification
Write-Host "TEST 4: Configuration & Dependencies" -ForegroundColor Yellow
$csproj = "C:\Users\Gavril\PaperlessDesktop\PaperlessDesktop.csproj"
if (Test-Path $csproj) {
    Write-Host "  ✅ Project file found" -ForegroundColor Green

    # Check for key package references
    $content = Get-Content $csproj -Raw
    $packages = @(
        "CommunityToolkit.Mvvm"
        "PdfPig"
        "Microsoft.WindowsAppSDK"
        "DocumentFormat.OpenXml"
        "Ghostscript.NET"
        "Microsoft.Extensions.DependencyInjection"
    )

    foreach ($pkg in $packages) {
        if ($content -match "PackageReference.*$pkg") {
            Write-Host "     ✅ $pkg" -ForegroundColor Green
        } else {
            Write-Host "     ❌ $pkg NOT FOUND" -ForegroundColor Red
        }
    }
} else {
    Write-Host "  ❌ Project file not found" -ForegroundColor Red
}
Write-Host ""

# Test 5: Code Files Verification
Write-Host "TEST 5: Source Code Structure" -ForegroundColor Yellow
$criticalFiles = @(
    "GlobalUsings.cs"
    "Shared\Logger.cs"
    "Services\ToolManager.cs"
    "Services\PdfCoreService.cs"
    "Services\PdfConvertService.cs"
    "Interop\WindowHelpers.cs"
    "ViewModels\MainViewModel.cs"
    "MainWindow.xaml.cs"
)

$basePath = "C:\Users\Gavril\PaperlessDesktop"
foreach ($file in $criticalFiles) {
    $fullPath = Join-Path $basePath $file
    if (Test-Path $fullPath) {
        Write-Host "  ✅ $file" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $file NOT FOUND" -ForegroundColor Red
    }
}
Write-Host ""

# Test 6: Build Status
Write-Host "TEST 6: Recent Build Status" -ForegroundColor Yellow
$binPath = Join-Path $basePath "bin\Debug\net8.0-windows10.0.19041.0"
if (Test-Path $binPath) {
    Write-Host "  ✅ Build output exists at: $binPath" -ForegroundColor Green
    $exe = Get-ChildItem $binPath -Filter "*.exe" | Select-Object -First 1
    if ($exe) {
        Write-Host "     📦 Executable: $($exe.Name)" -ForegroundColor Green
        Write-Host "     📅 Built: $(Get-Date -Date $exe.CreationTime -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
    }
} else {
    Write-Host "  ⚠️  Build output directory not found - rebuild may be needed" -ForegroundColor Yellow
}
Write-Host ""

# Test 7: Tool Availability Checks
Write-Host "TEST 7: External Tool Availability" -ForegroundColor Yellow
$tools = @(
    @{ Name = "ocrmypdf"; Cmd = "ocrmypdf --version" }
    @{ Name = "python"; Cmd = "python --version" }
    @{ Name = "Ghostscript"; Cmd = "gswin64c --version" }
)

foreach ($tool in $tools) {
    try {
        $result = & cmd /c $tool.Cmd 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✅ $($tool.Name) - Available" -ForegroundColor Green
        } else {
            Write-Host "  ⚠️  $($tool.Name) - Not accessible" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  ❌ $($tool.Name) - Not installed" -ForegroundColor Red
    }
}
Write-Host ""

# Summary
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  TEST SUITE COMPLETE                                       ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "📋 NEXT STEPS:" -ForegroundColor Yellow
Write-Host "  1. Interact with the GUI to test actual functionality"
Write-Host "  2. Try adding files using the file picker"
Write-Host "  3. Test PDF operations (merge, split, etc.)"
Write-Host "  4. Check the log file for operation details"
Write-Host "  5. Test error scenarios (invalid files, missing tools)"
Write-Host ""
Write-Host "📊 MANUAL TESTING CHECKLIST:" -ForegroundColor Yellow
Write-Host "  [ ] File picker opens correctly"
Write-Host "  [ ] Files display in list"
Write-Host "  [ ] File count updates"
Write-Host "  [ ] Status messages appear"
Write-Host "  [ ] Progress bar works for long operations"
Write-Host "  [ ] Error dialogs show meaningful messages"
Write-Host "  [ ] Logs are written to: $logDir"
Write-Host ""

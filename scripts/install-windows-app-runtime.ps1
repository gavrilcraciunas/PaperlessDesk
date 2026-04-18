<#
Small helper script to ensure the Windows App Runtime (Windows App SDK runtime) is installed.

Behavior:
- Checks whether Windows App Runtime packages are already installed (Get-AppxPackage Microsoft.WindowsAppRuntime*).
- If present, reports and exits.
- If winget is available, offers to install the runtime via winget (recommended).
- Otherwise opens the official Windows App SDK 1.4 "Get started" docs page so you can download the redistributable manually.

Usage (pwsh.exe):
  .\scripts\install-windows-app-runtime.ps1

Note: Installing the runtime may require elevation (run as Administrator).
#>

param(
    [switch]$Force,
    [switch]$AutoAccept
)

function Write-Info($m){ Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Write-Warn($m){ Write-Host "[WARN] $m" -ForegroundColor Yellow }
function Write-Err($m){ Write-Host "[ERROR] $m" -ForegroundColor Red }

try {
    Write-Info "Checking for existing Windows App Runtime installation..."
    $installed = Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime*' -AllUsers -ErrorAction SilentlyContinue
    if ($installed) {
        Write-Info "Windows App Runtime appears to be installed. Details:"
        $installed | Select-Object Name, PackageFullName, Version | Format-Table
        exit 0
    }

    Write-Info "Windows App Runtime not found or not installed."

    # Try winget if available
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        Write-Info "winget detected. Will attempt to install via winget."
        if (-not $AutoAccept) {
            $yn = Read-Host "Install Windows App Runtime using winget now? (Y/N)"
            if ($yn.Trim().ToUpperInvariant() -ne 'Y') { Write-Warn "Aborted by user."; exit 2 }
        }

        # Try to install the Windows App Runtime package. Use the package ID if available; otherwise install the WindowsAppRuntime package.
        # winget package ids may change; this tries a few common ids.
        $candidates = @(
            'Microsoft.WindowsAppRuntime',
            'Microsoft.WindowsAppRuntime.1.4',
            'Microsoft.WindowsAppRuntime.1.3',
            'Microsoft.WindowsAppRuntime.1.2'
        )

        $installedViaWinget = $false
        foreach ($id in $candidates) {
            Write-Info "Trying winget install --id $id -e --silent"
            try {
                # Accept agreements if requested
                $args = @('install','--id',$id,'-e')
                if ($AutoAccept) { $args += @('--accept-package-agreements','--accept-source-agreements','--silent') }
                else { $args += @('--accept-package-agreements','--accept-source-agreements') }
                $proc = Start-Process -FilePath winget -ArgumentList $args -NoNewWindow -Wait -PassThru -ErrorAction Stop
                if ($proc.ExitCode -eq 0) { $installedViaWinget = $true; break }
            } catch {
                Write-Warn "winget install for $id failed or not available. Trying next candidate..."
            }
        }

        if ($installedViaWinget) {
            Write-Info "winget reported successful install. Re-checking installation..."
            Start-Sleep -Seconds 2
            $installed = Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime*' -AllUsers -ErrorAction SilentlyContinue
            if ($installed) { Write-Info "Windows App Runtime installed successfully."; exit 0 }
            else { Write-Warn "winget finished but package not found. You may need to reboot or install manually."; exit 3 }
        }

        Write-Warn "winget installation attempts failed. Falling back to opening the official download page in your browser."
    }
    else {
        Write-Warn "winget not found on PATH. Falling back to opening the official download page in your browser."
    }

    # Open official docs page where the runtime redistributable downloads are documented
    $docsUrl = 'https://learn.microsoft.com/windows/apps/windows-app-sdk/get-started/1.4/'
    Write-Info "Opening the Windows App SDK 1.4 documentation page in your browser: $docsUrl"
    Start-Process $docsUrl

    Write-Info "On the docs page look for the 'Download the Windows App Runtime (Redistributable)' link and install the x64 runtime."
    Write-Info "If you need an unattended/scripted install for offline or enterprise distribution, use the redistributable .msix/.msixbundle or the installer provided by Microsoft and run it elevated."

    exit 0
}
catch {
    Write-Err "Unexpected error: $_"
    exit 10
}

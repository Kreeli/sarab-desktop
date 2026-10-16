param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent), [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Requires Windows 10 1809+ x64 or Windows 11 x64. Linux and macOS are not supported.' }
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ($architecture -ne 'X64') { throw 'Requires an x64 Windows PC. ARM64 and 32-bit Windows are not supported by the bundled GNSS driver.' }
    $windows = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    if ([int]$windows.CurrentBuildNumber -lt 17763) { throw 'Requires Windows 10 version 1809 (build 17763) or later.' }
    $framework = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
    if (-not $framework -or [int]$framework.Release -lt 461808) { throw 'Enable or install Microsoft .NET Framework 4.7.2 or later, then run Start.cmd again. No developer SDK is required for a release package.' }
    $ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
    $distribution = $ProjectRoot
    if (-not (Test-Path -LiteralPath (Join-Path $distribution 'SarabDesktop.exe') -PathType Leaf)) {
        $distribution = Join-Path $ProjectRoot 'dist'
        if (-not (Test-Path -LiteralPath (Join-Path $distribution 'SarabDesktop.exe') -PathType Leaf)) {
            if ($CheckOnly) { throw 'No built package found. Run Start.cmd to build from source, or download and extract the complete portable release.' }
            $build = Join-Path $ProjectRoot 'Build.ps1'
            if (-not (Test-Path -LiteralPath $build -PathType Leaf)) { throw 'Application is missing. Extract the complete release archive instead of running from inside the ZIP.' }
            Write-Host 'Building from source. Visual Studio C++ Build Tools and Windows SDK are required. The pinned upstream dependency is downloaded only if it is not already cached.'
            & $build
        }
    }
    & (Join-Path $PSScriptRoot 'Test-Distribution.ps1') -DistributionRoot $distribution
    if ($CheckOnly) { Write-Output 'Prerequisites and package checks passed.'; exit 0 }
    Start-Process -FilePath (Join-Path $distribution 'SarabDesktop.exe') -WorkingDirectory $distribution | Out-Null
    exit 0
} catch {
    Write-Host ('Sarab Desktop could not start: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}

param([string]$Iscc, [string]$Compiler, [string]$VisualStudio, [string]$ProbeBinary, [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent), [switch]$SkipBuild, [switch]$PortableOnly)
$ErrorActionPreference = 'Stop'
& (Join-Path $ProjectRoot 'scripts\Test-InstallerPolicy.ps1') -ProjectRoot $ProjectRoot
if (-not $PortableOnly -and -not $Iscc) {
    $candidates = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'), (Join-Path $ProjectRoot 'tools\InnoSetup\ISCC.exe'))
    $Iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $PortableOnly -and (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc))) { throw 'Inno Setup compiler not found. Specify -Iscc, or use -PortableOnly to create the portable ZIP without Inno Setup.' }
$buildArgs = @{ }
if ($Compiler) { $buildArgs.Compiler = $Compiler }
if ($VisualStudio) { $buildArgs.VisualStudio = $VisualStudio }
if ($ProbeBinary) { $buildArgs.ProbeBinary = $ProbeBinary }
if (-not $SkipBuild) { & (Join-Path $ProjectRoot 'Build.ps1') @buildArgs }
if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'dist\SarabDesktop.exe'))) { throw 'Fresh build output is missing; packaging stopped.' }
& (Join-Path $ProjectRoot 'scripts\Test-Distribution.ps1') -DistributionRoot (Join-Path $ProjectRoot 'dist')
$releaseRoot = Join-Path $ProjectRoot 'releases'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
if (-not $PortableOnly) {
    $bundle = [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'LICENSE')) + "`r`n`r`n--- ORIGINAL SARAB v1.0.0 ---`r`n`r`n" + [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'third_party\sarab\LICENSE'))
    [System.IO.File]::WriteAllText((Join-Path $ProjectRoot 'installer\LICENSE-BUNDLE.txt'), $bundle, (New-Object System.Text.UTF8Encoding $true))
    & $Iscc ('/DProjectRoot=' + $ProjectRoot) (Join-Path $ProjectRoot 'installer\SarabDesktop.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
Compress-Archive -Path (Join-Path $ProjectRoot 'dist\*') -DestinationPath (Join-Path $releaseRoot 'SarabDesktop-1.1.0-Portable-x64.zip') -Force
$packageFiles = @((Join-Path $releaseRoot 'SarabDesktop-1.1.0-Portable-x64.zip'))
if (-not $PortableOnly) { $packageFiles += Join-Path $releaseRoot 'SarabDesktop-1.1.0-Setup-x64.exe' }
$hashes = $packageFiles | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($_.Path) }
[System.IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'), $hashes, (New-Object System.Text.UTF8Encoding $false))
Write-Output "Packages ready: $releaseRoot"

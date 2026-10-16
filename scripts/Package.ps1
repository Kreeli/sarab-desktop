param([string]$Iscc, [string]$Compiler, [string]$VisualStudio, [string]$ProbeBinary, [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
if (-not $Iscc) {
    $candidates = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'), (Join-Path $ProjectRoot 'tools\InnoSetup\ISCC.exe'))
    $Iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) { throw 'Inno Setup compiler not found. Specify -Iscc.' }
$buildArgs = @{ }
if ($Compiler) { $buildArgs.Compiler = $Compiler }
if ($VisualStudio) { $buildArgs.VisualStudio = $VisualStudio }
if ($ProbeBinary) { $buildArgs.ProbeBinary = $ProbeBinary }
& (Join-Path $ProjectRoot 'Build.ps1') @buildArgs
if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'dist\SarabDesktop.exe'))) { throw 'Fresh build output is missing; packaging stopped.' }
$releaseRoot = Join-Path $ProjectRoot 'releases'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$bundle = [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'LICENSE')) + "`r`n`r`n--- ORIGINAL SARAB v1.0.0 ---`r`n`r`n" + [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'third_party\sarab\LICENSE'))
[System.IO.File]::WriteAllText((Join-Path $ProjectRoot 'installer\LICENSE-BUNDLE.txt'), $bundle, (New-Object System.Text.UTF8Encoding $true))
& $Iscc ('/DProjectRoot=' + $ProjectRoot) (Join-Path $ProjectRoot 'installer\SarabDesktop.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Compress-Archive -Path (Join-Path $ProjectRoot 'dist\*') -DestinationPath (Join-Path $releaseRoot 'SarabDesktop-1.1.0-Portable-x64.zip') -Force
$hashes = Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object Extension -in @('.exe','.zip') | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($_.Path) }
[System.IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'), $hashes, (New-Object System.Text.UTF8Encoding $false))
Write-Output "Packages ready: $releaseRoot"

param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent), [string]$VisualStudio, [string]$WindowsSdkRoot)
$ErrorActionPreference = 'Stop'
if (-not $VisualStudio) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $VisualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}
$vcvars = Join-Path $VisualStudio 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path -LiteralPath $vcvars)) { throw 'Install Visual Studio C++ Build Tools and Windows SDK.' }
if (-not $WindowsSdkRoot) {
    $sdkCandidates = @($env:WindowsSdkDir, (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'), (Join-Path ([System.IO.Path]::GetPathRoot($VisualStudio)) 'Windows Kits\10'))
    $WindowsSdkRoot = $sdkCandidates | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'Include')) } | Select-Object -First 1
}
if (-not $WindowsSdkRoot) { throw 'Windows SDK not found. Specify -WindowsSdkRoot.' }
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $WindowsSdkRoot 'Include') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'cppwinrt\winrt\Windows.Foundation.h') } | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
if (-not $sdkVersion) { throw 'Windows SDK C++/WinRT headers not found.' }
$buildRoot = Join-Path $ProjectRoot 'build\probe'
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$source = Join-Path $ProjectRoot 'src\location-probe.cpp'
$batch = Join-Path $buildRoot 'compile.cmd'
$sdkInclude = Join-Path $WindowsSdkRoot "Include\$sdkVersion"
$sdkLib = Join-Path $WindowsSdkRoot "Lib\$sdkVersion"
$text = "@echo off`r`ncall `"$vcvars`" > vcvars.log 2>&1`r`nif errorlevel 1 exit /b 1`r`nset `"INCLUDE=$sdkInclude\um;$sdkInclude\shared;$sdkInclude\ucrt;$sdkInclude\winrt;$sdkInclude\cppwinrt;%INCLUDE%`"`r`nset `"LIB=$sdkLib\um\x64;$sdkLib\ucrt\x64;%LIB%`"`r`ncl /nologo /EHsc /std:c++20 /O2 /MT /utf-8 /W3 `"$source`" /Fe:location-probe.exe /link windowsapp.lib > compile.log 2>&1`r`nexit /b %errorlevel%`r`n"
[System.IO.File]::WriteAllText($batch, $text, [System.Text.Encoding]::Default)
Push-Location $buildRoot
try { & $env:COMSPEC /d /c $batch; if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $buildRoot 'compile.log'); throw 'Native location probe build failed.' } } finally { Pop-Location }
Write-Output (Join-Path $buildRoot 'location-probe.exe')

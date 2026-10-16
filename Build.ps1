param([string]$Compiler, [string]$VisualStudio, [string]$ProbeBinary)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
& (Join-Path $projectRoot 'scripts\Test-Localization.ps1') -ProjectRoot $projectRoot
& (Join-Path $projectRoot 'scripts\Build-Help.ps1') -ProjectRoot $projectRoot
if (-not $VisualStudio) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio Build Tools with the C++ workload.' }
    $VisualStudio = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
}
if (-not $Compiler) { $Compiler = Join-Path $VisualStudio 'MSBuild\Current\Bin\Roslyn\csc.exe' }
if (-not (Test-Path -LiteralPath $Compiler)) { throw 'Roslyn C# compiler not found.' }
$vendor = & (Join-Path $projectRoot 'scripts\Fetch-Sarab.ps1') -ProjectRoot $projectRoot
if (-not $ProbeBinary) { $ProbeBinary = & (Join-Path $projectRoot 'scripts\Build-Probe.ps1') -ProjectRoot $projectRoot -VisualStudio $VisualStudio }
$buildRoot = Join-Path $projectRoot 'build'
$generatedRoot = Join-Path $buildRoot 'generated'
$outputRoot = Join-Path $buildRoot 'staging'
if (Test-Path -LiteralPath $outputRoot) {
    $preserved = Join-Path $buildRoot ('staging-preserved-' + [DateTime]::Now.ToString('yyyyMMddHHmmssfff'))
    Move-Item -LiteralPath $outputRoot -Destination $preserved
}
foreach ($dir in @($outputRoot, (Join-Path $outputRoot 'assets'), (Join-Path $outputRoot 'backend'), (Join-Path $outputRoot 'driver'), (Join-Path $outputRoot 'docs'), $generatedRoot)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$probeHash = (Get-FileHash -LiteralPath $ProbeBinary -Algorithm SHA256).Hash
$generatedHelper = Join-Path $generatedRoot 'HelperBuildTimePins.cs'
$generatedMain = Join-Path $generatedRoot 'MainBuildTimePins.cs'
[System.IO.File]::WriteAllText($generatedHelper, ('namespace SarabDesktop { public static class BuildTimePins { public const string ProbeHash = "' + $probeHash + '"; public const string HelperHash = ""; } }'))
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$referenceNames = @('System.dll','System.Core.dll','System.Xaml.dll','System.Xml.dll','System.Web.dll','System.Web.Extensions.dll','System.Security.dll','System.Management.dll','System.Net.Http.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\WindowsBase.dll')
$references = @($referenceNames | ForEach-Object { '/reference:' + (Join-Path $framework $_) })
$iconPath = Join-Path $projectRoot 'assets\sarab.ico'
$musicDirectory = Join-Path $projectRoot 'assets\notice-music'
$certificateBackground = Join-Path $projectRoot 'assets\notice-certificate-bg.jpg'
if (-not (Test-Path -LiteralPath $certificateBackground -PathType Leaf)) { throw ('Missing certificate background image: ' + $certificateBackground) }
$certificateBackgroundResource = '/resource:' + $certificateBackground + ',SarabDesktop.NoticeCertificateBackground.jpg'
$musicFiles = @(Get-ChildItem -LiteralPath $musicDirectory -File -Filter 'track-*.wav' -ErrorAction SilentlyContinue | Sort-Object Name)
if ($musicFiles.Count -ne 20) { throw ('Expected 20 embedded notice music tracks in ' + $musicDirectory + '; found ' + $musicFiles.Count + '.') }
$expectedMusicNames = @(1..20 | ForEach-Object { 'track-' + $_.ToString('D2', [Globalization.CultureInfo]::InvariantCulture) + '.wav' })
if ((@($musicFiles.Name) -join '|') -cne ($expectedMusicNames -join '|')) { throw 'Embedded notice music filenames must be track-01.wav through track-20.wav.' }
$musicResources = @($musicFiles | ForEach-Object { '/resource:' + $_.FullName + ',SarabDesktop.NoticeMusic.' + [System.IO.Path]::GetFileNameWithoutExtension($_.Name) })
$common = @('/nologo','/target:winexe','/platform:x64','/langversion:latest','/optimize+','/utf8output','/deterministic+',('/win32icon:' + $iconPath),('/resource:' + (Join-Path $projectRoot 'locales\en-US.json') + ',SarabDesktop.en.json'),('/resource:' + (Join-Path $projectRoot 'locales\ja-JP.json') + ',SarabDesktop.ja.json'),('/resource:' + (Join-Path $projectRoot 'locales\zh-TW.json') + ',SarabDesktop.zh-TW.json')) + $references
$mainSources = @('Core.cs','Services.cs','Installation.cs','Localization.cs','DesktopController.cs','Program.cs','AssemblyInfo.cs') | ForEach-Object { Join-Path $projectRoot "src\$_" }
$helperSources = @('Core.cs','Services.cs','Installation.cs','Localization.cs','Helper.cs','AssemblyInfo.cs') | ForEach-Object { Join-Path $projectRoot "src\$_" }
& $Compiler @common ('/out:' + (Join-Path $outputRoot 'SarabDesktop.Helper.exe')) ('/win32manifest:' + (Join-Path $projectRoot 'src\helper.manifest')) @helperSources $generatedHelper
if ($LASTEXITCODE -ne 0) { throw 'Administrator helper build failed.' }
$helperHash = (Get-FileHash -LiteralPath (Join-Path $outputRoot 'SarabDesktop.Helper.exe') -Algorithm SHA256).Hash
[System.IO.File]::WriteAllText($generatedMain, ('namespace SarabDesktop { public static class BuildTimePins { public const string ProbeHash = "' + $probeHash + '"; public const string HelperHash = "' + $helperHash + '"; } }'))
& $Compiler @common $musicResources $certificateBackgroundResource ('/out:' + (Join-Path $outputRoot 'SarabDesktop.exe')) ('/win32manifest:' + (Join-Path $projectRoot 'src\app.manifest')) ('/resource:' + (Join-Path $projectRoot 'src\MainWindow.xaml') + ',SarabDesktop.MainWindow.xaml') @mainSources (Join-Path $projectRoot 'src\Appearance.cs') $generatedMain (Join-Path $projectRoot 'tests\SelfTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $outputRoot 'assets\sarab.ico') -Force
Copy-Item -LiteralPath (Join-Path $vendor 'sarab.exe') -Destination (Join-Path $outputRoot 'backend\sarab.exe') -Force
Copy-Item -LiteralPath $ProbeBinary -Destination (Join-Path $outputRoot 'backend\location-probe.exe') -Force
Copy-Item -LiteralPath (Join-Path $vendor 'LICENSE') -Destination (Join-Path $outputRoot 'backend\Sarab-LICENSE.txt') -Force
foreach ($name in @('SarabGnss.dll','SarabGnss.cat','SarabGnss.inf','SarabGnss.cer')) { Copy-Item -LiteralPath (Join-Path $vendor $name) -Destination (Join-Path $outputRoot 'driver') -Force }
foreach ($name in @('README.md','README.zh-CN.md','LICENSE','NOTICE','CREDITS.md','CREDITS.html','THIRD-PARTY-NOTICES.md')) { $file = Join-Path $projectRoot $name; if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $outputRoot -Force } }
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -File | Copy-Item -Destination (Join-Path $outputRoot 'docs') -Force
$hashes = Get-ChildItem -LiteralPath $outputRoot -File -Recurse | Where-Object Name -ne 'SHA256SUMS.txt' | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + $_.Path.Substring($outputRoot.Length + 1) }
[System.IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS.txt'), $hashes, (New-Object System.Text.UTF8Encoding $false))
$publishedRoot = Join-Path $projectRoot 'dist'
if (Test-Path -LiteralPath $publishedRoot) {
    $preserved = Join-Path $buildRoot ('dist-preserved-' + [DateTime]::Now.ToString('yyyyMMddHHmmssfff'))
    Move-Item -LiteralPath $publishedRoot -Destination $preserved
}
Move-Item -LiteralPath $outputRoot -Destination $publishedRoot
Write-Output "Built: $publishedRoot\SarabDesktop.exe"

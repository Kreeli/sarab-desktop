param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$localeFiles = @('en-US.json','ja-JP.json','zh-TW.json')
$dictionaries = @{}
foreach ($filename in $localeFiles) {
    $path = Join-Path $ProjectRoot ('locales\' + $filename)
    if (-not (Test-Path -LiteralPath $path)) { throw ('Missing locale file: ' + $filename) }
    $dictionary = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $map = @{}
    foreach ($item in $dictionary.PSObject.Properties) {
        if ([string]::IsNullOrWhiteSpace([string]$item.Value)) { throw ('Empty translation in ' + $filename + ': ' + $item.Name) }
        $map[$item.Name] = $true
    }
    $dictionaries[$filename] = $map
}
$englishKeys = $dictionaries['en-US.json']
$required = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'src') -File -Filter '*.cs') {
    $source = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($source, 'L\.(?:T|F)\(\s*"((?:\\.|[^"\\])*)"')) {
        $key = [regex]::Unescape($match.Groups[1].Value)
        if ($key -match '[\u3400-\u9fff]') { [void]$required.Add($key) }
    }
}
[xml]$xaml = [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'src\MainWindow.xaml'))
foreach ($node in $xaml.SelectNodes('//*')) {
    foreach ($attribute in $node.Attributes) {
        if ($attribute.LocalName -in @('Text','Content','Header','ToolTip') -and $attribute.Value -match '[\u3400-\u9fff]') { [void]$required.Add($attribute.Value) }
    }
}
foreach ($node in $xaml.SelectNodes('//text()')) {
    $text = $node.Value.Trim()
    if ($text -match '[\u3400-\u9fff]') { [void]$required.Add($text) }
}
$missingSource = @($required | Where-Object { -not $englishKeys.ContainsKey($_) })
if ($missingSource.Count) { throw ('Missing English source mappings: ' + ($missingSource -join ' | ')) }
$referenceKeys = @($englishKeys.Keys)
foreach ($filename in $localeFiles | Where-Object { $_ -ne 'en-US.json' }) {
    $keys = $dictionaries[$filename]
    $missing = @($referenceKeys | Where-Object { -not $keys.ContainsKey($_) })
    $extra = @($keys.Keys | Where-Object { -not $englishKeys.ContainsKey($_) })
    if ($missing.Count -or $extra.Count) { throw ('Locale key mismatch in ' + $filename + '. Missing: ' + ($missing -join ' | ') + '. Extra: ' + ($extra -join ' | ')) }
}
Write-Output ('Localization checked: {0} source strings, {1} complete embedded dictionaries plus the zh-CN source.' -f $required.Count, $englishKeys.Count)

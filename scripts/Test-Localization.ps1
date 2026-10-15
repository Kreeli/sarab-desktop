param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$dictionary = Get-Content -LiteralPath (Join-Path $ProjectRoot 'locales\en-US.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$keys = @{}; foreach ($item in $dictionary.PSObject.Properties) { if ([string]::IsNullOrWhiteSpace($item.Value)) { throw ('Empty English translation: ' + $item.Name) }; $keys[$item.Name] = $true }
$required = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'src') -File -Filter '*.cs') {
    $source = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($source, 'L\.(?:T|F)\(\s*"((?:\\.|[^"\\])*)"')) { $key = [regex]::Unescape($match.Groups[1].Value); if ($key -match '[\u3400-\u9fff]') { [void]$required.Add($key) } }
}
[xml]$xaml = [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'src\MainWindow.xaml'))
foreach ($node in $xaml.SelectNodes('//*')) { foreach ($attribute in $node.Attributes) { if ($attribute.LocalName -in @('Text','Content','Header','ToolTip') -and $attribute.Value -match '[\u3400-\u9fff]') { [void]$required.Add($attribute.Value) } } }
$missing = @($required | Where-Object { -not $keys.ContainsKey($_) })
if ($missing.Count) { throw ('Missing English translations: ' + ($missing -join ' | ')) }
Write-Output ('Localization checked: {0} required strings, {1} translations.' -f $required.Count, $keys.Count)

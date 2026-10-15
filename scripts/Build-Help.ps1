param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
function Convert-Inline([string]$text) {
    $html = [System.Net.WebUtility]::HtmlEncode($text)
    $html = [regex]::Replace($html, '\[([^\]]+)\]\(([^)]+)\)', '<a href="$2">$1</a>')
    $html = [regex]::Replace($html, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
    $html = [regex]::Replace($html, '`([^`]+)`', '<code>$1</code>')
    $html = $html.Replace('href="INSTALL.md"','href="INSTALL.html"').Replace('href="INSTALL.zh-CN.md"','href="INSTALL.zh-CN.html"').Replace('href="../README.md"','href="https://github.com/Kreeli/sarab-desktop"').Replace('href="../README.zh-CN.md"','href="https://github.com/Kreeli/sarab-desktop/blob/main/README.zh-CN.md"')
    return $html
}
foreach ($relative in @('CREDITS.md','docs\INSTALL.md','docs\INSTALL.zh-CN.md')) {
    $source = Join-Path $ProjectRoot $relative
    $lines = [System.IO.File]::ReadAllLines($source)
    $body = New-Object System.Text.StringBuilder
    $fenced = $false
    foreach ($line in $lines) {
        if ($line.StartsWith('```')) { if ($fenced) { [void]$body.AppendLine('</code></pre>') } else { [void]$body.AppendLine('<pre><code>') }; $fenced = -not $fenced; continue }
        if ($fenced) { [void]$body.AppendLine([System.Net.WebUtility]::HtmlEncode($line)); continue }
        if ($line -match '^(#{1,6}) (.*)$') { $level = $matches[1].Length; [void]$body.AppendLine(('<h{0}>{1}</h{0}>' -f $level,(Convert-Inline $matches[2]))) }
        elseif ($line -match '^\s*$') { [void]$body.AppendLine('') }
        else { [void]$body.AppendLine(('<p>' + (Convert-Inline $line) + '</p>')) }
    }
    $html = '<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Sarab Desktop · Help</title><style>body{margin:40px auto;padding:0 28px 60px;max-width:900px;background:#191923;color:#eeeaf9;font:16px/1.8 system-ui,"Microsoft YaHei UI",sans-serif}h1,h2{color:#c9bbff;line-height:1.3;margin-top:1.6em}a{color:#c9bbff}code{font-family:Consolas,monospace;background:#2a263b;padding:2px 5px;border-radius:4px}pre{background:#22222f;border:1px solid #454055;border-radius:12px;padding:18px;overflow:auto}pre code{background:none;padding:0}strong{color:#fff}p{margin:14px 0}</style>' + $body.ToString() + '</html>'
    $target = [System.IO.Path]::ChangeExtension($source, '.html')
    [System.IO.File]::WriteAllText($target, $html, (New-Object System.Text.UTF8Encoding $false))
}

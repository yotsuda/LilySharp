# scripttie-lp.svg: glyph path を d でグループ化し、accent 候補（同一 d が多数）の絶対座標を出す
$svg = Get-Content audit\lpreg\scripttie-lp.svg -Raw
# 各 glyph: <g transform="translate(x, y)"><path ... d="..."/></g>
$ms = [regex]::Matches($svg, '<g transform="translate\(([\d.\-]+), ([\d.\-]+)\)">\s*<path[^>]*? d="([^"]+)"')
"glyph paths: $($ms.Count)"
$byD = $ms | Group-Object { $_.Groups[3].Value } | Sort-Object Count -Descending
foreach ($g in $byD | Select-Object -First 8) {
    $sig = $g.Name.Substring(0, [Math]::Min(28, $g.Name.Length))
    "count=$($g.Count) d~ $sig"
}
'--- positions of top-3 groups ---'
foreach ($g in $byD | Select-Object -First 3) {
    $sig = $g.Name.Substring(0, [Math]::Min(18, $g.Name.Length))
    foreach ($m in $g.Group) {
        "d~{0} x={1} y={2}" -f $sig, $m.Groups[1].Value, $m.Groups[2].Value
    }
}
'--- staff line rows (system centers) ---'
[regex]::Matches($svg, '<g transform="translate\(([\d.\-]+), ([\d.\-]+)\)">\s*<line[^>]*x2="10[01]\.[\d.]+"') |
  ForEach-Object { $_.Groups[2].Value } | Sort-Object { [double]$_ } -Unique

# slurvsky 用 LP 抽出器: translate 付き要素を staff 相対で一覧。
# 五線 = stroke-width 0.1000 の全幅 <line>。中央線 translate Y を原点にする。
param([string]$Path = "$PSScriptRoot\slurvsky-lp.svg")
$svgText = [IO.File]::ReadAllText($Path)
$all = [regex]::Matches($svgText,
  '<g transform="translate\(([^,]+),\s*([^)]+)\)">\s*(<(?:line|path|polygon|rect|text)\b.*?)</g>',
  'Singleline')
$staffYs = @()
foreach ($m in $all) {
  if ($m.Groups[3].Value -match 'stroke-width="0\.1000"[^>]*x1="0\.0500"') {
    $staffYs += [double]$m.Groups[2].Value
  }
}
$staffYs = $staffYs | Sort-Object
"staff line Ys: $($staffYs -join ', ')"
$mid = $staffYs[[int]($staffYs.Count/2)]
"middleY = $mid  (spacing $([Math]::Round($staffYs[1]-$staffYs[0],4)))"
$rows = foreach ($m in $all) {
  $x = [Math]::Round([double]$m.Groups[1].Value, 4)
  $y = [Math]::Round([double]$m.Groups[2].Value - $mid, 4)
  $body = $m.Groups[3].Value
  $kind =
    if ($body -match '^<text') {
      $t = [regex]::Match($body, '<tspan>([^<]*)</tspan>').Groups[1].Value
      "text '$t'"
    } elseif ($body -match '^<line') {
      $w = [regex]::Match($body, 'stroke-width="([\d.]+)"').Groups[1].Value
      $x2 = [regex]::Match($body, 'x2="([\d.\-]+)"').Groups[1].Value
      "line w=$w x2=$x2"
    } elseif ($body -match '^<path[^>]*scale\(') {
      $d0 = [regex]::Match($body, 'd="(M[\d\- ]+)').Groups[1].Value
      "glyph $d0"
    } elseif ($body -match '^<path') {
      $d0 = [regex]::Match($body, 'd="([^"]{0,60})').Groups[1].Value
      "path $d0"
    } elseif ($body -match '^<rect') {
      $w = [regex]::Match($body, 'width="([\d.]+)"').Groups[1].Value
      $h = [regex]::Match($body, 'height="([\d.]+)"').Groups[1].Value
      "rect ${w}x${h}"
    } else { $body.Substring(0, [Math]::Min(50, $body.Length)) }
  [pscustomobject]@{ X = $x; Y = $y; What = $kind }
}
$rows | Sort-Object X, Y | Format-Table -AutoSize | Out-String -Width 200

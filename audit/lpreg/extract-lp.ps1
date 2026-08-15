# LP SVG extractor: translate + element + short d/attrs, staff-relative coords.
param([string]$Path, [double]$OriginX = 8.5358, [double]$OriginY = 11.6906)
$svgText = [IO.File]::ReadAllText($Path)
$rows = foreach ($m in [regex]::Matches($svgText,'<g transform="translate\(([^)]+)\)">\s*<(rect|path|polygon)([^>]*?)(?: d="(M[^ c]+ [^ c]+)|(/?>))')) {
  $xy = $m.Groups[1].Value.Split(',')
  $px = [Math]::Round([double]$xy[0] - $OriginX, 3)
  $py = [Math]::Round([double]$xy[1] - $OriginY, 3)
  $attrs = $m.Groups[3].Value
  $glyph = $m.Groups[4].Value
  $rw = [regex]::Match($attrs,'width="([\d.]+)"').Groups[1].Value
  $rx = [regex]::Match($attrs,'x="([\d.\-]+)"').Groups[1].Value
  $ry = [regex]::Match($attrs,'y="([\d.\-]+)"').Groups[1].Value
  $rh = [regex]::Match($attrs,'height="([\d.]+)"').Groups[1].Value
  $sc = [regex]::Match($attrs,'scale\(([\d.\-]+)').Groups[1].Value
  [pscustomobject]@{ El=$m.Groups[2].Value; X=$px; Y=$py; Glyph=$glyph; Scale=$sc; RX=$rx; RY=$ry; RW=$rw; RH=$rh }
}
$rows | Sort-Object X, Y | Format-Table -AutoSize

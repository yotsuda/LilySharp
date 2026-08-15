# Lily# SVG extractor: music glyphs (with codepoint), stems (0.13 lines), barline rects.
# Staff-relative: X as-is (staff starts at 0), Y minus middle-line Y (given).
param([string]$Path, [double]$MiddleY)
$svgText = [IO.File]::ReadAllText($Path)
if (-not $MiddleY) {
  # middle line = 3rd staff line: median of the five 0.100-width full-length lines
  $staffYs = [regex]::Matches($svgText,'<line x1="0.00" y1="([\d.]+)".*?stroke-width="0.100"') |
    ForEach-Object { [double]$_.Groups[1].Value } | Sort-Object
  $MiddleY = $staffYs[[int]($staffYs.Count/2)]
}
"middleY = $MiddleY"
$rows = @()
foreach ($m in [regex]::Matches($svgText,'<text class="music"[^>]* x="([\d.\-]+)" y="([\d.\-]+)" font-size="([\d.]+)"[^>]*>(.+?)</text>')) {
  $cp = ('U+{0:X4}' -f [int][char]$m.Groups[4].Value[0])
  $rows += [pscustomobject]@{ El='glyph'; X=[double]$m.Groups[1].Value; Y=[Math]::Round([double]$m.Groups[2].Value-$MiddleY,3); Info="fs=$($m.Groups[3].Value) cp=$cp" }
}
foreach ($m in [regex]::Matches($svgText,'<line x1="([\d.]+)" y1="([\d.]+)" x2="[\d.]+" y2="([\d.]+)"[^>]*stroke-width="0.130"')) {
  $rows += [pscustomobject]@{ El='stem'; X=[double]$m.Groups[1].Value; Y=[Math]::Round([double]$m.Groups[2].Value-$MiddleY,3); Info=("to {0}" -f ([Math]::Round([double]$m.Groups[3].Value-$MiddleY,3))) }
}
foreach ($m in [regex]::Matches($svgText,'<rect x="([\d.]+)" y="([\d.]+)" width="([\d.]+)" height="([\d.]+)"[^>]*data-pos')) {
  $rows += [pscustomobject]@{ El='rect'; X=[double]$m.Groups[1].Value; Y=[Math]::Round([double]$m.Groups[2].Value-$MiddleY,3); Info="w=$($m.Groups[3].Value) h=$($m.Groups[4].Value)" }
}
$rows | Sort-Object X, Y | Format-Table -AutoSize

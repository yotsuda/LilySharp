# Lily# hairpin extractor: sloped arm lines + noteheads + barlines, absolute page coords.
param([string]$Path)
$svgText = [IO.File]::ReadAllText($Path)

"--- staff middle lines (y of 3rd line per system) ---"
$staffYs = [regex]::Matches($svgText,'<line x1="0.00" y1="([\d.]+)"[^>]*stroke-width="0.100"') |
  ForEach-Object { [double]$_.Groups[1].Value } | Sort-Object
for ($i = 2; $i -lt $staffYs.Count; $i += 5) { $staffYs[$i] }

"--- hairpin arms (sloped or long horizontal non-staff lines) ---"
$lines = [regex]::Matches($svgText,'<line x1="([-\d.]+)" y1="([-\d.]+)" x2="([-\d.]+)" y2="([-\d.]+)"[^>]*stroke-width="0.100"')
foreach ($m in $lines) {
  $lx1=[double]$m.Groups[1].Value; $ly1=[double]$m.Groups[2].Value
  $lx2=[double]$m.Groups[3].Value; $ly2=[double]$m.Groups[4].Value
  if ([Math]::Abs($ly1-$ly2) -gt 0.01) {
    "arm ($lx1,$ly1)-($lx2,$ly2)"
  }
}

"--- noteheads / dynamics glyphs ---"
foreach ($m in [regex]::Matches($svgText,'<text class="music"[^>]* x="([-\d.]+)" y="([-\d.]+)" font-size="([\d.]+)"[^>]*>(.+?)</text>')) {
  $cp = ('U+{0:X4}' -f [int][char]$m.Groups[4].Value[0])
  "glyph x=$($m.Groups[1].Value) y=$($m.Groups[2].Value) cp=$cp"
}

"--- barlines ---"
foreach ($m in [regex]::Matches($svgText,'<rect x="([-\d.]+)" y="([-\d.]+)" width="(0\.19|0\.60)" height="([\d.]+)"')) {
  "bar x=$($m.Groups[1].Value) y=$($m.Groups[2].Value) w=$($m.Groups[3].Value)"
}

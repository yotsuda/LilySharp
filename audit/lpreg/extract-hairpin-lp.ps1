# LP hairpin-book extractor: staff spans, hairpin arms, glyphs, barlines.
param([string]$Path)
$svgText = [IO.File]::ReadAllText($Path)

"--- staff systems (translate-Y of middle line, x2 span) ---"
$staff = [regex]::Matches($svgText,'<g transform="translate\(8\.5358, ([\d.]+)\)">\s*<line[^>]*x2="([\d.]+)"')
$staff | ForEach-Object { "{0} len={1}" -f $_.Groups[1].Value, $_.Groups[2].Value } |
  Group-Object | ForEach-Object { $_.Name }

"--- sloped/short lines (hairpin arms), page coords ---"
$lines = [regex]::Matches($svgText,'<g transform="translate\(([\d.]+), ([\d.]+)\)">\s*<line[^>]*x1="([-\d.]+)" y1="([-\d.]+)" x2="([-\d.]+)" y2="([-\d.]+)"')
foreach ($m in $lines) {
  $gx=[double]$m.Groups[1].Value; $gy=[double]$m.Groups[2].Value
  $lx1=[double]$m.Groups[3].Value; $ly1=[double]$m.Groups[4].Value
  $lx2=[double]$m.Groups[5].Value; $ly2=[double]$m.Groups[6].Value
  if ($gx -ne 8.5358) {
    "arm t=($gx,$gy) rel=($lx1,$ly1)-($lx2,$ly2) startX=$([Math]::Round($gx-8.5358+$lx1,3)) endX=$([Math]::Round($gx-8.5358+$lx2,3))"
  }
}

"--- glyphs (path) and rects, page coords ---"
$els = [regex]::Matches($svgText,'<g transform="translate\(([\d.]+), ([\d.]+)\)">\s*<(rect|path)([^>]*?)(?: d="(M[-\d]+ [-\d]+)|(/>))')
foreach ($m in $els) {
  $gx=[double]$m.Groups[1].Value; $gy=[double]$m.Groups[2].Value
  $kind=$m.Groups[3].Value; $d=$m.Groups[5].Value
  $w=[regex]::Match($m.Groups[4].Value,'width="([\d.]+)"').Groups[1].Value
  "{0} t=({1},{2}) relX={3} d={4} w={5}" -f $kind,$gx,$gy,[Math]::Round($gx-8.5358,3),$d,$w
}

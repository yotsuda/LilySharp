# Lily# extraction: group glyphs/labels by measure via data-pos -> .lys source line.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp
$src = Get-Content audit\lp-regression\lys\chord-name-entry.lys -Raw
$ls  = Get-Content audit\lpreg\chord-name-entry-lys.svg -Raw

# line start offsets
$lineStarts = @(0)
for ($i = 0; $i -lt $src.Length; $i++) { if ($src[$i] -eq "`n") { $lineStarts += ($i + 1) } }
function LineOf($pos) {
  for ($i = $lineStarts.Count - 1; $i -ge 0; $i--) { if ($pos -ge $lineStarts[$i]) { return $i + 1 } }
  return 0
}

$gmap = @{ "$([char]0xE0FC)" = 'head'; "$([char]0xE021)" = 'flat'; "$([char]0xE013)" = 'sharp'; "$([char]0xE02A)" = 'dflat'; "$([char]0xE095)" = 'nat' }
$music = [regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*data-pos="(\d+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; pos=[int]$_.Groups[3].Value; g=$gmap[$_.Groups[4].Value]; ln=(LineOf ([int]$_.Groups[3].Value))} }
$labels = [regex]::Matches($ls, '<text x="([-\d.]+)" y="([-\d.]+)"[^>]*font-style="italic"[^>]*data-pos="(\d+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; pos=[int]$_.Groups[3].Value; t=$_.Groups[4].Value; ln=(LineOf ([int]$_.Groups[3].Value))} }
$staffTops = [regex]::Matches($ls, '<line x1="0\.00" y1="([-\d.]+)"') | ForEach-Object { [double]$_.Groups[1].Value } | Sort-Object | Group-Object { [math]::Floor($_ / 6) } | ForEach-Object { ($_.Group | Measure-Object -Minimum).Minimum }
"staff line tops: $($staffTops -join ', ')"
"music glyphs: $(@($music).Count)  labels: $(@($labels).Count)"

function TopFor($y) { ($staffTops | Where-Object { $_ -le ($y + 6) } | Select-Object -Last 1) }

"--- Lily# by source line ---"
$music | Where-Object g | Group-Object ln | Sort-Object { [int]$_.Name } | ForEach-Object {
  $heads = @($_.Group | Where-Object g -eq 'head' | Sort-Object y)
  if (-not $heads.Count) { return }
  $top = TopFor $heads[0].y
  $posStr = ($heads | ForEach-Object { [math]::Round(($_.y - $top) * 2) / 2 }) -join ','
  $accStr = (@($_.Group | Where-Object { $_.g -in 'flat','sharp','dflat','nat' } | Sort-Object x |
    ForEach-Object { "$($_.g)@$([math]::Round(($_.y - $top) * 2) / 2)" }) -join ' ')
  $xmin = ($heads | Measure-Object x -Minimum).Minimum
  $lab = $labels | Where-Object ln -eq ([int]$_.Name) | Select-Object -First 1
  $labS = if ($lab) { "'$($lab.t)' dx=$([math]::Round($lab.x - $xmin, 2)) yrel=$([math]::Round($lab.y - $top, 2))" } else { '' }
  "line $($_.Name) x=$([math]::Round($xmin,2)) pos: $posStr | $accStr | $labS"
}

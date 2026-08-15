# chord-tremolo gap check: beam polygon X-extents vs stem X positions.
# LP polygon points are "x y x y ..." space-separated (NOT x,y pairs).
param([string]$name = 'chord-tremolo')
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp\audit\lpreg

"=== LP beams (polygon x-extent, y-mid) and stems (rect x, y0-y1) ==="
[xml]$lp = Get-Content "$name.svg" -Raw
$script:polys = [System.Collections.Generic.List[object]]::new()
$script:stems = [System.Collections.Generic.List[object]]::new()
function Walk($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\(([-\d.]+),\s*([-\d.]+)\)') { $tx += [double]$Matches[1]; $ty += [double]$Matches[2] }
  if ($node.LocalName -eq 'polygon') {
    $nums = ($node.GetAttribute('points') -split '\s+') | Where-Object { $_ } | ForEach-Object { [double]$_ }
    $xs = @(); $ys = @()
    for ($i = 0; $i -lt $nums.Count; $i += 2) { $xs += $nums[$i] + $tx; $ys += $nums[$i+1] + $ty }
    $script:polys.Add([pscustomobject]@{
      x0=[math]::Round(($xs | Measure-Object -Minimum).Minimum,2)
      x1=[math]::Round(($xs | Measure-Object -Maximum).Maximum,2)
      y=[math]::Round((($ys | Measure-Object -Average).Average),2)})
  } elseif ($node.LocalName -eq 'rect') {
    $w = [double]$node.GetAttribute('width'); $h = [double]$node.GetAttribute('height')
    if ($w -lt 0.3 -and $h -gt 1) { # stems: thin & tall
      $x = $tx + [double]$node.GetAttribute('x'); $y = $ty + [double]$node.GetAttribute('y')
      $script:stems.Add([pscustomobject]@{x=[math]::Round($x + $w/2,2); y0=[math]::Round($y,2); y1=[math]::Round($y+$h,2)})
    }
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty }
}
Walk $lp.DocumentElement 0 0
"-- beams --"
$script:polys | Sort-Object y, x0 | ForEach-Object { "x $($_.x0)..$($_.x1)  y $($_.y)" }
"-- stems --"
$script:stems | Sort-Object x | ForEach-Object { "x $($_.x)  y $($_.y0)..$($_.y1)" }

"=== Lily# beams (polygon x-extent, y-mid) and stems (line x, y0-y1) ==="
$ls = Get-Content "$name-lys.svg" -Raw
[regex]::Matches($ls, '<polygon points="([^"]+)"') | ForEach-Object {
  $nums = ($_.Groups[1].Value -split '[,\s]+') | Where-Object { $_ } | ForEach-Object { [double]$_ }
  $xs = @(); $ys = @()
  for ($i = 0; $i -lt $nums.Count; $i += 2) { $xs += $nums[$i]; $ys += $nums[$i+1] }
  [pscustomobject]@{
    x0=[math]::Round(($xs | Measure-Object -Minimum).Minimum,2)
    x1=[math]::Round(($xs | Measure-Object -Maximum).Maximum,2)
    y=[math]::Round((($ys | Measure-Object -Average).Average),2)}
} | Sort-Object y, x0 | ForEach-Object { "beam x $($_.x0)..$($_.x1)  y $($_.y)" }
[regex]::Matches($ls, '<line x1="([-\d.]+)" y1="([-\d.]+)" x2="([-\d.]+)" y2="([-\d.]+)"[^>]*class="stem"') | ForEach-Object {
  "stem x $($_.Groups[1].Value)  y $($_.Groups[2].Value)..$($_.Groups[4].Value)"
}
if ([regex]::Matches($ls, 'class="stem"').Count -eq 0) {
  "-- no class=stem; vertical lines instead --"
  [regex]::Matches($ls, '<line x1="([-\d.]+)" y1="([-\d.]+)" x2="([-\d.]+)" y2="([-\d.]+)"') | ForEach-Object {
    if ($_.Groups[1].Value -eq $_.Groups[3].Value) { "vline x $($_.Groups[1].Value)  y $($_.Groups[2].Value)..$($_.Groups[4].Value)" }
  }
}

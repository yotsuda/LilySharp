# Rough bbox (control-point extrema) of each clef path in the LP SVG, in staff spaces.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp
[xml]$lpx = Get-Content audit\lpreg\clefend-lp.svg -Raw
function WalkLp($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\((-?[\d.]+),\s*(-?[\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d')
    if ($d.Length -gt 300) {   # clefs and other big glyphs only
      $nums = [regex]::Matches($d, '-?\d+\.?\d*') | ForEach-Object { [double]$_.Value }
      $xs = @(); $ys = @()
      for ($i = 0; $i -lt $nums.Count - 1; $i += 2) { $xs += $nums[$i]; $ys += $nums[$i+1] }
      $w = (($xs | Measure-Object -Maximum).Maximum - ($xs | Measure-Object -Minimum).Minimum) * 0.004
      $h = (($ys | Measure-Object -Maximum).Maximum - ($ys | Measure-Object -Minimum).Minimum) * 0.004
      "path at ($([math]::Round($tx,3)),$([math]::Round($ty,3))) dlen=$($d.Length) bbox w=$([math]::Round($w,3)) h=$([math]::Round($h,3)) ss"
    }
  }
  foreach ($c in $node.ChildNodes) { WalkLp $c $tx $ty }
}
WalkLp $lpx.DocumentElement 0 0

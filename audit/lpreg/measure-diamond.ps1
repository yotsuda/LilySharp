# Ink widths of the two heads in colharm-lp.svg (control-point extrema).
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp
[xml]$x = Get-Content audit\lpreg\colharm-lp.svg -Raw
function Walk($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\((-?[\d.]+),\s*(-?[\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d')
    if ($d -notmatch '[hvHV]') {
      $nums = [regex]::Matches($d, '-?\d+\.?\d*') | ForEach-Object { [double]$_.Value }
      $xs = @(); for ($i = 0; $i -lt $nums.Count - 1; $i += 2) { $xs += $nums[$i] }
      $w = (($xs | Measure-Object -Maximum).Maximum - ($xs | Measure-Object -Minimum).Minimum) * 0.004
      "path @($([math]::Round($tx,4)),$([math]::Round($ty,4))) w=$([math]::Round($w,4)) dlen=$($d.Length)"
    } else {
      "path @($([math]::Round($tx,4)),$([math]::Round($ty,4))) has h/v (skip) dlen=$($d.Length)"
    }
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty }
}
Walk $x.DocumentElement 0 0

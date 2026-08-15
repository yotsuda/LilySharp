# Everything in both clefend SVGs: LP paths with translate+href, Lily# texts/lines.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

"=== LP items ==="
[xml]$lpx = Get-Content audit\lpreg\clefend-lp.svg -Raw
function WalkLp($node, $tx, $ty, $href) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'a') {
    foreach ($at in $node.Attributes) { if ($at.LocalName -eq 'href') { $href = $at.Value } }
  }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\((-?[\d.]+),\s*(-?[\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($t -match 'scale\((-?[\d.]+)') { $sc = $Matches[1] } else { $sc = '' }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d'); if ($d.Length -gt 24) { $d = $d.Substring(0,24) }
    "path x=$([math]::Round($tx,3)) y=$([math]::Round($ty,3)) scale=$sc d=$d src=$($href -replace '.*\.ly:','')"
  } elseif ($node.LocalName -eq 'line' -or $node.LocalName -eq 'rect') {
    "$($node.LocalName) x=$([math]::Round($tx,3)) y=$([math]::Round($ty,3))"
  }
  foreach ($c in $node.ChildNodes) { WalkLp $c $tx $ty $href }
}
WalkLp $lpx.DocumentElement 0 0 $null

"=== Lily# items ==="
[xml]$lsx = Get-Content audit\lpreg\clefend-ls.svg -Raw
function WalkLs($node) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'text') {
    $cp = ([int][char]$node.InnerText[0]).ToString('x4')
    "text x=$($node.GetAttribute('x')) y=$($node.GetAttribute('y')) cp=$cp fs=$($node.GetAttribute('font-size')) pos=$($node.GetAttribute('data-pos'))"
  } elseif ($node.LocalName -eq 'line') {
    "line ($($node.GetAttribute('x1')),$($node.GetAttribute('y1')))-($($node.GetAttribute('x2')),$($node.GetAttribute('y2')))"
  }
  foreach ($c in $node.ChildNodes) { WalkLs $c }
}
WalkLs $lsx.DocumentElement

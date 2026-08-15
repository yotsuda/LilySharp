# Full path data for the 6 curve paths (slurs+ties) on each side, absolute coords.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

[xml]$lpx = Get-Content audit\lpreg\chord-X-align-lp.svg -Raw
$script:out = [System.Collections.Generic.List[object]]::new()
function WalkLp($node, $tx, $ty, $href) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'a') {
    foreach ($at in $node.Attributes) { if ($at.LocalName -eq 'href') { $href = $at.Value } }
  }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\((-?[\d.]+),\s*(-?[\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d')
    if ($d -match '^M-?[\d.]+\.[\d]+ ') {  # decimal-coordinate curves = slur/tie
      $script:out.Add([pscustomobject]@{tx=$tx; ty=$ty; src=($href -replace '.*\.ly:', ''); d=$d})
    }
  }
  foreach ($c in $node.ChildNodes) { WalkLp $c $tx $ty $href }
}
WalkLp $lpx.DocumentElement 0 0 $null
"=== LP curves (translate + local d) ==="
foreach ($c in $script:out) {
  "src=$($c.src) translate=($($c.tx),$($c.ty))"
  "  d=$($c.d)"
  # first M point absolute
  if ($c.d -match '^M(-?[\d.]+) (-?[\d.]+)') {
    $sx = $c.tx + [double]$Matches[1]; $sy = $c.ty + [double]$Matches[2]
    "  start abs = ($([math]::Round($sx,3)), $([math]::Round($sy,3)))"
  }
}

"`n=== Lily# curves (absolute d) ==="
[xml]$lsx = Get-Content audit\lpreg\chord-X-align-ls.svg -Raw
function WalkLs($node) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'path') {
    "pos=$($node.GetAttribute('data-pos'))"
    "  d=$($node.GetAttribute('d'))"
  }
  foreach ($c in $node.ChildNodes) { WalkLs $c }
}
WalkLs $lsx.DocumentElement

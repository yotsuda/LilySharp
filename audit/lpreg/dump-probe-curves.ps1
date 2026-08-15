# Probe: heads + curves from both probe SVGs.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

[xml]$lpx = Get-Content audit\lpreg\probe-slur-tie-x-lp.svg -Raw
"=== LP ==="
$script:items = [System.Collections.Generic.List[object]]::new()
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
    if ($d -match '^M217 136') {
      "head  x=$([math]::Round($tx,3)) y=$([math]::Round($ty,3)) src=$($href -replace '.*\.ly:','')"
    } elseif ($d -match '^M-?[\d.]+\.[\d]+ ') {
      "curve translate=($([math]::Round($tx,4)),$([math]::Round($ty,4))) src=$($href -replace '.*\.ly:','')"
      "  d=$d"
    }
  }
  foreach ($c in $node.ChildNodes) { WalkLp $c $tx $ty $href }
}
WalkLp $lpx.DocumentElement 0 0 $null

"=== Lily# ==="
[xml]$lsx = Get-Content audit\lpreg\probe-slur-tie-x-ls.svg -Raw
function WalkLs($node) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'text' -and $node.InnerText.Length -ge 1 -and ([int][char]$node.InnerText[0]) -eq 0xe0fe) {
    "head  x=$($node.GetAttribute('x')) y=$($node.GetAttribute('y')) pos=$($node.GetAttribute('data-pos'))"
  } elseif ($node.LocalName -eq 'path') {
    "curve d=$($node.GetAttribute('d'))"
  }
  foreach ($c in $node.ChildNodes) { WalkLs $c }
}
WalkLs $lsx.DocumentElement

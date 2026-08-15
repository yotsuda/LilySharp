# Extract chord-name text from both funky SVGs, grouped left-to-right.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

"=== LP tspans (x, y, text) ==="
[xml]$lpx = Get-Content audit\lpreg\funky-lp.svg -Raw
$script:lp = [System.Collections.Generic.List[object]]::new()
function WalkLp($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\((-?[\d.]+),\s*(-?[\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'text' -or $node.LocalName -eq 'tspan') {
    $txt = ($node.InnerText -replace '\s+', ' ').Trim()
    if ($txt) { $script:lp.Add([pscustomobject]@{x=[math]::Round($tx,2); y=[math]::Round($ty,2); t=$txt}) }
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d')
    # non-glyph decimal paths in the name row could be triangles; note size only
    $script:lp.Add([pscustomobject]@{x=[math]::Round($tx,2); y=[math]::Round($ty,2); t="[path len $($d.Length)]"})
  }
  foreach ($c in $node.ChildNodes) { WalkLp $c $tx $ty }
}
WalkLp $lpx.DocumentElement 0 0
# name row = smallest y band (names above staff); print everything sorted by y then x
$script:lp | Sort-Object y, x | Format-Table x, y, t -AutoSize | Out-String -Width 200

"=== Lily# texts (x, y, text) ==="
[xml]$lsx = Get-Content audit\lpreg\funky-ls.svg -Raw
$script:ls = [System.Collections.Generic.List[object]]::new()
function WalkLs($node) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'text') {
    $txt = $node.InnerText
    $cp = if ($txt.Length -ge 1) { [int][char]$txt[0] } else { 0 }
    if ($cp -lt 0xE000) {  # skip feta glyphs
      $script:ls.Add([pscustomobject]@{
        x=[double]$node.GetAttribute('x'); y=[double]$node.GetAttribute('y'); t=$txt })
    }
  }
  foreach ($c in $node.ChildNodes) { WalkLs $c }
}
WalkLs $lsx.DocumentElement
$script:ls | Sort-Object y, x | Format-Table x, y, t -AutoSize | Out-String -Width 200

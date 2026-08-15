# chord-X-align-on-main-noteheads: dump classified glyphs from both SVGs.
# Phase 1 (this script): raw inventories — LP paths (translate+href), Lily# text
# glyphs (x,y,codepoint,data-pos) + curve paths. Classification by count/eyeball.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

# ---------- LP ----------
[xml]$lpx = Get-Content audit\lpreg\chord-X-align-lp.svg -Raw
$script:lp = [System.Collections.Generic.List[object]]::new()
function WalkLp($node, $tx, $ty, $href) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'a') {
    $h = $null
    foreach ($at in $node.Attributes) { if ($at.LocalName -eq 'href') { $h = $at.Value } }
    if ($h) { $href = $h }
  }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\((-?[\d.]+),\s*(-?[\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d')
    $script:lp.Add([pscustomobject]@{
      x = [math]::Round($tx,3); y = [math]::Round($ty,3)
      dp = $(if ($d.Length -gt 30) { $d.Substring(0,30) } else { $d })
      dlen = $d.Length
      src = ($href -replace '.*\.ly:', '')
    })
  }
  foreach ($c in $node.ChildNodes) { WalkLp $c $tx $ty $href }
}
WalkLp $lpx.DocumentElement 0 0 $null
"=== LP path prefixes (count / dlen range) ==="
$script:lp | Group-Object dp | Sort-Object Count -Descending | ForEach-Object {
  $lens = ($_.Group | ForEach-Object dlen | Sort-Object -Unique) -join ','
  "{0,3}  len={1,-12} {2}" -f $_.Count, $lens, $_.Name
}
"=== LP items (x asc) ==="
$script:lp | Sort-Object x, y | Format-Table x, y, dp, src -AutoSize | Out-String -Width 200

# ---------- Lily# ----------
[xml]$lsx = Get-Content audit\lpreg\chord-X-align-ls.svg -Raw
$script:ls = [System.Collections.Generic.List[object]]::new()
function WalkLs($node) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'text') {
    $cp = ''
    if ($node.InnerText.Length -ge 1) {
      $cp = ([int][char]$node.InnerText[0]).ToString('x4')
      if ($node.InnerText.Length -gt 1) { $cp += "+$($node.InnerText.Length - 1)" }
    }
    $script:ls.Add([pscustomobject]@{
      kind='text'; x=[double]$node.GetAttribute('x'); y=[double]$node.GetAttribute('y')
      d=$cp; pos=$node.GetAttribute('data-pos'); fs=$node.GetAttribute('font-size')
    })
  } elseif ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d')
    $m = [regex]::Match($d, 'M\s*(-?[\d.]+)[ ,](-?[\d.]+)')
    $script:ls.Add([pscustomobject]@{
      kind='path'; x=[double]$m.Groups[1].Value; y=[double]$m.Groups[2].Value
      d=$(if ($d.Length -gt 60) { $d.Substring(0,60) } else { $d }); pos=$node.GetAttribute('data-pos'); fs=''
    })
  } elseif ($node.LocalName -eq 'line') {
    $script:ls.Add([pscustomobject]@{
      kind='line'; x=[double]$node.GetAttribute('x1'); y=[double]$node.GetAttribute('y1')
      d=("x2={0} y2={1}" -f $node.GetAttribute('x2'), $node.GetAttribute('y2')); pos=$node.GetAttribute('data-pos'); fs=''
    })
  }
  foreach ($c in $node.ChildNodes) { WalkLs $c }
}
WalkLs $lsx.DocumentElement
"=== Lily# text glyph codepoints (count) ==="
$script:ls | Where-Object kind -eq 'text' | Group-Object d | Sort-Object Count -Descending |
  Select-Object Count, Name | Format-Table -AutoSize | Out-String
"=== Lily# items (x asc, texts+paths) ==="
$script:ls | Where-Object { $_.kind -ne 'line' } | Sort-Object x, y |
  Format-Table kind, x, y, d, pos, fs -AutoSize | Out-String -Width 200

# collisions.ly: notehead (x,y,glyph) fingerprint compare, LP twin vs Lily#, 2 systems.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

[xml]$lpx = Get-Content audit\lpreg\collisions.svg -Raw
$script:lp = [System.Collections.Generic.List[object]]::new()
$script:lplines = [System.Collections.Generic.List[object]]::new()
function Walk($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\(([-\d.]+),\s*([-\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d'); if ($d.Length -gt 20) { $d = $d.Substring(0, 20) }
    $script:lp.Add([pscustomobject]@{x=[math]::Round($tx,4); y=[math]::Round($ty,4); d=$d})
  } elseif ($node.LocalName -eq 'line') {
    $script:lplines.Add([pscustomobject]@{x=[math]::Round($tx,4); y=[math]::Round($ty,4)})
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty }
}
Walk $lpx.DocumentElement 0 0
"LP path d-prefix counts (top 10):"
$script:lp | Group-Object d | Sort-Object Count -Descending | Select-Object -First 10 Count, Name | Format-Table -AutoSize | Out-String
"LP staff-line anchor xs (distinct):"
($script:lplines | Group-Object x | Sort-Object { [double]$_.Name } | ForEach-Object { "$($_.Name) (n=$($_.Count))" }) -join '; '

$ls = Get-Content audit\lpreg\collisions-lys.svg -Raw
$lsg = [regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; cp=([int][char]$_.Groups[3].Value.Substring(0,1)).ToString('X4')} }
"Lily# music glyph counts:"
$lsg | Group-Object cp | Sort-Object Count -Descending | Select-Object -First 10 Count, Name | Format-Table -AutoSize | Out-String

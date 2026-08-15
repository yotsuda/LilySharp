# collisions.ly: 1:1 head pairing by sorted (x,y); report deltas.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

[xml]$lpx = Get-Content audit\lpreg\collisions.svg -Raw
$script:lp = [System.Collections.Generic.List[object]]::new()
function Walk($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\(([-\d.]+),\s*([-\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d'); if ($d.Length -gt 20) { $d = $d.Substring(0, 20) }
    $script:lp.Add([pscustomobject]@{x=[math]::Round($tx - 8.5358, 3); y=[math]::Round($ty,3); d=$d})
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty }
}
Walk $lpx.DocumentElement 0 0
$type = @{ 'M217 136c56 0 109 -2' = 'q'; 'M303 37c7 9 11 18 11' = 'h'; 'M213 112c-48 0 -70 -' = 'w' }
$lph = @($script:lp | Where-Object { $type[$_.d] } |
  ForEach-Object { [pscustomobject]@{t=$type[$_.d]; x=$_.x; y=$_.y} } | Sort-Object x, y)

$ls = Get-Content audit\lpreg\collisions-lys.svg -Raw
$lst = @{ 'E0FE'='q'; 'E0FD'='h'; 'E0FC'='w' }
$lsh = @([regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; cp=([int][char]$_.Groups[3].Value.Substring(0,1)).ToString('X4')} } |
  Where-Object { $lst[$_.cp] } |
  ForEach-Object { [pscustomobject]@{t=$lst[$_.cp]; x=$_.x; y=$_.y} } | Sort-Object x, y)

# pair in sorted order within each system band (y<18 = sys1)
foreach ($band in 0,1) {
  $a = @($lph | Where-Object { ($_.y -lt 18) -eq ($band -eq 0) })
  $b = @($lsh | Where-Object { ($_.y -lt 18) -eq ($band -eq 0) })
  "=== system $($band+1): LP $($a.Count) vs LS $($b.Count) ==="
  $n = [Math]::Min($a.Count, $b.Count)
  for ($i = 0; $i -lt $n; $i++) {
    $dx = [math]::Round($b[$i].x - $a[$i].x, 3); $dy = [math]::Round($b[$i].y - $a[$i].y, 3)
    $flag = if ([math]::Abs($dx) -gt 0.02 -or [math]::Abs($dy) -gt 0.02) { '  <<<' } else { '' }
    "{0,2} {1} LP {2,8:F3} {3,7:F3}  LS {4,8:F3} {5,7:F3}  dx {6,7:F3}{7}" -f $i, $a[$i].t, $a[$i].x, $a[$i].y, $b[$i].x, $b[$i].y, $dx, $flag
  }
}

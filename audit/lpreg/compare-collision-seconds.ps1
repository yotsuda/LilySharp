# collision-seconds: notehead (x,y) fingerprint compare, LP twin vs Lily#.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

# ---- LP side
[xml]$lpx = Get-Content audit\lpreg\collision-seconds.svg -Raw
$script:lp = [System.Collections.Generic.List[object]]::new()
function Walk($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\(([-\d.]+),\s*([-\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d'); if ($d.Length -gt 20) { $d = $d.Substring(0, 20) }
    $script:lp.Add([pscustomobject]@{x=[math]::Round($tx,4); y=[math]::Round($ty,4); d=$d})
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty }
}
Walk $lpx.DocumentElement 0 0
$lpHeads = @($script:lp | Where-Object { $_.d -like 'M303 37c7 9 11 18 11*' })

# LP staff line Ys for reference (line elements)
$lpRaw = Get-Content audit\lpreg\collision-seconds.svg -Raw

# ---- Lily# side
$ls = Get-Content audit\lpreg\collision-seconds-lys.svg -Raw
$lsAll = [regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; cp=([int][char]$_.Groups[3].Value.Substring(0,1)).ToString('X4')} }
$lsHeads = @($lsAll | Where-Object cp -eq 'E0FD')

"LP heads: $($lpHeads.Count)   Lily# heads: $($lsHeads.Count)"
"Lily# non-head music glyphs:"
$lsAll | Where-Object cp -ne 'E0FD' | ForEach-Object { "  cp=$($_.cp) x=$($_.x) y=$($_.y)" }
""
"--- LP heads sorted by (x,y) ---"
$lpHeads | Sort-Object x, y | ForEach-Object { "{0,9:F4} {1,9:F4}" -f $_.x, $_.y }
""
"--- Lily# heads sorted by (x,y) ---"
$lsHeads | Sort-Object x, y | ForEach-Object { "{0,9:F4} {1,9:F4}" -f $_.x, $_.y }

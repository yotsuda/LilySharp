# collisions.ly: full (x,y) fingerprint, per glyph type, LP-x normalized by staff start 8.5358.
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
$type = @{ 'M217 136c56 0 109 -2' = 'q'; 'M303 37c7 9 11 18 11' = 'h'; 'M213 112c-48 0 -70 -' = 'w';
           'M0 119c0 8 5 15 13 1' = '#'; 'M27 41l-1 -66v-11c0 ' = 'b'; 'M-7 375c8 4 17 7 25 ' = '?' }
"--- LP mystery glyph ---"
$script:lp | Where-Object { $_.d -like 'M-7 375*' } | ForEach-Object { "x=$($_.x) y=$($_.y)" }
"--- LP flat/sharps ---"
$script:lp | Where-Object { $type[$_.d] -in '#','b' } | ForEach-Object { "$($type[$_.d]) x=$($_.x) y=$($_.y)" }

$ls = Get-Content audit\lpreg\collisions-lys.svg -Raw
$lsg = [regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; cp=([int][char]$_.Groups[3].Value.Substring(0,1)).ToString('X4')} }
$lst = @{ 'E0FE'='q'; 'E0FD'='h'; 'E0FC'='w'; 'E013'='#'; 'E021'='b' }
"--- Lily# accidentals ---"
$lsg | Where-Object { $lst[$_.cp] -in '#','b' } | ForEach-Object { "$($lst[$_.cp]) x=$($_.x) y=$($_.y)" }
""
"--- heads side by side (sorted y band then x) ---"
$lph = @($script:lp | Where-Object { $type[$_.d] -in 'q','h','w' } |
  ForEach-Object { [pscustomobject]@{t=$type[$_.d]; x=$_.x; y=$_.y} } | Sort-Object y, x)
$lsh = @($lsg | Where-Object { $lst[$_.cp] -in 'q','h','w' } |
  ForEach-Object { [pscustomobject]@{t=$lst[$_.cp]; x=$_.x; y=$_.y} } | Sort-Object y, x)
"LP $($lph.Count) vs LS $($lsh.Count)"
"LP y range: $($lph[0].y) .. $($lph[-1].y);  LS y range: $($lsh[0].y) .. $($lsh[-1].y)"
# system split: big gap in y
"LP distinct y (first 40): " + (($lph | Group-Object y | Sort-Object {[double]$_.Name} | Select-Object -First 40 | ForEach-Object Name) -join ' ')
"LS distinct y (first 40): " + (($lsh | Group-Object y | Sort-Object {[double]$_.Name} | Select-Object -First 40 | ForEach-Object Name) -join ' ')

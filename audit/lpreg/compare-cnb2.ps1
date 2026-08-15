# chord-names-bass: per-chord staff positions, both engines.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

# ---------- LP ----------
[xml]$lpx = Get-Content audit\lpreg\chord-names-bass.svg -Raw
$script:items = [System.Collections.Generic.List[object]]::new()
function Walk($node, $tx, $ty, $href) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'a') {
    foreach ($at in $node.Attributes) { if ($at.LocalName -eq 'href') { $href = $at.Value } }
  }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\(([-\d.]+),\s*([-\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  switch ($node.LocalName) {
    'path' {
      $d = $node.GetAttribute('d'); if ($d.Length -gt 25) { $d = $d.Substring(0, 25) }
      $script:items.Add([pscustomobject]@{kind='path'; x=[math]::Round($tx,2); y=[math]::Round($ty,2); t=$d; src=($href -replace '.*\.ly:', '')})
    }
    'line' {
      $x1 = [double]$node.GetAttribute('x1'); $x2 = [double]$node.GetAttribute('x2')
      if ([math]::Abs($x2-$x1) -gt 30) { $script:items.Add([pscustomobject]@{kind='staffline'; x=$tx; y=$ty+[double]$node.GetAttribute('y1'); t=''; src=''}) }
    }
    'tspan' {
      $script:items.Add([pscustomobject]@{kind='text'; x=[math]::Round($tx,2); y=[math]::Round($ty,2); t=$node.InnerText; src=($href -replace '.*\.ly:', '')})
    }
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty $href }
}
Walk $lpx.DocumentElement 0 0 $null
$lpTop = ($script:items | Where-Object kind -eq 'staffline' | Measure-Object y -Minimum).Minimum
"LP staff top: $lpTop"
$lpHeadD = @('M217 136c56 0 109 -27 109', 'M303 37c7 9 11 18 11 27c0')
"--- LP chords (pos in ss below top line; q=quarter h=half) ---"
$script:items | Where-Object { $_.kind -eq 'path' -and $_.t -in $lpHeadD } |
  Group-Object src | Sort-Object Name | ForEach-Object {
    $pos = ($_.Group | Sort-Object y | ForEach-Object { [math]::Round(($_.y - $lpTop) * 2) / 2 }) -join ' '
    $k = if ($_.Group[0].t -eq $lpHeadD[1]) { 'h' } else { 'q' }
    "src $($_.Name) [$k] pos: $pos"
  }
"--- LP labels/names rel top ---"
$script:items | Where-Object { $_.kind -eq 'text' -and $_.t -match ':maj7' } | Sort-Object x | ForEach-Object {
  "'$($_.t)' x=$($_.x) yrel=$([math]::Round($_.y - $lpTop, 2))"
}
$lpNames = $script:items | Where-Object { $_.kind -eq 'text' -and $_.t -in 'F','/','E','G' }
"LP name row yrel: $((($lpNames | Select-Object -First 1).y - $lpTop))"

# ---------- Lily# ----------
$src = Get-Content audit\lp-regression\lys\chord-names-bass.lys -Raw
$ls  = Get-Content audit\lpreg\chord-names-bass-lys.svg -Raw
$lsTop = ([regex]::Matches($ls, '<line x1="0\.00" y1="([-\d.]+)"') | ForEach-Object { [double]$_.Groups[1].Value } | Measure-Object -Minimum).Minimum
"Lily# staff top: $lsTop"
$heads = [regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*data-pos="(\d+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; pos=[int]$_.Groups[3].Value; cp=[int]$_.Groups[4].Value[0]} } |
  Where-Object { $_.cp -in 0xE0FD, 0xE0FE }
# chord = cluster by x (heads of one chord within 2.2ss; second-shifts included)
"--- Lily# chords (cluster by x) ---"
$sorted = $heads | Sort-Object x, y
$clusters = @(); $cl = @()
foreach ($h in $sorted) {
  if ($cl.Count -and ($h.x - ($cl | Measure-Object x -Minimum).Minimum) -gt 2.6) { $clusters += ,@($cl); $cl = @() }
  $cl += $h
}
if ($cl.Count) { $clusters += ,@($cl) }
foreach ($c in $clusters) {
  $pos = ($c | Sort-Object y | ForEach-Object { [math]::Round(($_.y - $lsTop) * 2) / 2 }) -join ' '
  $k = if ($c[0].cp -eq 0xE0FD) { 'h' } else { 'q' }
  $xmin = ($c | Measure-Object x -Minimum).Minimum
  "x=$xmin [$k] pos: $pos"
}
"--- Lily# labels rel top ---"
[regex]::Matches($ls, '<text x="([-\d.]+)" y="([-\d.]+)"[^>]*font-style="italic"[^>]*>([^<]+)</text>') |
  ForEach-Object { "'$($_.Groups[3].Value)' x=$($_.Groups[1].Value) yrel=$([math]::Round([double]$_.Groups[2].Value - $lsTop, 2))" }
"--- Lily# chord names rel top ---"
[regex]::Matches($ls, '<text x="([-\d.]+)" y="([-\d.]+)"[^>]*>(Fmaj7[^<]*)</text>') |
  ForEach-Object { "'$($_.Groups[3].Value)' x=$($_.Groups[1].Value) yrel=$([math]::Round([double]$_.Groups[2].Value - $lsTop, 2))" }

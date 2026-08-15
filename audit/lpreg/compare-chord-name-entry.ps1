# chord-name-entry: LP vs Lily# per-measure structure extraction.
# LP SVG nests coordinates in <g transform="translate(...)"> wrappers -> walk the
# XML accumulating translates. Lily#: flat <text class="music"> / plain <text>.
param()
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp

# ---------- LP: XML walk ----------
[xml]$lpx = Get-Content audit\lpreg\chord-name-entry.svg -Raw
$script:lpItems = [System.Collections.Generic.List[object]]::new()
function Walk($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\(([-\d.]+),\s*([-\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  switch ($node.LocalName) {
    'line' {
      $x1 = [double]$node.GetAttribute('x1'); $x2 = [double]$node.GetAttribute('x2')
      $script:lpItems.Add([pscustomobject]@{kind='line'; x=$tx+$x1; y=$ty+[double]$node.GetAttribute('y1'); len=[math]::Abs($x2-$x1)})
    }
    'path' {
      $d = $node.GetAttribute('d'); if ($d.Length -gt 30) { $d = $d.Substring(0, 30) }
      $script:lpItems.Add([pscustomobject]@{kind='path'; x=$tx; y=$ty; d=$d})
    }
    'tspan' {
      # tspan x/y are absolute within the enclosing text's coordinate system (already translated)
      $script:lpItems.Add([pscustomobject]@{kind='text'; x=$tx+[double]$node.GetAttribute('x'); y=$ty+[double]$node.GetAttribute('y'); t=$node.InnerText})
    }
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty }
}
Walk $lpx.DocumentElement 0 0

$dHead    = 'M213 112c-48 0 -70 -40 -70 -84'
$accMap = @{
  'M27 41l-1 -66v-11c0 -22 1 -44 ' = 'b'
  'M0 119c0 8 5 15 13 18l46 17v15' = '#'
  'M190 41l-1 -66c0 -4 -1 -7 -1 -' = 'bb'
  'M359 27c-49 0 -75 42 -75 75c0 ' = 'n'
}
$heads  = @($script:lpItems | Where-Object { $_.kind -eq 'path' -and $_.d -eq $dHead })
$accs   = @($script:lpItems | Where-Object { $_.kind -eq 'path' -and $accMap.ContainsKey($_.d) } |
            ForEach-Object { $_ | Add-Member -PassThru NoteProperty a $accMap[$_.d] })
$labels = @($script:lpItems | Where-Object { $_.kind -eq 'text' -and $_.t -notmatch '^\d+$' -and $_.t -notmatch 'LilyPond' })
$staffLines = @($script:lpItems | Where-Object { $_.kind -eq 'line' -and $_.len -gt 50 } | Sort-Object y)
"LP heads $($heads.Count) accs $($accs.Count) labels $($labels.Count) staffLines $($staffLines.Count)"

# group staff lines into systems (5 each, gap > 2)
$systems = @(); $cur = @()
foreach ($l in $staffLines) {
  if ($cur.Count -and ($l.y - $cur[-1].y) -gt 2) { $systems += ,@($cur); $cur = @() }
  $cur += $l
}
if ($cur.Count) { $systems += ,@($cur) }
"LP systems $($systems.Count): tops = $(($systems | ForEach-Object { $_[0].y }) -join ', ')"

function Emit($heads, $accs, $labels, $systems, $tag) {
  $out = @()
  foreach ($si in 0..($systems.Count-1)) {
    $top = $systems[$si][0].y; $bot = $systems[$si][-1].y
    $mid = ($top + $bot) / 2
    $sysHeads = @($heads | Where-Object { [math]::Abs($_.y - $mid) -lt 10 } | Sort-Object x, y)
    if (-not $sysHeads.Count) { continue }
    # cluster into chords by X (note-column width < 2.4; whole-note seconds shift ~1.6)
    $clusters = @(); $cl = @()
    foreach ($h in $sysHeads) {
      if ($cl.Count -and ($h.x - ($cl | Measure-Object x -Minimum).Minimum) -gt 2.6) { $clusters += ,@($cl); $cl = @() }
      $cl += $h
    }
    if ($cl.Count) { $clusters += ,@($cl) }
    foreach ($c in $clusters) {
      $xmin = ($c | Measure-Object x -Minimum).Minimum
      $xmax = ($c | Measure-Object x -Maximum).Maximum
      $pos = (@($c | ForEach-Object { [math]::Round(($_.y - $top) * 2) / 2 } | Sort-Object) -join ' ')
      $acc = (@($accs | Where-Object { [math]::Abs($_.y - $mid) -lt 10 -and $_.x -gt ($xmin - 4.5) -and $_.x -lt $xmin } |
        Sort-Object x, y | ForEach-Object { "$($_.a)@$([math]::Round(($_.y - $top) * 2) / 2)" }) -join ' ')
      $lab = $labels | Where-Object { $_.y -gt $bot -and $_.y -lt ($bot + 12) -and $_.x -gt ($xmin - 5) -and $_.x -lt ($xmax + 5) } | Select-Object -First 1
      $out += [pscustomobject]@{
        sys = $si; x = [math]::Round($xmin, 2); pos = $pos; acc = $acc
        lab = $lab.t
        labDx = if ($lab) { [math]::Round($lab.x - $xmin, 2) } else { $null }
        labY  = if ($lab) { [math]::Round($lab.y - $top, 2) } else { $null }
      }
    }
  }
  $out
}
$lpOut = Emit $heads $accs $labels $systems 'LP'
"--- LP ($($lpOut.Count) chords) ---"
$lpOut | Format-Table -AutoSize | Out-String -Width 210

# ---------- Lily# ----------
$ls = Get-Content audit\lpreg\chord-name-entry-lys.svg -Raw
$gmap = @{ "`u{E0FC}" = 'head'; "`u{E021}" = 'b'; "`u{E013}" = '#'; "`u{E02A}" = 'bb'; "`u{E095}" = 'n' }
$lsMusic = [regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; g=$_.Groups[3].Value} }
$lsHeads = @($lsMusic | Where-Object { $gmap[$_.g] -eq 'head' })
$lsAccs  = @($lsMusic | Where-Object { $gmap[$_.g] -in 'b','#','bb','n' } |
  ForEach-Object { $_ | Add-Member -PassThru NoteProperty a $gmap[$_.g] })
$lsLabels = [regex]::Matches($ls, '<text x="([-\d.]+)" y="([-\d.]+)"[^>]*font-style="italic"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{kind='text'; x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; t=$_.Groups[3].Value} }
# Lily# staff lines: rects? find wide rects (height small, width > 50) OR lines
$lsLines = [regex]::Matches($ls, '<rect x="([-\d.]+)" y="([-\d.]+)" width="([-\d.]+)" height="([-\d.]+)"') |
  ForEach-Object { [pscustomobject]@{kind='line'; x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; len=[double]$_.Groups[3].Value; h=[double]$_.Groups[4].Value} } |
  Where-Object { $_.len -gt 50 -and $_.h -lt 0.5 } | Sort-Object y
"Lily# heads $($lsHeads.Count) accs $($lsAccs.Count) labels $($lsLabels.Count) staffLines $($lsLines.Count)"
$lsSystems = @(); $cur = @()
foreach ($l in $lsLines) {
  if ($cur.Count -and ($l.y - $cur[-1].y) -gt 2) { $lsSystems += ,@($cur); $cur = @() }
  $cur += $l
}
if ($cur.Count) { $lsSystems += ,@($cur) }
"Lily# systems $($lsSystems.Count): tops = $(($lsSystems | ForEach-Object { $_[0].y }) -join ', ')"
$lsOut = Emit $lsHeads $lsAccs $lsLabels $lsSystems 'LS'
"--- Lily# ($($lsOut.Count) chords) ---"
$lsOut | Format-Table -AutoSize | Out-String -Width 210

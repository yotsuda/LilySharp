# LP extraction v2: attach the enclosing textedit href (source line:col) to every
# glyph -> group by SOURCE LINE (= measure), no geometric clustering.
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp
[xml]$lpx = Get-Content audit\lpreg\chord-name-entry.svg -Raw
$script:items = [System.Collections.Generic.List[object]]::new()
function Walk($node, $tx, $ty, $href) {
  if ($node.NodeType -ne 'Element') { return }
  if ($node.LocalName -eq 'a') {
    $h = $node.GetAttribute('href'); if (-not $h) { $h = $node.GetAttribute('xlink:href') }
    if (-not $h) { foreach ($at in $node.Attributes) { if ($at.LocalName -eq 'href') { $h = $at.Value } } }
    if ($h) { $href = $h }
  }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\(([-\d.]+),\s*([-\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d'); if ($d.Length -gt 30) { $d = $d.Substring(0, 30) }
    $script:items.Add([pscustomobject]@{kind='path'; x=[math]::Round($tx,3); y=[math]::Round($ty,3); d=$d; src=($href -replace '.*\.ly:', '')})
  } elseif ($node.LocalName -eq 'tspan') {
    $script:items.Add([pscustomobject]@{kind='text'; x=[math]::Round($tx,3); y=[math]::Round($ty,3); d=$node.InnerText; src=($href -replace '.*\.ly:', '')})
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty $href }
}
Walk $lpx.DocumentElement 0 0 $null
$glyphNames = @{
  'M213 112c-48 0 -70 -40 -70 -84' = 'head'
  'M27 41l-1 -66v-11c0 -22 1 -44 ' = 'flat'
  'M0 119c0 8 5 15 13 18l46 17v15' = 'sharp'
  'M190 41l-1 -66c0 -4 -1 -7 -1 -' = 'dflat'
  'M359 27c-49 0 -75 42 -75 75c0 ' = 'nat'
}
$script:items | Where-Object { $_.kind -eq 'path' -and $glyphNames.ContainsKey($_.d) } |
  ForEach-Object { $_ | Add-Member -Force NoteProperty g $glyphNames[$_.d] } | Out-Null
$paths = @($script:items | Where-Object { $_.kind -eq 'path' -and $_.g })
"total classified glyph paths: $($paths.Count)"
$paths | Group-Object g | Select-Object Count, Name | Format-Table -AutoSize | Out-String
"--- by source line (measure) ---"
$paths | Group-Object { ($_.src -split ':')[0] } | Sort-Object { [int]$_.Name } |
  ForEach-Object {
    $line = $_.Name
    $byG = ($_.Group | Group-Object g | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ' '
    $ys = ($_.Group | Where-Object g -eq 'head' | Sort-Object y | ForEach-Object { $_.y }) -join ','
    "line $line : $byG | headY: $ys"
  }
"--- text items ---"
$script:items | Where-Object kind -eq 'text' | ForEach-Object { "line $($_.src) : '$($_.d)' @ $($_.x),$($_.y)" }

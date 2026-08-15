# chord-names-bass: LP vs Lily# — note stacks, chord names above, labels below.
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
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d'); if ($d.Length -gt 30) { $d = $d.Substring(0, 30) }
    $script:items.Add([pscustomobject]@{kind='path'; x=[math]::Round($tx,2); y=[math]::Round($ty,2); t=$d; src=($href -replace '.*\.ly:', '')})
  } elseif ($node.LocalName -eq 'tspan') {
    $script:items.Add([pscustomobject]@{kind='text'; x=[math]::Round($tx,2); y=[math]::Round($ty,2); t=$node.InnerText; src=($href -replace '.*\.ly:', '')})
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty $href }
}
Walk $lpx.DocumentElement 0 0 $null
$dHead = 'M213 112c-48 0 -70 -40 -70 -84'
"--- LP heads by chord (src line:col), y in ss (staff top at 5.69? shown raw) ---"
$script:items | Where-Object { $_.kind -eq 'path' -and $_.t -eq $dHead } |
  Group-Object src | Sort-Object Name | ForEach-Object {
    $xs = ($_.Group | Sort-Object y | ForEach-Object { "$($_.x),$($_.y)" }) -join ' '
    "src $($_.Name) n=$($_.Count): $xs"
  }
"--- LP texts (chord names + labels + bar numbers) ---"
$script:items | Where-Object kind -eq 'text' | Sort-Object y, x | ForEach-Object { "'$($_.t)' @ $($_.x),$($_.y) src=$($_.src)" }
"--- LP other glyphs (triangle etc.) ---"
$script:items | Where-Object { $_.kind -eq 'path' -and $_.t -ne $dHead } | Group-Object t |
  Sort-Object Count -Descending | Select-Object -First 6 Count, @{n='xy';e={ ($_.Group | Select-Object -First 3 | ForEach-Object { "$($_.x),$($_.y)" }) -join ' ' }}, Name | Format-Table -AutoSize | Out-String -Width 200

# ---------- Lily# ----------
$src = Get-Content audit\lp-regression\lys\chord-names-bass.lys -Raw
$ls  = Get-Content audit\lpreg\chord-names-bass-lys.svg -Raw
$lineStarts = @(0); for ($i = 0; $i -lt $src.Length; $i++) { if ($src[$i] -eq "`n") { $lineStarts += ($i + 1) } }
function LineOf($pos) { for ($i = $lineStarts.Count - 1; $i -ge 0; $i--) { if ($pos -ge $lineStarts[$i]) { return $i + 1 } } 0 }
"--- Lily# heads (data-pos -> line) ---"
[regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*data-pos="(\d+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; pos=[int]$_.Groups[3].Value; g=$_.Groups[4].Value} } |
  Where-Object { $_.g -eq "$([char]0xE0FC)" } |
  Group-Object pos | Sort-Object { [int]$_.Name } | ForEach-Object {
    $ln = LineOf ([int]$_.Name)
    $xs = ($_.Group | Sort-Object y | ForEach-Object { "$($_.x),$($_.y)" }) -join ' '
    "pos $($_.Name) (line $ln) n=$($_.Count): $xs"
  }
"--- Lily# texts ---"
[regex]::Matches($ls, '<text x="([-\d.]+)" y="([-\d.]+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { "'$($_.Groups[3].Value)' @ $($_.Groups[1].Value),$($_.Groups[2].Value)" }

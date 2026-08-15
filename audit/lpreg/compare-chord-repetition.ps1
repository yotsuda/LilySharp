# chord-repetition family: LP vs Lily# structural comparison.
# Prints, per side: notehead columns (X-grouped, Y lists), accidental/paren
# glyphs, digits and texts — the reader pairs the columns.
param([string]$name = 'chord-repetition')
$ErrorActionPreference = 'Stop'
cd C:\MyProj\LilySharp\audit\lpreg

# ---------- LP ----------
[xml]$lpx = Get-Content "$name.svg" -Raw
$script:items = [System.Collections.Generic.List[object]]::new()
function Walk($node, $tx, $ty) {
  if ($node.NodeType -ne 'Element') { return }
  $t = $node.GetAttribute('transform')
  if ($t -match 'translate\(([-\d.]+),\s*([-\d.]+)\)') {
    $tx += [double]$Matches[1]; $ty += [double]$Matches[2]
  }
  if ($node.LocalName -eq 'path') {
    $d = $node.GetAttribute('d'); if ($d.Length -gt 26) { $d = $d.Substring(0, 26) }
    $script:items.Add([pscustomobject]@{kind='path'; x=[math]::Round($tx,2); y=[math]::Round($ty,2); t=$d})
  } elseif ($node.LocalName -eq 'tspan') {
    $script:items.Add([pscustomobject]@{kind='text'; x=[math]::Round($tx,2); y=[math]::Round($ty,2); t=$node.InnerText})
  } elseif ($node.LocalName -in 'polygon','line','rect') {
    $script:items.Add([pscustomobject]@{kind=$node.LocalName; x=[math]::Round($tx,2); y=[math]::Round($ty,2); t=''})
  }
  foreach ($c in $node.ChildNodes) { Walk $c $tx $ty }
}
Walk $lpx.DocumentElement 0 0
"=== LP paths grouped by shape (top 12 by count) ==="
$script:items | Where-Object kind -eq 'path' | Group-Object t | Sort-Object Count -Descending |
  Select-Object -First 12 | ForEach-Object {
    $xy = ($_.Group | Sort-Object x, y | ForEach-Object { "$($_.x),$($_.y)" }) -join ' '
    "n=$($_.Count) [$($_.Name)]: $xy"
  }
"=== LP texts ==="
$script:items | Where-Object kind -eq 'text' | Sort-Object x | ForEach-Object { "'$($_.t)' @ $($_.x),$($_.y)" }
"=== LP polygons/lines (beams, stems, brackets) ==="
$script:items | Where-Object { $_.kind -in 'polygon','line','rect' } | Group-Object kind |
  ForEach-Object { "$($_.Name) n=$($_.Count)" }

# ---------- Lily# ----------
$ls = Get-Content "$name-lys.svg" -Raw
"=== Lily# music glyphs by codepoint ==="
[regex]::Matches($ls, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"[^>]*>(&#x[0-9A-Fa-f]+;|[^<])</text>') |
  ForEach-Object {
    $g = $_.Groups[3].Value
    $cp = if ($g -match '&#x([0-9A-Fa-f]+);') { $Matches[1] } else { '{0:X4}' -f [int][char]$g }
    [pscustomobject]@{x=[double]$_.Groups[1].Value; y=[double]$_.Groups[2].Value; cp=$cp}
  } | Group-Object cp | Sort-Object Count -Descending | ForEach-Object {
    $xy = ($_.Group | Sort-Object x, y | ForEach-Object { "$($_.x),$($_.y)" }) -join ' '
    "U+$($_.Name) n=$($_.Count): $xy"
  }
"=== Lily# plain texts ==="
[regex]::Matches($ls, '<text (?![^>]*class="music")[^>]*x="([-\d.]+)" y="([-\d.]+)"[^>]*>([^<]+)</text>') |
  ForEach-Object { "'$($_.Groups[3].Value)' @ $($_.Groups[1].Value),$($_.Groups[2].Value)" }
"=== Lily# polygons (beams) / paths (slur, tie) ==="
"polygon n=$([regex]::Matches($ls,'<polygon').Count)  path n=$([regex]::Matches($ls,'<path').Count)  line n=$([regex]::Matches($ls,'<line').Count)"

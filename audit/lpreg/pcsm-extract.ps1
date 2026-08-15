# Read a Lily# SVG in the frame the LP grob dump uses: x as written, y as STAFF SPACES ABOVE
# THE CENTRE LINE (Y-up).  Unlike pcsil-extract.ps1 this also prints the NON-music <text>
# elements (the labels), because the book under measurement is about labels as much as about
# silences, and it resolves every data-pos back to line:col of the .lys so a glyph can be
# named by the token that made it.
#
# ⚠️ A music glyph with no data-pos is one with no source token of its own: augmentation dots
# and MULTI-MEASURE RESTS (symbol and bar-count number).  Printed with `(no-src)`, never dropped.
param([Parameter(Mandatory)][string]$Svg, [Parameter(Mandatory)][string]$Source)

$svgText = Get-Content $Svg -Raw
$src     = Get-Content $Source -Raw

$lineStarts = @(0)
for ($i = 0; $i -lt $src.Length; $i++) { if ($src[$i] -eq "`n") { $lineStarts += ($i + 1) } }
function Resolve-Pos([int]$off) {
  $ln = 0
  for ($i = 0; $i -lt $lineStarts.Count; $i++) { if ($lineStarts[$i] -le $off) { $ln = $i } else { break } }
  $col = $off - $lineStarts[$ln]
  $text = ($src.Substring($off, [Math]::Min(6, $src.Length - $off)) -replace "`r|`n", ' ')
  "{0}:{1} '{2}'" -f ($ln + 1), ($col + 1), $text.Trim()
}

$lines = [regex]::Matches($svgText, '<line x1="([\d.]+)" y1="([\d.]+)" x2="([\d.]+)" y2="\2"') |
         ForEach-Object { [double]$_.Groups[2].Value } | Sort-Object -Unique
if ($lines.Count -lt 5) { throw "expected 5 staff lines, found $($lines.Count)" }
$centre = $lines[2]
$space  = $lines[1] - $lines[0]

"# $([IO.Path]::GetFileName($Svg))  centre=$centre space=$space  staffLines=$($lines.Count)"
$rows = @()
# ⚠️ A LABEL carries no class attribute at all (`<text x=… font-style="italic">`), so a regex
# that requires class="…" reports a page with six labels on it as a page with none. Both forms
# are matched here and the classless ones are called `label`.
foreach ($m in [regex]::Matches($svgText, '<text (?:class="([^"]*)" )?x="([-\d.]+)" y="([-\d.]+)"([^>]*)>(.*?)</text>')) {
  $cls   = if ($m.Groups[1].Success -and $m.Groups[1].Value) { $m.Groups[1].Value } else { 'label' }
  $x     = [double]$m.Groups[2].Value
  $y     = [double]$m.Groups[3].Value
  $attrs = $m.Groups[4].Value
  $body  = $m.Groups[5].Value
  $srcM  = [regex]::Match($attrs, 'data-pos="(\d+)"')
  $pos   = if ($srcM.Success) { Resolve-Pos ([int]$srcM.Groups[1].Value) } else { '(no-src)' }
  if ($cls -eq 'music') {
    $glyph = ($body.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
  } else {
    $glyph = '"' + $body + '"'
  }
  $rows += [pscustomobject]@{ X = $x; Y = ($centre - $y) / $space; Cls = $cls; Glyph = $glyph; Src = $pos }
}
$rows | Sort-Object X | ForEach-Object { "{0,8:F2}  y={1,6:F2}  {2,-8} {3,-30} {4}" -f $_.X, $_.Y, $_.Cls, $_.Glyph, $_.Src }

"# --- bar lines (vertical) ---"
[regex]::Matches($svgText, '<line x1="([-\d.]+)" y1="([-\d.]+)" x2="\1" y2="([-\d.]+)"') |
  ForEach-Object { [double]$_.Groups[1].Value } | Sort-Object -Unique |
  ForEach-Object { "{0,8:F2}" -f $_ }

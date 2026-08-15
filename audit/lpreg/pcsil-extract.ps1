# Read a Lily# SVG's music glyphs in the same frame the LP grob dump reports:
# x as written, y as STAFF SPACES ABOVE THE CENTRE LINE (Y-up), so the two engines'
# numbers can be put side by side without a second conversion step.
# The centre line is taken from the staff lines actually drawn (3rd of five).
#
# ⚠️ A music glyph with NO data-pos is one with no source token of its own. There are two
# kinds and they are NOT noise:
#   - augmentation dots      (same y as their note, a little to the right)
#   - MULTI-MEASURE RESTS    (the church-rest symbol AND its bar-count number)
# An earlier version of this script required data-pos and so reported "no multi-measure
# rest was drawn" for scores that draw one. They are printed here with a `(no-src)` tag
# instead of being dropped; filter on the tag, never on the presence of the attribute.
param([string]$Path, [switch]$SourceOnly)

$svg = Get-Content $Path -Raw
$lines = [regex]::Matches($svg, '<line x1="([\d.]+)" y1="([\d.]+)" x2="([\d.]+)" y2="\2"') |
         ForEach-Object { [double]$_.Groups[2].Value } | Sort-Object -Unique
if ($lines.Count -lt 5) { throw "expected 5 staff lines, found $($lines.Count)" }
$centre = $lines[2]
$space  = $lines[1] - $lines[0]

"# $([IO.Path]::GetFileName($Path))  centre=$centre space=$space"
foreach ($m in [regex]::Matches($svg, '<text class="music" x="([-\d.]+)" y="([-\d.]+)"([^>]*)>(.*?)</text>')) {
  $attrs = $m.Groups[3].Value
  $srcM  = [regex]::Match($attrs, 'data-pos="(\d+)"')
  if ($SourceOnly -and -not $srcM.Success) { continue }
  $x  = [double]$m.Groups[1].Value
  $y  = [double]$m.Groups[2].Value
  $ss = ($centre - $y) / $space
  $ch = $m.Groups[4].Value
  $cp = ($ch.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
  $pos = if ($srcM.Success) { "pos=" + $srcM.Groups[1].Value } else { "(no-src)" }
  "{0,8:F2}  y={1,6:F2}  {2,-9} {3}" -f $x, $ss, $pos, $cp
}

\version "2.24.0"
% tuplet-number-slur-script twin (LP side) — R1 -> r1 both sides; explicit break
% keeps justify, so no ragged-right here (the usual paper rule inverts).
\paper { indent = 0 }
\relative c'
{
  r1 |
  \break
  \tuplet 3/2 { e8(-> e e) } r4 r2
}

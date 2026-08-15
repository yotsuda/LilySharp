\version "2.24.0"
% score1 twin (LP side). d'! -> des' both sides (a real accidental keeps the claim).
\paper { indent = 0 ragged-right = ##t }
{
  \repeat tremolo 16 { c'32 es' }
  \repeat tremolo 16 { a'32 cis'' }
  \repeat tremolo 16 { a'32 gisis' }
  <<
    \repeat tremolo 16 { a''32 bes'' }
    \\
    \repeat tremolo 16 { f'32 des' }
  >>
}

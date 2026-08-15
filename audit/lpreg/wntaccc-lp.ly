\version "2.24.0"
\paper { indent = 0 ragged-right = ##t }
{
  \time 4/2
  \repeat tremolo 16 { c'16 es' }
  \repeat tremolo 16 { a'16 cis'' }
  \repeat tremolo 16 { a'16 gisis' }
  <<
    \repeat tremolo 16 { a''16 bes'' }
    \\
    \repeat tremolo 16 { f'16 des' }
  >>
}

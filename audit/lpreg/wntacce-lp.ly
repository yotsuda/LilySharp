\version "2.24.0"
\paper { indent = 0 ragged-right = ##t }
{
  \time 4/1
  \repeat tremolo 16 { c'8 es' }
  \repeat tremolo 16 { a'8 cis'' }
  \repeat tremolo 16 { a'8 gisis' }
  <<
    \repeat tremolo 16 { a''8 bes'' }
    \\
    \repeat tremolo 16 { f'8 des' }
  >>
}

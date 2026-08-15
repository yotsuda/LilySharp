\version "2.26.0"
% Oracle: where does a slur end when the ')' is written on an EMPTY chord <>?
% <> occupies no time, so its moment is the moment of the FOLLOWING note.
% m1 = the question. m2 = control, the same slur closed on a real note.
\score {
  \new Staff {
    \time 4/4
    r4 e'8( g' <>) c''4 r4 |
    r4 e'8( g') c''4 r4 |
  }
  \layout { }
}

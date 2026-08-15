\version "2.26.0"

% tablature-dot-placement.ly 逐語 + paper 揃え(indent 0 + ragged-right) + \bar "|."
% (Lily# は常時終止線)。\relative は原書のまま(無引数=先頭 f' が絶対)。

\paper { ragged-right = ##t }

myMusic = \relative  {
  <f'\3 a c>4.
  <f\3 g d'>4.
  <f\3 a d>4 \bar "|."
}

\score {
  <<
    \new Staff {
      \clef "treble_8"
      \myMusic
    }
    \new TabStaff {
      \tabFullNotation
      \myMusic
    }
  >>
  \layout { indent = 0\mm }
}

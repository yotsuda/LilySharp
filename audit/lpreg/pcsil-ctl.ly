\version "2.26.0"
% Positive control for pcsil-a: the SAME music written as two plain voices, so the
% rest positions the part combiner produces can be told apart from the rest positions
% \voiceOne / \voiceTwo produce on their own.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score {
  \new Staff <<
    \new Voice { \voiceOne r4    r2 r8 r8 | r1 }
    \new Voice { \voiceTwo r8 r8 r2 r4    | r1 }
  >>
}

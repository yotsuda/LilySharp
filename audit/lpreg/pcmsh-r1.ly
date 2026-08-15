\version "2.26.0"
% Does \compressMMRests merge SEPARATELY WRITTEN whole-bar rests, or is one
% multi-measure rest one written event? This decides whether "a written R opens a run"
% is the rule, or only "a written R*N opens one".
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score { \new Staff { \compressMMRests \relative f' { R1 | R1 | R1 } } }
\score { \new Staff { \compressMMRests \relative f' { R1*3 } } }

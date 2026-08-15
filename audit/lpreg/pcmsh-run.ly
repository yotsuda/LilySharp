\version "2.26.0"
% Isolation, no \partCombine at all: ONE voice writing three multi-measure rests in a row.
% Does LilyPond keep the three written runs apart, or merge them into one 28-bar rest?
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score { \new Staff { \compressMMRests \relative f' { R1*8 | R1*16 | R1*4 } } }

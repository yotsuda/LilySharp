\version "2.26.0"
% Ground truth for Fixtures/test/multi-measure-rest-single.lys, whose comment claims a one-bar
% multi-measure rest "must hang from the 4th line like a real whole rest".  The same four bars
% that fixture writes, uncompressed, in a neutral voice.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score { \new Staff \relative f' { g4 a b c | r1 | g4 a b c | R1*1 | g4 a b c | R1*2 | g4 a b c | R1*3 | g4 a b c | } }

% and again with \compressMMRests, which is what turns R1*N into ONE symbol group
\score { \new Staff \relative f' { \compressMMRests { g4 a b c | r1 | g4 a b c | R1*1 | g4 a b c | R1*2 | g4 a b c | R1*3 | g4 a b c | } } }

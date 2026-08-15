\version "2.26.0"
% score 1 of input/regression/part-combine-mmrest-shared.ly, verbatim.
% texidoc: "Multi-measure rests do not have to begin and end simultaneously to be combined."
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score { <<
  \compressMMRests
  \partCombine
    \relative f' { R1*8  | R1*16     | R1*4 }
    \relative f' { R1*16     | R1*8  | R1*4 }
>> }

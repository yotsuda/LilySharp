\version "2.26.0"
% Score 2 of input/regression/part-combine-mmrest-shared.ly, one score per file so the
% dump records cannot be attributed to the wrong book.
%
% Part one interrupts its own rests with an ORDINARY whole rest carrying a text
% (r1^"r") at bar 8, against part two's ongoing R1*16. The question this book asks is
% what the shared voice prints where a normal rest meets a multi-measure rest.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score { <<
  \compressMMRests
  \partCombine
    \relative f' { R1*8  | r1^"r" | R1*15     | R1*4 }
    \relative f' { R1*16              | R1*8  | R1*4 }
>> }

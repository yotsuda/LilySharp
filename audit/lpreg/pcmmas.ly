\version "2.26.0"
% input/regression/part-combine-mmrest-after-apart-silence.ly, verbatim.
% texidoc: "Normal rests are preferred over multi-measure rests.  A multi-measure rest
% beginning in one part in the middle of a multi-measure rest in the other part appears
% as expected."
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score { <<
  \new Staff {
    \partCombine
      \relative f' { r2 r2 | R1 }
      \relative f' { R1*2       }
  }
  \new Staff {
    \partCombine
      \relative f' { R1*2       }
      \relative f' { r2 r2 | R1 }
  }
>> }

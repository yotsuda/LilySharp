\version "2.26.0"
% Score 3 of input/regression/part-combine-mmrest-shared.ly.
%
% The SAME music as score 2 with the two parts exchanged (and the text flipped to _"r"
% to match the voice it now sits in). The pair is therefore near-identity: whatever the
% rule is, the two books must engrave the same rests in the same places, and only the
% text's direction may differ. Any other difference between pcmsh-b and pcmsh-c is a
% defect, in LilyPond's answer or in the twin's.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score { <<
  \compressMMRests
  \partCombine
    \relative f' { R1*16              | R1*8  | R1*4 }
    \relative f' { R1*8  | r1_"r" | R1*15     | R1*4 }
>> }

\version "2.26.0"
% input/regression/part-combine-silence-mixed.ly, verbatim body.
% texidoc: "Different kinds of silence are not merged into the shared voice even if they begin
% and end simultaneously; however, when rests and skips are present in the same part, the skips
% are ignored."
% The second staff is the first with the two parts swapped — the book is its own control.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\score { <<
  \new Staff {
    \partCombine
      \relative f' { R1^"R" | s1^"s" | r1^"r" | << R1 s1 s4 >> | << r1 s2 s4 >> }
      \relative f' { r1_"r" | R1_"R" | s1_"s" | << s4 s1 R1 >> | << s4 s2 r1 >> }
  }
  \new Staff {
    \partCombine
      \relative f' { r1^"r" | R1^"R" | s1^"s" | << s4 s1 R1 >> | << s4 s2 r1 >> }
      \relative f' { R1_"R" | s1_"s" | r1_"r" | << R1 s1 s4 >> | << r1 s2 s4 >> }
  }
>> }

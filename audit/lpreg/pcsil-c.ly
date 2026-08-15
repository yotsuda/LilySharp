\version "2.26.0"
% score 3 of input/regression/part-combine-silence.ly, verbatim.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

% mmrest and rest of different durations beginning simultaneously
\score {
  \partCombine
    \relative { r4 f'2. | R1 }
    \relative { R1     | r4 d'2. }
}

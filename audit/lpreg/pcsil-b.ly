\version "2.26.0"
% score 2 of input/regression/part-combine-silence.ly, verbatim.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

% rests of different durations beginning simultaneously, followed by
% solo then a2.
\score {
  \partCombine
    \relative { r4   f'2. | r8 f e2. }
    \relative { r8 d' f2. | r4   e2. }
}

\version "2.26.0"
% score 1 of input/regression/part-combine-silence.ly, verbatim.
% texidoc: "Rests must begin and end simultaneously to be merged into the shared voice."
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

% rests of different durations beginning simultaneously, followed by
% unisilence
\score {
  \partCombine
    \relative f' { r4    r2 r8 r8 | r1 }
    \relative f' { r8 r8 r2 r4    | r1 }
}

\version "2.19.21"

% Frame-aligned twin of input/regression/chord-repetition-relative.ly
% (verbatim body + comparison paper).

\paper { indent = 0 ragged-right = ##t }

{
  <c''' d'' g''>4^"absolute" q q q
  \relative { <c''' d, g>4^"relative" q q q }
}

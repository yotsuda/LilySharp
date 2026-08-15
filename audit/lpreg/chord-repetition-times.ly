\version "2.19.21"

% Frame-aligned twin of input/regression/chord-repetition-times.ly
% (verbatim body + comparison paper).

\paper { indent = 0 ragged-right = ##t }

\relative {
  <c' e g>4 r <c e g>2 ~ |
  \tuplet 3/2 { <c e g>4 q q } \tuplet 3/2 { q q q } |
}

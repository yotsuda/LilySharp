\version "2.23.12"

% Frame-aligned twin of input/regression/fermata-dot-position.ly, block 2
% (accents): same fermata-family substitution as block 1.

\paper { indent = 0 ragged-right = ##t }

\relative c''' {
  \tempo 4 = 60
  a4->
  a4->\fermata
  a4->\shortfermata
  a4->\longfermata
  a4->\shortfermata
  a4->\longfermata
  a4->\shortfermata
  a4->\longfermata \bar "|."
}

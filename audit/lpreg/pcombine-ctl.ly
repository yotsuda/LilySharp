\version "2.26.0"
% Control: the SAME two parts as plain polyphony, with no combining.
one = { c'4 d' e' f' | g'2 g' | R1 | c'4 e' g' e' | }
two = { c'4 d' e' f' | R1 | g2 g | g4 g g g | }

\score { \new Staff << \one \\ \two >> \layout { } }

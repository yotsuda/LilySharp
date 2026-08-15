\version "2.26.0"
% What \partCombine actually DOES, in four bars:
%   bar 1 = unison      -> ONE notehead, ONE stem, "a2" printed
%   bar 2 = voice 2 rests -> "Solo" printed, single voice, stems neutral
%   bar 3 = voice 1 rests -> "Solo II" printed
%   bar 4 = different    -> two voices, stems up/down (the << \\ >> look)
one = { c'4 d' e' f' | g'2 g' | R1 | c'4 e' g' e' | }
two = { c'4 d' e' f' | R1 | g2 g | g4 g g g | }

\score { \new Staff \partCombine \one \two \layout { } }

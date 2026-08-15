\version "2.26.0"
% Does a DURATION written on an empty chord change the running default?
% If <>4 leaves it alone, the two c's stay eighths and the bar is 4/4.
% If it sets the default to a quarter, the bar overflows and LilyPond says so.
\score { \new Staff { \time 4/4 r4 e'8 g' <>4 c'' c'' r4 | } \layout { } }

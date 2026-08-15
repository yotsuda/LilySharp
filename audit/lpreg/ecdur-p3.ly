\version "2.26.0"
% P3: <>4 with EXPLICIT eighths after it, so the running default cannot matter.
% A complaint here means the empty chord itself consumed the quarter.
\score { \new Staff { \time 4/4 e'8 g' <>4 c''8 c''8 c''8 c''8 c''8 c''8 | } \layout { } }

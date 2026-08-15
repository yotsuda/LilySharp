\version "2.26.0"
% POSITIVE CONTROL for ecdur-lp: a deliberately overfull bar. If the detection in
% ecdur-lp is real, this one MUST produce a bar-check complaint.
\score { \new Staff { \time 4/4 r4 e'8 g' c''4 c''4 r4 | } \layout { } }

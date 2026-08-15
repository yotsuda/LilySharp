\version "2.26.0"
% (a) LP's own spelling in part-combine-3voices.ly: the combined pair is forced UP.
soprano = { d''2 f'' g'' }
alto = { a' c''4 d'' e''2 }
basso = { d'4 e' f' g' g'2 }
\score { \new Staff << \partCombineUp \soprano \alto \\ \basso >> \layout { } }

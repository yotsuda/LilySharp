\version "2.26.0"
% (b) the same, but with plain \partCombine — is the Up variant redundant with the
% voice-1 position inside << ... \\ ... >> ?
soprano = { d''2 f'' g'' }
alto = { a' c''4 d'' e''2 }
basso = { d'4 e' f' g' g'2 }
\score { \new Staff << \partCombine \soprano \alto \\ \basso >> \layout { } }

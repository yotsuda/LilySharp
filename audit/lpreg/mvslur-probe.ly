\version "2.26.0"
% Mechanism probe (not a twin): voiceTwo down-slur on stem-down quarters —
% does LP's slur clear the stem TIPS (edge stem_ term) or hug the heads
% (paying the /5-discounted stem penalty)? Mirrors test/multivoice-spanners.
% Octaves: Lily# absolute c = LP c', so the fixture's c''/e'/g' are c'''/e''/g'' here.
\score {
  \new Staff <<
    \new Voice { \voiceOne c'''4 d''' e''' f''' | g'''4 a''' b''' c'''' | }
    \new Voice { \voiceTwo e''4 g'' g''( e'') | f''4 f''~ f'' f'' | }
  >>
  \layout { indent = 0\mm }
}

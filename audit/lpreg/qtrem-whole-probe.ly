\version "2.24.0"
% 監査 probe: 全音符和音の単独 tremolo (stemless 枝の対照)。
\paper { indent = 0 }
\score {
  \fixed c' {
    \time 4/4
    \repeat tremolo 32 { <c e>32 }
  }
  \layout { ragged-right = ##t }
}


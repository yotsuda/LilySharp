\version "2.24.0"
% laissez-vibrer-chords.ly の比較用 twin。
% 主張: \laissezVibrer は和音の個々の音にも効く (member 単位 + ^/_ 向き強制)。
% 自然折返し本 = ragged-right 有り。
\paper { indent = 0 }
\score {
  \relative {
    <d'-\laissezVibrer g>1
    <d^\laissezVibrer g_\laissezVibrer>1
  }
  \layout { ragged-right = ##t }
}

\version "2.24.0"
% lyric-extender-completion.ly の比較用 twin。
% 主張: 音符が歌詞より多くても LyricExtender は正しい場所で終わる。
\paper { indent = 0 }
\score {
  <<
    \new Staff \relative {
      \new Voice = "upper" {
        \voiceTwo
        g'1( |
        c,) |
        d |
      }
    }
    \new Lyrics \lyricsto "upper" { Ah __ }
  >>
  \layout { ragged-right = ##t }
}

\version "2.24.0"
% lyric-hyphen-grace.ly の比較用 twin。
% 主張: 行頭の grace の下に hyphen を刷らない (grace の主音が新しい音節を始めるとき)。
% 明示 \break → LP twin ragged-right 無し。
\paper { indent = 0 }
<<
  \new Staff {
    \appoggiatura f'8 g'2 g'( | \break
    \appoggiatura f'8 g'2) \appoggiatura f'8 g'2 | \break
    \appoggiatura f'8 g'2 g' | \break
    g'2 g' |
  }
  \addlyrics {
    \lyricmode {
      bla -- bla -- bla -- bla -- bla -- bla -- bla
    }
  }
  \new Staff {
    g'2 g' |
    g'2 g' |
    g'2 g' |
    g'2 g' |
  }
  \addlyrics {
    \lyricmode {
      bla -- bla -- bla -- bla -- bla -- bla -- bla -- bla
    }
  }
>>

\version "2.24.0"
% lyrics-pass-under-bar.ly の比較用 twin。
% 主張: 長い歌詞は小節線の下を通ってよい。
% 自然折返し本 → ragged-right 有り。
\paper { indent = 0 ragged-right = ##t }
\relative { c'''2 c c c }
\addlyrics { foo bar foooooooo bar }

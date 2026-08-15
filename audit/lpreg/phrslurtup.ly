\version "2.24.0"
% phrasing-slur-tuplet.ly の比較用 twin。
% 主張: (phrasing) slur は tuplet 番号と衝突しない。
% 両側置換: \( \) → ( ) (Lily# に phrasing slur 綴りなし・衝突回避は同機構)。
% \voiceOne は残す (符尾上+slur上=番号と同側、が主張の前提)。
\paper { indent = 0 ragged-right = ##t }
\relative {
  \voiceOne
  \tuplet 3/2 {
    c''8( b c
  }
  a2.)
}

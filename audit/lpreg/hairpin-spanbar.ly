\version "2.24.0"
% hairpin-span-bar.ly の比較用 twin (score 1 のみ)。
% score 2/3 は ^\< (上側 hairpin) が要る = Lily# は hairpin 常時下の意図した設計
% で書けない (台帳 notes に開示)。score 1 が主張の両半分を担う:
%   系1 = SpanBar 有り → hairpin は span bar の手前で止まる
%   系2 = 下譜が hara-kiri で消え SpanBar 無し → hairpin は行末まで伸びる
% 置換: 終端 \! → \f (綴り無し)。明示 \break → LP 側 ragged-right 無し。
\paper { indent = 0 }
\score {
  \new GrandStaff <<
    \new Staff \relative { a'\< a a a \break a a a a \break a a a a\f }
    \new Staff \relative { a'4 a a a s1 a4 a a a }
  >>
  \layout {
    \context {
      \Staff
      \RemoveEmptyStaves
    }
  }
}

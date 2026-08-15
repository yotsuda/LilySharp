\version "2.24.0"
% input-order-alignment.ly の比較用 twin。
% 主張: 吊り2度和音に付く lyrics/dynamics/textscript/articulation の X 揃えは
% 入力順 (<b' c''> vs <c'' b'>) に依らず main notehead (符尾終端側の頭) 基準。
% 自然折返し本 = ragged-right 有り (第105 規約)。
\paper { indent = 0 }
\score {
  <<
    \new Staff {
      <b' c''>2 s
      <b' c''>\f s
      <b' c''>^"Text" s
      <b' c''>-! s
    }
    \addlyrics { blah }
    \new Staff {
      <c'' b'>2 s
      <c'' b'>\f s
      <c'' b'>^"Text" s
      <c'' b'>-! s
    }
    \addlyrics { blah }
  >>
  \layout { ragged-right = ##t }
}

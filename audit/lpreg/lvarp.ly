\version "2.24.0"
% laissez-vibrer-arpeggio.ly の比較用 twin。
% 主張: l.v. tie は次の和音の arpeggio 記号と衝突しない。
% 自然折返し本 = ragged-right 有り。
\paper { indent = 0 }
\score {
  {
    <e'>\laissezVibrer <f  f'> \arpeggio
    <e'>\laissezVibrer <g  f'> \arpeggio
    <e'>\laissezVibrer <a  f'> \arpeggio
    <e'>\laissezVibrer <b  f'> \arpeggio
  }
  \layout { ragged-right = ##t }
}

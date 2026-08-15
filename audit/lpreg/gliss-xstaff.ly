\version "2.24.0"
% glissando-cross-staff.ly の比較用 twin。
% 原本そのまま + paper 揃え (indent 0 + ragged-right) + \bar "|."
% (Lily# は常時終止線を刷るので LP twin に明示する規約)。
\paper { indent = 0 ragged-right = ##t }
\new PianoStaff <<
\new Staff = "right" {
  e'''2\glissando
  \change Staff = "left"

  a,,\glissando
  \change Staff = "right"
  b''8
  \bar "|."
}
\new Staff = "left" {
  \clef bass
  s1 s8
}
>>

\version "2.26.0"

% tablature-grace-notes.ly 逐語 + paper 揃え。\parenthesize は両側落とし
% (Lily# 綴りなし・claim = grace フレット数字のサイズ)。

\paper { ragged-right = ##t }

gracenotes = \relative {
   c4 d e f
   \grace e8 c4 d e f
   \grace e8 c4 d e f
   \appoggiatura e8 c4 d e f
   \acciaccatura e8 c4 d e f
   \bar "|."
}

\context StaffGroup <<
  \context Staff <<
    \clef "G_8"
    \gracenotes
  >>
  \context TabStaff <<
    \gracenotes
  >>
>>

\layout { indent = 0\mm }

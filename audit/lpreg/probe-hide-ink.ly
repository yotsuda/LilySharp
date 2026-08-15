\version "2.19.21"
\paper { indent = 0 ragged-right = ##t }
% Which inks does \hideNotes remove? m1: hidden 8th (flag?), hidden chord (stem?).
% m2: visible beamed pair (control), hidden beamed pair (beam? stems?).
\relative {
  \hideNotes c'8 d <e g>4 \unHideNotes r4 |
  c8[ d] \hideNotes e8[ f] \unHideNotes r2 \bar "|."
}

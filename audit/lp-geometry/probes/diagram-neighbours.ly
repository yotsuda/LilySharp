\version "2.26.0"
%
% A CHORD DIAGRAM AND THE COLUMNS BESIDE IT
%
% The twin writes a diagram as a TextScript with \textLengthOn's two tweaks: extra-spacing-width
% (-0.0 . 0.4) and extra-spacing-height (-inf . +inf). So its box joins its column's skyline at
% every height and meets whatever stands in the columns beside it — a bar line, a note — and a
% diagram on a bar's first column is in that bar line's Staff_spacing min_dist, lifted by 0.3
% (lily/staff-spacing.cc:210-215). x02010's box is (-1.6895 . 3.9422) about the head's origin.
% Lily# rodded a diagram only against another diagram and the bar edges (without the bar's own
% 0.1) until session 835.
%
% DN1: x02010 on the first quarter after a bar line — bar line ink right -> head 2.089495
%      (0.1 + 1.6895 + 0.3), the head -> the next quarter's 4.542156 (3.942 + 0.4 + 0.1 + 0.1).
% DN2: x02010 on a bar's last quarter — the quarter before -> it 3.193695, it -> the bar line
%      4.542156.
% DN3: DN2 with 1;3;3;2;1;1; — no X / O row, a dot on both end strings, so the box ends at the
%      dots' rings, (-1.665 . 3.885) about the head.
%
% Bodies are what `lysc ly` emitted for the .lys books DN1/DN2 (LpGeometryProbes.cs), indent 0
% and ragged-right as the .lys says `paper { raggedRight }`.
%
% Output: PROBEDN <tag> <grob> x=<refpoint x in the system>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
}

#(define (dump tag name)
   (lambda (grob)
     (format #t "\nPROBEDN ~a ~a x=~a\n" tag name
             (ly:grob-relative-coordinate grob (ly:grob-system grob) X))))

#(define (probe tag)
   #{
     \override NoteHead.after-line-breaking = #(dump tag "HEAD")
     \override Staff.BarLine.after-line-breaking = #(dump tag "BAR")
   #})

mOne = \fixed c' {
  \time 4/4
  \mark \markup \box "A" e'1 |
  c'4-\tweak extra-spacing-width #'(-0.0 . 0.4) -\tweak extra-spacing-height #'(-inf.0 . +inf.0) ^\markup \fret-diagram-terse "x;o;2;o;1;o;" c' c' c' |
}

\score {
  \new Staff { \clef "treble" $(probe "DN1") \mOne }
  \layout { indent = 0 ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}

mTwo = \fixed c' {
  \time 4/4
  \mark \markup \box "A" c'4 c' c' c'-\tweak extra-spacing-width #'(-0.0 . 0.4) -\tweak extra-spacing-height #'(-inf.0 . +inf.0) ^\markup \fret-diagram-terse "x;o;2;o;1;o;" |
  e'1 |
}

\score {
  \new Staff { \clef "treble" $(probe "DN2") \mTwo }
  \layout { indent = 0 ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}

mThree = \fixed c' {
  \time 4/4
  \mark \markup \box "A" c'4 c' c' c'-\tweak extra-spacing-width #'(-0.0 . 0.4) -\tweak extra-spacing-height #'(-inf.0 . +inf.0) ^\markup \fret-diagram-terse "1;3;3;2;1;1;" |
  e'1 |
}

\score {
  \new Staff { \clef "treble" $(probe "DN3") \mThree }
  \layout { indent = 0 ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}

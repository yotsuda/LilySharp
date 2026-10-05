\version "2.26.0"
%
% WHAT A LINE START OWES THE FIRST COLUMN'S DIAGRAM, AND A TAB UNDER THE STAFF
%
% The prefix's Staff_spacing wish is floored at 0.3 + min_dist (lily/staff-spacing.cc:210-215),
% and min_dist reaches every grob of the first column (Paper_column::minimum_distance). Lily#
% floored the FIXED distance at the opening measure's own bar-line spring minimum instead until
% session 834 (ownFixedFloor), and drew a markup diagram's grid centred on the head.
%
% LSD: `e'4@chord(C x32010)` opening a first line. The diagram is a TextScript with
%      \textLengthOn's two tweaks, so its box is in min_dist; its stencil is aligned at
%      align-dir -0.4 (scm/fret-diagrams.scm make-fret-diagram) and its origin is the head's
%      (TextScript's alignments are #f). Lily# drew the grid 0.45 left of this.
% LST: a staff over a 4-string bass TAB, a continuation line in A major opening on
%      `<e gis>4.` — the floor over-reserved the fret digits' column by 1.29.
%
% Bodies are what `lysc ly` emitted for the .lys books LSD/LST (LpGeometryProbes.cs),
% indent 0 and ragged-right as the .lys says `paper { raggedRight }`.
%
% Output: PROBELSD <tag> <grob> x=<refpoint x in the system> sys=<system rank>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
}

#(define (dump tag name)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBELSD ~a ~a x=~a sys=~a\n" tag name
               (ly:grob-relative-coordinate grob sys X)
               (ly:grob-property sys 'rank 0)))))

#(define (probe tag)
   #{
     \override Staff.Clef.after-line-breaking = #(dump tag "CLEF")
     \override NoteHead.after-line-breaking = #(dump tag "HEAD")
   #})

mInlineChords = \chordmode {
  c1 |
}

m = \fixed c' {
  \time 4/4
  \key c \major
  \mark \markup \box "A" e'4-\tweak extra-spacing-width #'(-0.0 . 0.4) -\tweak extra-spacing-height #'(-inf.0 . +inf.0) ^\markup \fret-diagram-terse "x;3;2;o;1;o;" d' e' f' |
}

\score {
  <<
    \new ChordNames \mInlineChords
    \new Staff { \clef "treble" $(probe "LSD") \m }
  >>
  \layout { indent = 0 ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}

bVarTwo = \fixed c' {
  \key a \major
  \time 4/4
  \mark \markup \box "A" r1 |
  \break
  <e gis>4. <e a>8 ~ <e a>2 |
}

\score {
  <<
    \new Staff { \clef "bass" $(probe "LST") \bVarTwo }
    \new TabStaff \with { stringTunings = #bass-four-string-tuning } { \transpose c c, \bVarTwo }
  >>
  \layout { indent = 0 ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}

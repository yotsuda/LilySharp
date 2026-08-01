\version "2.26.0"
%
% WHERE DOES A BEAM SIT ON A TAB STAFF?
%
% Every other beam point in the ledger is on a five-line staff of unit space. A TAB staff is
% not a different beam — it is a different STAFF, and everything LilyPond's quanter reads is
% expressed in the staff's own spaces. A TabStaff's space is 1.5, so LilyPond re-tunes exactly
% two beam constants for it and leaves every LENGTH alone (ly/engraver-init.ly:1234-1246,
% under the comment "TabStaff increase the staff-space, which in turn increases beam thickness
% and spacing; beams are too big. We have to adjust the beam settings"):
%
%     \override Beam.beam-thickness  = #0.32      % = 0.48/1.5 — the ABSOLUTE thickness kept
%     \override Beam.length-fraction = #0.62
%
% The quanter then divides both thicknesses by the staff space again
% (lily/beam-quanting.cc:232-234), so:
%
%                       notation   tab (space 1.5)
%     beam_thickness_     0.48        0.32
%     line_thickness_     0.10        0.0667
%     sit / hang quant    0.19        0.12667
%     beam_translation_   0.81        0.480667      <- (2*0.62 + 0.0667*0.62 - 0.32)/2
%     staff_radius_       2.0         1.5           <- (line_count - 1)/2
%
% ⚠️ THE STRINGS ARE ONE STAFF SPACE APART IN THAT FRAME. A four-string tab is positions
% (3, 1, -1, -3) — the 1.5 lives in the staff's space, not in the positions.
%
% The music is LilySharp.Tests/Fixtures/test/tab-string-pinned's, tab staff only, and every
% note names its STRING (\4 \3 \2 \1). That is what makes it comparable at all: the two
% engines' string allocators do not agree, and a beam sits on the string — a book that leaves
% the choice open is comparing two different fingerings, not two beams.
%
% Bar 1 runs 4->1 then 1->4, so both slopes are exercised; bar 2 is flat on the lowest string
% (stems up) and flat on the highest (stems down), which is where the staff's own LINE GRID
% decides rather than its translation.
%
% ⚠️ The body came out of `lysc ly`, not a hand transcription.
%
% Output: PROBET BEAM pos=<positions> lines=<line-count>

\paper { indent = 0 ragged-right = ##t }

\layout {
  \context {
    \Score
    \override Beam.after-line-breaking =
      #(lambda (grob)
         (let* ((ss (ly:grob-object grob 'staff-symbol)))
           (format #t "\nPROBET BEAM pos=~a lines=~a\n"
                   (ly:grob-property grob 'positions)
                   (if (ly:grob? ss) (ly:grob-property ss 'line-count) -1))))
  }
}

bl = \fixed c' {
  \time 4/4
  \key c \major
  a,,8\4 d,\3 g,\2 c\1 c\1 g,\2 d,\3 a,,\4 |
  a,,8\4 a,,\4 a,,\4 a,,\4 c\1 c\1 c\1 c\1 |
}

\score {
  \new TabStaff \with { stringTunings = #bass-four-string-tuning }
    { \tabFullNotation \transpose c c, \bl }
  \layout {}
}
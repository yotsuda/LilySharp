\version "2.26.0"
%
% WHY ARE A KNEED BEAM'S NOTE COLUMNS UNEVENLY SPACED?
%
% This is the thing that blocks the beam quanter's frame fix (see beam-stem-x.ly and the
% beam.quant.knee.* entries): LilyPond's stems for `c'8 c' c' c'''` come out EVENLY spaced
% while its COLUMNS do not, and Lily# does the opposite, so the quanter's least squares fits
% a slope off by a factor of two whichever frame it runs in.
%
% Three candidate causes, and this book separates them:
%   (a) ledger lines widen a column        -> FALSIFIED by book B
%   (b) the stem's direction enters the spacing -> CONFIRMED by book D
%   (c) a minimum-distance rod             -> not needed to explain anything here
%
% Output: SP <name> colX=.. colExt=.. headExt=.. stemX=.. dir=..
%
% What it prints (2.26.0), as COLUMN gaps:
%   A knee      2.5042 / 2.5042 / 3.6784   <- the last pair, up-stem then down-stem, is wide
%   B noknee    2.5042 / 2.5042 / 2.5042   <- four c''' : ledgers ABOVE on every column, and
%                                             the spacing is perfectly even. Ledger lines are
%                                             NOT the cause.
%   C plain     2.5042 / 2.5042 / 2.5042   <- control, four b' inside the staff
%   D mixeddir  3.6784 / 1.8042 / 3.6784   <- b' throughout, NO ledger anywhere, directions
%                                             forced to alternate: the uneven gaps come back.
%                                             It is the STEM DIRECTION, not the pitch.
%
% The mechanism is lily/note-spacing.cc:111 Note_spacing::stem_dir_correction. When the two
% stems point opposite ways it forks (:288-302):
%   both stems in the SAME beam -> knee_correction (:117-137)
%        = -note_head_width * direction(right stem) * knee-spacing-correction
%          (scm/define-grobs.scm:2653, default 1.0; note_head_width is the right stem's
%           support head extent[RIGHT], else the spacing increment)
%   otherwise                   -> different_directions_correction (:139-160)
%          = min(overlap/7, 1) * direction(left stem) * stem-spacing-correction
%
% THE +1.1742 IS THE WHOLE TERM, AND IT IS NOT note_head_width. knee_correction subtracts the
% stem's own thickness from the head extent before scaling (:131 `note_head_width -=
% Stem::thickness (right_stem)`):
%   head extent[RIGHT] 1.304200   (the LILC box of noteheads.s2 — LilyPond's `extent` is the
%                                  declared box, not the outline; Lily# has the same number in
%                                  GlyphMetricsGenerated.NoteheadBlack.Right)
%   - Stem::thickness  0.130000   (lily/stem.cc:909-913 = Stem.thickness * line_thickness;
%                                  scm/define-grobs.scm:3469 (thickness . 1.3) and
%                                  scm/paper.scm:52-66 calc-line-thickness = 0.5pt = 0.1 ss
%                                  at the default 20pt staff)
%   = 1.174200                    = 3.6784 - 2.5042, the wide gap of book A, exactly.
% An earlier reading called the term +1.3042 and left 0.13 unexplained; it was the stem.
%
% E/F/G BELOW ARE THE FALSIFIER, and the predictions were written before the run.
% They perturb knee-spacing-correction over book D's music, where BOTH signs of the term are
% visible at once. D's middle gap is 1.8042 = 2.5042 - 0.7, which the symmetric term does NOT
% explain (it predicts 2.5042 - 1.1742 = 1.3300), so one of these is true:
%   FLOOR      the correction IS symmetric and the middle gap sits on the spring's minimum
%              distance (the rod), which no correction can push through
%              -> E 2.5042 x3 ; F 3.0913 / 1.9171 / 3.0913 ; G 4.8526 / 1.8042 / 4.8526
%                 i.e. the middle gap FALLS with F and then SATURATES at D's own 1.8042
%   ASYMMETRY  the down->up direction earns a different, smaller term than up->down
%              -> F 3.0913 / 2.1542 / 3.0913 ; G 4.8526 / 1.1042 / 4.8526
%                 i.e. the middle gap keeps falling in proportion, never saturating
% The two answers select different work: FLOOR means the port is the term as written and the
% middle gap is the ROD model's business (Lily# already ports the rod); ASYMMETRY means the
% sign reading above is wrong and the port must not be written from it at all.
%
% MEASURED (2.26.0) — FLOOR, and all three books landed on the predicted numbers:
%   E knee0   2.5042 / 2.5042 / 2.5042      the term is the WHOLE difference from the control
%   F knee.5  3.0913 / 1.9171 / 3.0913      +-0.5871 = +-0.5 * 1.1742, both signs, unfloored
%   G knee2   4.8526 / 1.8042 / 4.8526      +2.3484 up->down; down->up SATURATES at 1.8042
% So the term is symmetric, D's narrow gap is the spring's minimum distance, and 1.8042 is a
% rod reading (head 1.3042 + 0.5), not a correction. E also kills the alternative fork: the
% different-directions branch does not read this property, so a book that changes with it is
% in the knee branch.
%
% The dump also pins the frame numbers this file's sibling (beam-stem-x.ly) reports:
% headExt is always colX + 1.3042 wide, and stemX - colX is 1.2392 up / 0.0650 down.
%
% ⚠️ Lily# ports the OTHER TWO branches and not this one. SpacingRules.CalculateStemCorrection
% says so in its own remarks ("the knee special case (:289-292) is not applied"), and
% NoteSpacingParameters.KneeSpacingCorrection is declared, asserted at 1.0 by
% SpringRodModelTests, and read by NOTHING in production — audit/property_coverage.csv
% classifies it "Mention". A declaration with zero observers, and it is exactly the term
% that would make these columns uneven.
\paper { indent = 0 ragged-right = ##t }

#(define (dump name)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (for-each
        (lambda (s)
          (let* ((col (ly:grob-parent s X))
                 (heads (ly:grob-array->list (ly:grob-object s 'note-heads)))
                 (h (if (null? heads) #f (car heads))))
            (format #t "SP ~a colX=~a colExt=~a headExt=~a stemX=~a dir=~a\n"
                    name
                    (ly:grob-relative-coordinate col sys X)
                    (ly:grob-extent col sys X)
                    (if h (ly:grob-extent h sys X) '())
                    (ly:grob-relative-coordinate s sys X)
                    (ly:grob-property s 'direction))))
        (ly:grob-array->list (ly:grob-object grob 'stems))))))

probe =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump name) }
      { $music } #})

% A: the kneed bar of showcase/05-special-techniques. Ledgers below AND far above.
\score { \probe "A knee" { \time 4/4 c'8 c' c' c''' r2 } }
% B: heavy ledgers on EVERY column, one direction. Isolates the ledger hypothesis.
\score { \probe "B noknee" { \time 4/4 c'''8 c''' c''' c''' r2 } }
% C: inside the staff, one direction. The control both of the above are read against.
\score { \probe "C plain" { \time 4/4 b'8 b' b' b' r2 } }
% D: inside the staff, no ledger, directions forced to alternate. Isolates the direction.
\score { \probe "D mixeddir" { \time 4/4 \stemUp b'8 \stemDown b' \stemUp b' \stemDown b' r2 } }
% E/F/G: D's music with the term itself turned down / up. E also falsifies the OTHER branch:
% different_directions_correction does not read knee-spacing-correction, so if E still shows
% uneven gaps the fork is not going where this file says it goes.
\score { \probe "E knee0"  { \time 4/4 \override NoteSpacing.knee-spacing-correction = #0
                             \stemUp b'8 \stemDown b' \stemUp b' \stemDown b' r2 } }
\score { \probe "F knee.5" { \time 4/4 \override NoteSpacing.knee-spacing-correction = #0.5
                             \stemUp b'8 \stemDown b' \stemUp b' \stemDown b' r2 } }
\score { \probe "G knee2"  { \time 4/4 \override NoteSpacing.knee-spacing-correction = #2
                             \stemUp b'8 \stemDown b' \stemUp b' \stemDown b' r2 } }

\version "2.26.0"
%
% WHAT THE GRACE→MAIN STEP INSIDE A CUE IS MADE OF.
%
% WHY THIS EXISTS. audit/lp-geometry cue.grace.column.to-main stands at +0.561116717 and its
% `why` says it is not decomposed. lily/spacing-spanner.cc:366-374 supplies the missing half of
% note-spacing.cc:77: when the LEFT column is a grace column the `increment` swapped out of the
% duration ideal is not SpacingSpanner's 1.2 but GraceSpacing's own, and GraceSpacing declares
% (spacing-increment . 0.8) and (shortest-duration-space . 1.6) (scm/define-grobs.scm:1724-1725).
%
% ---------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-03, session 83)
%
% ★★★ THE POINT DOES NOT MEASURE AN IDEAL AT ALL. IT MEASURES A FLOOR.
%
% The ideal is now known exactly, and the formula is confirmed rather than assumed. LilyPond
% was asked for its own inputs (CG-OPTS): GraceSpacing's common-shortest-duration is
% Mom 0G1/16 -- main part zero, so Spacing_options::init_from_grob (:45-52) takes the GRACE
% part, 1/16 -- while the columns' moments are Mom 0, Mom 1/2G-1/16 and Mom 1/2, so
% delta_t.grace_part_ across the grace column is also 1/16. Ratio 1, log2 0, and
% spacing-options.cc:105 gives
%
%   len   = (1.6 + 0) * 0.8                      = 1.280000000
%   ideal = len - 0.8 + 0.574399405 (grace head) = 1.054399405   [note-spacing.cc:77]
%
% The step MEASURES 1.377510498. An ideal cannot be exceeded by its own spring at natural
% length, so the drawn gap is not the ideal.
%
% ⚠️ THAT WAS NOT DEDUCED, IT WAS DRIVEN. Two books move the ideal and watch the step:
%   CG-WIDE    shortest-duration-space 6    predicted 4.574399405260890
%                                           MEASURED  4.574399405260890   (fourteen digits)
%   CG-NARROW  shortest-duration-space 0.5  ideal 0.174399405
%                                           MEASURED  1.377510498  -- IDENTICAL to CG-BASE
% So above the floor the step tracks the formula exactly, and below it the step does not move
% at all. The formula is right AND the ledger's book sits on the floor.
%
% ⇒ CONSEQUENCE FOR THE PORT: porting the grace ideal will not close cue.grace.column.to-main.
%   What has to be right is the MINIMUM -- the skyline distance between the grace column and
%   the column after it (note-spacing.cc:78-83), which for a grace means its flag and stem ink.
% ⚠️ THE FLOOR'S OWN FORM IS A CANDIDATE, NOT A MEASUREMENT. merge_springs' headroom
%   (spacing-spanner.cc:380-393 -> spring.cc:122, avg = max (min_distance + 0.3, avg)) would
%   make min_distance 1.077510498. This file did NOT measure min_distance, and 1.077510498 is
%   not to be written anywhere as if it had.
%
% ★★ AND THE STEM CORRECTION IS RULED OUT -- WITH AN INSTRUMENT THAT WAS PROVED ALIVE FIRST.
% CG-NOCOR (correction 0) and CG-BIGCOR (+10) and CG-NEGCOR (-10) are all identical to
% CG-BASE, so note-spacing.cc:111 contributes 0 here even though the grace's stem points up
% and the cue half note's points down. ⚠️ THAT SENTENCE WAS WORTHLESS UNTIL CG-INSTR EXISTED:
% "the output did not move" and "the override never reached the grob" are the same
% observation, and this file had already been bitten by the second (see the \with note below).
% CG-INSTR-BASE / CG-INSTR-BIG are a plain two-column book with disagreeing stems and no
% grace or cue in them, and +10 moves their heads from 12.015816 to 20.158674 -- the override
% path works, so the grace book's silence is the spring's answer and not the probe's.
%
% ⚠️ SUPERSEDED, KEPT SO THE MISTAKE IS LEGIBLE: an earlier pass here reasoned that
% 1.377510498 = base - 0.8 + 0.574399405 implies base = 1.603111092, "suspiciously close to
% the declared 1.6", and went looking for the 0.003111092. Both halves were wrong -- the step
% is not the ideal, so there was no such base, and the resemblance to 1.6 was the coincidence
% it looked like. The lesson is the one this corpus keeps relearning: before decomposing a
% number, establish that the number is the thing you think it is.
\paper { indent = 0 ragged-right = ##t }

#(define (dumph name)
   (lambda (g)
     (format #t "PROBE ~a head x=~a width=~a fontsize=~a\n" name
             (ly:grob-relative-coordinate (ly:item-get-column g) (ly:grob-system g) X)
             (cdr (ly:grob-extent g g X))
             (ly:grob-property g 'font-size))))

\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumph "CG-BASE")
  } { \clef treble \time 4/4 c''2 \new CueVoice { \grace { d''16 } e''2 } } }

% ...and the three numbers Spacing_options::init_from_grob actually reads off the GraceSpacing
% grob, plus the column moments delta_t is made of. ASKED RATHER THAN SOLVED FOR:
% :42-52 takes shortest-duration-space and spacing-increment straight from the grob, and
% global_shortest_ from common-shortest-duration -- its MAIN part when that is non-zero and its
% GRACE part otherwise. GraceSpacing's common-shortest-duration is a callback
% (grace-spacing::calc-shortest-duration), so the declared 1.6 / 0.8 pair says nothing about
% which duration the ratio is taken against.
% ⚠️ NOT after-line-breaking. MEASURED: an after-line-breaking override on GraceSpacing never
% fires (no GSP line printed), so the property is read by WRAPPING its own callback -- the
% wrapper calls grace-spacing::calc-shortest-duration and returns exactly what it returned, so
% nothing about the spacing changes and the value printed is the value used.
#(define ((dumpgsp name) g)
   (let ((v (grace-spacing::calc-shortest-duration g)))
     (format #t "PROBE ~a GSP common-shortest=~s sds=~s inc=~s\n" name v
             (ly:grob-property g 'shortest-duration-space)
             (ly:grob-property g 'spacing-increment))
     v))
#(define (dumpwhen name)
   (lambda (g)
     (format #t "PROBE ~a WHEN when=~s x=~a fontsize=~a\n" name
             (ly:grob-property (ly:item-get-column g) 'when)
             (ly:grob-relative-coordinate (ly:item-get-column g) (ly:grob-system g) X)
             (ly:grob-property g 'font-size))))

% ⚠️ AND THE OVERRIDE GOES IN \layout \context \Score, NOT IN \with. Grace_spacing_engraver
% lives in Score (ly/engraver-init.ly:771), so a Staff-level override of a GraceSpacing
% property never reaches the grob -- MEASURED: the \with form printed nothing at all.
\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumpwhen "CG-OPTS")
  } { \clef treble \time 4/4 c''2 \new CueVoice { \grace { d''16 } e''2 } }
  \layout { \context { \Score
    \override GraceSpacing.common-shortest-duration = #(dumpgsp "CG-OPTS")
  } } }

\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumph "CG-NOCOR")
    \override NoteSpacing.stem-spacing-correction = #0
    \override NoteSpacing.knee-spacing-correction = #0
  } { \clef treble \time 4/4 c''2 \new CueVoice { \grace { d''16 } e''2 } } }

% ⚠️ THE POSITIVE CONTROL, AND IT IS NOT OPTIONAL. CG-NOCOR proves nothing on its own: "the
% output did not move" and "the override never reached the grob" look identical, and the
% \with-form GraceSpacing override above turned out to be exactly that failure. So the SAME
% override path is driven to a value that MUST move the drawing. stem_dir_correction is read
% off the NoteSpacing grob (note-spacing.cc:111 passes the wish, not GraceSpacing), and
% Note_spacing_engraver sits in Voice, which a Staff-level override does reach.
% If CG-BIGCOR is identical to CG-BASE as well, the instrument is dead and CG-NOCOR says
% nothing at all.
\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumph "CG-BIGCOR")
    \override NoteSpacing.stem-spacing-correction = #10
  } { \clef treble \time 4/4 c''2 \new CueVoice { \grace { d''16 } e''2 } } }

% ...both signs, because different_directions_correction is SIGNED (note-spacing.cc:155
% multiplies by left_stem_dir) and a correction that pushes the ideal further DOWN cannot show
% if the spring is already sitting on a floor.
\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumph "CG-NEGCOR")
    \override NoteSpacing.stem-spacing-correction = #-10
  } { \clef treble \time 4/4 c''2 \new CueVoice { \grace { d''16 } e''2 } } }

% ...and the instrument's OWN control: a book with no grace and no cue in it at all, where
% the stem correction is known to be live (two columns whose stems disagree). If THIS pair
% does not move, the override path is broken and every "did not move" above is worthless.
\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumph "CG-INSTR-BASE")
  } { \clef treble \time 4/4 a'4 b''4 a'4 b''4 } }
\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumph "CG-INSTR-BIG")
    \override NoteSpacing.stem-spacing-correction = #10
  } { \clef treble \time 4/4 a'4 b''4 a'4 b''4 } }

% ---------------------------------------------------------------------------------------
% IS THE STEP THE IDEAL AT ALL, OR A FLOOR?
%
% With common-shortest-duration measured as Mom 0G1/16 and delta_t.grace_part_ = 1/16 the
% ratio is 1, so spacing-options.cc:105 gives len = (1.6 + log2 1) * 0.8 = 1.280000000 and
% note-spacing.cc:77 turns that into 1.280000 - 0.8 + 0.574399405 = 1.054399405. The step
% MEASURES 1.377510498. An ideal that small cannot produce a gap that large, so the gap is
% not the ideal: the candidate is merge_springs' floor (spacing-spanner.cc:380-393 ->
% spring.cc:122, avg_distance = max (min_distance + 0.3, avg_distance)), the same floor
% already documented for ordinary columns.
%
% THE TEST IS NOT ARITHMETIC. Raise shortest-duration-space until the ideal is far above any
% floor and see whether the step starts TRACKING it. sds 6 gives len = 4.8 and an ideal of
% 4.8 - 0.8 + 0.574399405 = 4.574399405.
\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumph "CG-WIDE")
  } { \clef treble \time 4/4 c''2 \new CueVoice { \grace { d''16 } e''2 } }
  \layout { \context { \Score \override GraceSpacing.shortest-duration-space = #6 } } }

% ...and the other side of the same claim: an ideal pushed FURTHER BELOW the floor must not
% move the step at all. sds 0.5 gives len = 0.4 and an ideal of 0.174399405.
\score { \new Staff \with {
    \override NoteHead.after-line-breaking = #(dumph "CG-NARROW")
  } { \clef treble \time 4/4 c''2 \new CueVoice { \grace { d''16 } e''2 } }
  \layout { \context { \Score \override GraceSpacing.shortest-duration-space = #0.5 } } }

\version "2.26.0"
%
% HOW WIDE IS A GRACE COLUMN?
%
% beam-grace.ly measured ONE number: LilyPond puts the two stems of `\grace { d16 e }` at
% 8.437939 and 9.855877, i.e. one grace column is 1.417939, against Lily#'s
% (GraceNoteWidth 1.2 + GraceNoteSpacing 0.3) * 0.65 = 0.975 — 45% narrower, which is what
% flattens the grace beam's quant (beam.quant.grace.* are symmetric: the height is exact and
% only the slope is left, and a slope-only residual has to live in the X frame).
%
% ⚠️ 1.417939 MUST NOT BE WRITTEN INTO GraceNoteWidth. It is two SIXTEENTH graces — one
% texture — and audit/lp-geometry has been burned by exactly that shape before (the figured
% bass 1.5, HANDOFF 5.0). LilyPond does not have a grace column WIDTH at all; it has a
% SPRING, and this probe is here to make its law visible before anything is ported:
%
%   lily/spacing-basic.cc:163-180 Spacing_spanner::note_spacing — for a spring whose
%     delta_t has a grace part, the options come from the GraceSpacing grob:
%       len = grace_opts.get_duration_space (delta_t.grace_part_);  min = increment
%   lily/spacing-options.cc:71-107 Spacing_options::get_duration_space —
%       ratio < 1 : (shortest_duration_space_ + ratio - 1) * increment_
%       else      : (shortest_duration_space_ + log2 (ratio)) * increment_
%   scm/define-grobs.scm:1721-1725 GraceSpacing — shortest-duration-space 1.6,
%     spacing-increment 0.8, common-shortest-duration grace-spacing::calc-shortest-duration
%   scm/output-lib.scm:1403-1422 grace-spacing::calc-shortest-duration — the MINIMUM of the
%     differences between consecutive columns of the run (the run's own columns, not the
%     score's global shortest), so the run is scale-free in duration.
%   lily/note-spacing.cc:42-115 Note_spacing::get_spacing — the spring the spanner asked for
%     is then rewritten: ideal = base.ideal - increment + left_head_end, where left_head_end
%     is the RIGHT edge of the left column's first note head measured in that column.
%
% Everything above is read from the source, so it is a PREDICTION, not a fit:
%
%   gap(i -> i+1) = (1.6 + log2 (dt / dt_min)) * 0.8 - 0.8 + head_end
%
% with dt the grace-part difference between the two columns, dt_min the run's minimum, and
% head_end the grace note head's right edge in its column. The ratio is >= 1 BY CONSTRUCTION
% (dt_min is the minimum), so the ratio<1 branch of get_duration_space is unreachable for a
% grace run and every gap is at least 1.6 * 0.8 - 0.8 + head_end.
%
% Falsifiable consequences, one book each:
%   * the gap does not depend on how MANY graces there are      (GCW2 / GCW3 / GCW4 equal)
%   * the gap does not depend on the graces' DURATION, as long
%     as they are all equal — the run normalises by its own min (GCW2 / GCW2E / GCW2T equal)
%   * a run with two different durations DOES split, by log2 of
%     the ratio: the longer step is 0.8 wider                   (GCWM / GCWN)
%   * the last grace -> main gap obeys the same law as any other
%     grace gap, i.e. it is NOT a separate junction padding      (GCW1)
%   * head_end scales with the grace font, so the ordinary
%     control's head_end / the grace's = magstep(-3) = 0.7071    (GCWO)
%
% Output: one line per note head, one line per record (HANDOFF 5.3),
%   PROBEGC <name> HEAD main=<main part> grace=<grace part> colx=<column x in the system>
%                       ext=<head extent in its own column>
% The gaps are differences of colx; ext[RIGHT] is left_head_end for that column.
\paper { indent = 0 ragged-right = ##t }

#(define (dump-head name)
   (lambda (grob)
     (let* ((col (ly:item-get-column grob))
            (sys (ly:grob-system grob))
            (mom (ly:grob-property col 'when))
            ;; The column's own separation skylines — what lily/spacing-spanner.cc:246-249
            ;; reads to set the ROD between two columns, i.e. the floor under the spring.
            (sk (ly:grob-property col 'horizontal-skylines)))
       (format #t "PROBEGC ~a HEAD main=~a grace=~a colx=~a ext=~a skyL=~a skyR=~a\n" name
               (ly:moment-main mom)
               (ly:moment-grace mom)
               (ly:grob-relative-coordinate col sys X)
               (ly:grob-extent grob col X)
               (if (pair? sk) (ly:skyline-max-height (car sk)) 'none)
               (if (pair? sk) (ly:skyline-max-height (cdr sk)) 'none)))))

%% An accidental is a CONDITIONAL element of its column's separation item, so it only enters
%% the floor when the column on the left is close enough to see it. Dump its extent in the
%% same frame as the heads, so GCWA's floor is read rather than left as a remainder.
#(define (dump-acc name)
   (lambda (grob)
     (let* ((col (ly:item-get-column grob)))
       (format #t "PROBEGC ~a ACC ext=~a\n" name (ly:grob-extent grob col X)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override NoteHead.after-line-breaking = #(dump-head name)
                         \override Accidental.after-line-breaking = #(dump-acc name) }
      { $music } #})

% The corpus regime: bar 1 of test/grace-notes, the book beam-grace.ly calls G.
\score { \sweep "GCW2" { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 } }

% COUNT. Three and four graces of the same duration: if the gap is a spring and not a
% reserved group width, every gap here equals GCW2's.
\score { \sweep "GCW3" { \time 4/4 \grace { d'16 e' f' } g'4 g'2 r4 } }
\score { \sweep "GCW4" { \time 4/4 \grace { d'16 e' f' g' } a'4 g'2 r4 } }

% DURATION. Eighths and thirty-seconds, all equal within the run: the run normalises by its
% own minimum, so these must give the SAME gap as the sixteenths — a width that scaled with
% the note value would show up here and nowhere else.
\score { \sweep "GCW2E" { \time 4/4 \grace { d'8 e' } f'4 g'2 r4 } }
\score { \sweep "GCW2T" { \time 4/4 \grace { d'32 e' } f'4 g'2 r4 } }

% ONE grace: the only gap is the grace -> main one. If that gap equals GCW2's gaps, the
% junction is not a padding of its own (Lily# spends GraceToMainRod 0.4 on it).
\score { \sweep "GCW1" { \time 4/4 \grace { d'16 } f'4 g'2 r4 } }

% MIXED durations, both orders. dt_min is 1/16 in both, so the 1/8 step should be
% log2 (2) * 0.8 = 0.8 wider than the 1/16 step, on whichever side it falls.
\score { \sweep "GCWM" { \time 4/4 \grace { d'16 e'8 } f'4 g'2 r4 } }
\score { \sweep "GCWN" { \time 4/4 \grace { d'8 e'16 } f'4 g'2 r4 } }

% ACCIDENTAL on the SECOND grace. Its accidental hangs left of that column, so it enters the
% RIGHT column's left skyline (as a CONDITIONAL element — lily/separation-item.cc:120-190
% boxes(me, left)), and the floor must grow by it while the spring's ideal does not move.
% This is the term GCW1 cannot test: GCW1 only widens the LEFT column.
\score { \sweep "GCWA" { \time 4/4 \grace { d'16 eis' } f'4 g'2 r4 } }

% APPROACH, with its control IN THE SAME BOOK. A note before the grace, so the previous main
% column -> first grace column spring is visible; lily/spacing-spanner.cc:396-403 multiplies
% that spring by 0.8 when the right column is a grace and the left one is not. That spring is
% an ORDINARY one (delta_t has a main part and the LEFT column has no grace part, so
% lily/spacing-basic.cc:148-162 takes the first branch), so the control has to be an ordinary
% quarter gap in the SAME book: c'->grace against f'->g'. A control in a separate book would
% measure a different spring, because a grace changes the score's common-shortest-duration.
% Both left heads are black quarter heads and neither pair triggers
% same_direction_correction (adjacent staff positions, delta == 1, not > 1), so the two
% springs are identical apart from the 0.8 — the ratio IS the multiplication.
\score { \sweep "GCWP" { \time 4/4 c'4 \grace { d'16 e' } f'4 g'2 } }

% CONTROL, full size: the same two pitches as ordinary sixteenths — the book beam-grace.ly
% calls H, whose beam is already exact in Lily#. Its head ext is the unscaled head_end.
\score { \sweep "GCWO" { \time 4/4 d'16 e' r8 g'2 r4 } }

% THE TWO PARAMETERS, one override each (HANDOFF 5.3: an identity pair made out of a grob
% property rather than out of music — same file, same music, same run, so the difference IS
% the one term). If the reading follows these, the gap is the SPRING's ideal; if it stops
% following on the way DOWN, the gap that stopped it is the rod, and its value is the floor.
%   GCWS1 sds 1.0 : ideal = 1.0*0.8 - 0.8 + head_end = head_end itself
%   GCWS3 sds 3.0 : ideal = 3.0*0.8 - 0.8 + head_end
%   GCWI  incr 1.6: ideal = 1.6*1.6 - 1.6 + head_end
\score {
  \new Staff \with { \override NoteHead.after-line-breaking = #(dump-head "GCWS1") }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
  \layout { \context { \Score \override GraceSpacing.shortest-duration-space = #1.0 } }
}
\score {
  \new Staff \with { \override NoteHead.after-line-breaking = #(dump-head "GCWS3") }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
  \layout { \context { \Score \override GraceSpacing.shortest-duration-space = #3.0 } }
}
\score {
  \new Staff \with { \override NoteHead.after-line-breaking = #(dump-head "GCWI") }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
  \layout { \context { \Score \override GraceSpacing.spacing-increment = #1.6 } }
}

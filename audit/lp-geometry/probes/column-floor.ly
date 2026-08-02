\version "2.26.0"
%
% WHAT DOES A COLUMN PAIR'S *FLOOR* MEASURE FROM — THE SKYLINE, OR THE ROD ABOVE IT?
%
% THE DEFECT THIS WAS OPENED FOR (2026-08-02, session 72). All three points of
% flagged-stem-reach.ly carried the same +0.100000 after the flag term itself was closed, and
% the entry for flag.down.reach.high-neighbour-control said in writing that whoever took it
% should NOT assume it was about flags. It was not. MeasureLayouter raised the spring's
% minimum to the ROD — the skyline distance PLUS the spacing spanner's 0.1 padding — and only
% then applied merge_springs' headroom, so every gap the floor decided came out at
% skyline + 0.1 + 0.3 instead of skyline + 0.3. LilyPond raises TWO separate constraints over
% one pair and the headroom is measured from the FIRST of them:
%   lily/note-spacing.cc:78-83  Note_spacing::get_spacing — the SPRING's minimum is the
%     padding-free skyline distance.
%   lily/separation-item.cc:47-68 Separation_item::set_distance — the ROD is that distance
%     plus `padding` (0.1), raised beside the spring by Spacing_spanner::set_column_rods.
%   lily/spring.cc:122          merge_springs — avg_distance = max (min_distance + 0.3, …).
% Being 0.2 under the headroom's answer, the rod cannot bind at force >= 0 at all.
%
% ⚠️ THE BOOKS HERE CONTAIN NO FLAG, WHICH IS THE WHOLE POINT. A flagged book cannot say
% whether a common offset belongs to the flag; a book with a quarter on both sides can.
%
% ⚠️ A FLOOR-BOUND PAIR IS NOT EASY TO BUILD AND MOST TEXTURES MEASURE NOTHING. The duration
% ideal bottoms out at 2.504200 — measured, not assumed: XS32 (c''32 d''32) and XS64
% (c''64 d''64) both report exactly that, the same number an eighth pair reports — while a
% plain head-to-head floor is only 1.404200 + 0.100000 + 0.3 = 1.804200. So NO accidental-free
% pair in this shape is ever floor-bound, at any duration, and the accidental is what lifts
% the floor over the ideal. XQN below is kept as the null result that says so.
%
% Output, one line per note column:
%   PROBECF <name> COL <index> when=<moment> x=<column x in the system>
%                  skyL=<left reach> skyR=<right reach> stem=<up|down|none>
\paper { indent = 0 ragged-right = ##t }

#(define (dump-col name idx)
   (lambda (grob)
     (let* ((col (ly:item-get-column grob))
            (sys (ly:grob-system grob))
            (sk (ly:grob-property col 'horizontal-skylines))
            (stem (ly:grob-object grob 'stem #f)))
       (format #t "PROBECF ~a COL ~a when=~a x=~a skyL=~a skyR=~a stem=~a\n" name idx
               (ly:moment-main (ly:grob-property col 'when))
               (ly:grob-relative-coordinate col sys X)
               (if (pair? sk) (ly:skyline-max-height (car sk)) 'none)
               (if (pair? sk) (ly:skyline-max-height (cdr sk)) 'none)
               (if (ly:grob? stem)
                   (if (> (ly:grob-property stem 'direction) 0) 'up 'down)
                   'none)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override NoteHead.after-line-breaking = #(dump-col name 0) }
      { $music } #})

% A. THE POINT. Two quarters, the right one carrying a sharp. Nothing flagged, nothing
%    beamed, nothing graced: the gap is the floor and only the floor.
\score { \sweep "XQS" { \time 4/4 c''4 dis''4 r2 } }

% B. THE CONTROL, and it is a NULL RESULT that has to be read as one: the same book without
%    the accidental is SPRING-bound (3.002245 is the quarter's duration space, not a floor),
%    so it was already exact before the fix and stayed exact after it. It says the duration
%    side of the spring was never what moved.
\score { \sweep "XQN" { \time 4/4 c''4 d''4 r2 } }

% C. A WIDER accidental. If the offset were a glyph width or a padding of the accidental's
%    own it would scale with the glyph; the floor is 0.468000 wider here and the residual was
%    the same 0.100000, which is what says the term was a CONSTANT sitting under both.
\score { \sweep "XQD" { \time 4/4 c''4 deses''4 r2 } }

% D. …and the same wide accidental with the left column FLAGGED, at an eighth. Same answer as
%    XQD to six places, which is the arithmetic statement that the flag adds nothing here:
%    a down stem stands at the head's LEFT edge and its flag hangs inside the head's shadow
%    (the null result flagged-stem-reach.ly's FSD8/FSD16/FSD32 report).
\score { \sweep "XFD" { \time 4/4 c''8 deses''4 r4 r8 } }

% E. THE IDEAL'S FLOOR, kept because it is what forbids an accidental-free point. The duration
%    space does not keep shrinking with the duration: a 32nd and a 64th pair both report the
%    eighth's 2.504200.
\score { \sweep "XS32" { \time 4/4 c''32 d''32 r16 r8 r4 r2 } }
\score { \sweep "XS64" { \time 4/4 c''64 d''64 r32 r16 r8 r4 r2 } }

% ---------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-02, session 72)
%
% THE GAPS (column x at the second onset minus column x at 0):
%   XQS   c''4 dis''4     3.354200   FLOOR — 1.404200 (head + its 0.1) + 1.650000 (the sharp's
%                                    ink 1.450000 + the Accidental grob's own 0.2 left
%                                    extra-spacing-width) + 0.3
%   XQN   c''4 d''4       3.002245   SPRING — the quarter's duration space
%   XQD   c''4 deses''4   3.822200   FLOOR, 0.468000 wider: the double flat's wider ink
%   XFD   c''8 deses''4   3.822200   the same, flagged — identical to six places
%   XS32  c''32 d''32     2.504200   the ideal's floor
%   XS64  c''64 d''64     2.504200   …unchanged at half the duration
%
% ⇒ Lily# read 3.454200 / 3.002245 / 3.922200 / 3.922200 / 2.504200 / 2.504200 before the fix:
%   EXACTLY +0.100000 on every FLOOR-bound book and EXACTLY 0 on every SPRING-bound one. That
%   split is the reading — it is the floor's own constant that was wrong, not any ink in the
%   books, and no amount of work on the flag could have found it.

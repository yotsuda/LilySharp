\version "2.26.0"
%
% HOW FAR RIGHT DOES A FLAGGED COLUMN'S INK REACH — AND WHEN DOES THAT REACH DECIDE ANYTHING?
%
% THE DEFECT THIS IS BEING OPENED FOR. Lily# reserves a flagged note's flag at
% noteheadLeftX + StemDownNW.X for a DOWN stem, while the renderer draws it at
% LayoutUtilities.StemX = the head's left edge plus half a stem thickness — 0.065 apart, on
% every flagged down-stem note (ItemSkylineFactory, the comment at the `stemX` assignment).
% An UP stem has no such split. Nothing in the corpus observes it, so the fix is blocked on a
% point, and this file is that point's LilyPond side.
%
% ⚠️ THE LEDGER'S Lily# SIDE READS THE DRAWN DOCUMENT, so a reservation error is only visible
% where the RESERVATION decides a drawn distance. LilyPond's column spring has an ideal from
% the duration and a MINIMUM from the columns' own ink:
%   lily/spacing-spanner.cc:246-249 — the rod between two columns comes from their
%     horizontal-skylines, i.e. the separation items' real boxes.
%   lily/spacing-basic.cc:148-162 Spacing_spanner::note_spacing — ideal = duration space,
%     and the spring's minimum is the skyline distance plus 0.3.
% So a book where the ideal is comfortably wider than the ink measures NOTHING about the ink:
% the flag can be reserved anywhere and the columns do not move (this is the trap named in
% grace-column-width.ly's header, and the reason GCW1 is built the way it is).
%
% THIS FILE THEREFORE ASKS TWO QUESTIONS, IN THIS ORDER:
%   (1) what IS the right-hand reach of a flagged column, up-stem and down-stem?
%       — read straight off the column's own skyline, no spacing involved.
%   (2) in which of the textures below does that reach actually SET the next gap?
%       — printed as both the gap and the reach, so the header's claim can be checked
%         rather than believed: a floor-bound gap equals reach(left col) + reach(right col)
%         + 0.3, and a spring-bound one is wider than that.
%
% Output, one line per note column:
%   PROBEFS <name> COL <index> when=<moment> x=<column x in the system>
%                  skyL=<left reach> skyR=<right reach> stem=<up|down|none>
\paper { indent = 0 ragged-right = ##t }

#(define (dump-col name idx)
   (lambda (grob)
     (let* ((col (ly:item-get-column grob))
            (sys (ly:grob-system grob))
            (sk (ly:grob-property col 'horizontal-skylines))
            (stem (ly:grob-object grob 'stem #f)))
       (format #t "PROBEFS ~a COL ~a when=~a x=~a skyL=~a skyR=~a stem=~a\n" name idx
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

% ---------------------------------------------------------------------------------------
% A. THE REACH ITSELF. One flagged note per book, nothing else to interact with, so skyR is
%    the flagged column's own ink and can be compared against Lily#'s reservation directly.
%    b' is above the middle line => DOWN stem; d' is below => UP stem. The pair is the
%    falsifier: Lily# splits draw from reserve on the DOWN one only.
\score { \sweep "FSD8"  { \time 4/4 b'8 r8 r4 r2 } }
\score { \sweep "FSU8"  { \time 4/4 d'8 r8 r4 r2 } }
\score { \sweep "FSD16" { \time 4/4 b'16 r16 r8 r4 r2 } }
\score { \sweep "FSD32" { \time 4/4 b'32 r32 r16 r8 r4 r2 } }

% B. DOES IT BIND? A flagged down-stem note followed by a column that reaches far LEFT (an
%    accidental), at durations short enough that the duration space is small. If the gap
%    equals skyR + skyL + 0.3 the floor is what set it, and a reservation error moves it;
%    if the gap is wider, this texture measures nothing and must not become a point.
\score { \sweep "FSB8"  { \time 4/4 b'8 cis''8 r4 r2 } }
\score { \sweep "FSB16" { \time 4/4 b'16 cis''16 r8 r4 r2 } }
\score { \sweep "FSB32" { \time 4/4 b'32 cis''32 r16 r8 r4 r2 } }

% C. The same with the accidental's own column made as wide as it gets (double flat), still
%    at 32nds — the widest floor this shape can produce.
\score { \sweep "FSBF" { \time 4/4 b'32 deses''32 r16 r8 r4 r2 } }

% D. ⚠️ A AND B MEASURE NOTHING ABOUT THE FLAG, and the numbers above say so: FSD8, FSD16 and
%    FSD32 all report skyR = 1.4042 = the notehead's 1.3042 plus its 0.1 extra-spacing-width,
%    identical at every duration, while the UP-stem control reports 2.1674. A DOWN stem stands
%    at the head's LEFT edge, so its flag hangs INSIDE the head's shadow and never sets the
%    column's reach at the head's own height.
%    The flag only decides anything where a neighbour's ink lies in the FLAG's Y band — below
%    the head, where the stem ends. So: a high flagged note whose neighbour is low enough to
%    tuck under the flag, and the same pair with the neighbour high (where it cannot).
%    ⚠️ The skyline dumped above is the column's own; what a rod reads is the DISTANCE between
%    two columns' facing skylines, so these books are read as GAPS, with the high-neighbour
%    book as the control that holds the duration fixed.
%    ⚠️⚠️ AND THE NEIGHBOUR MUST NOT BEAM WITH IT. `c''8 dis'8` is one beat and LilyPond beams
%    it, which KILLS THE FLAG (lily/stem-engraver.cc:165-172 kill_unused_flags) and re-decides
%    the stem direction for the whole beam — the first cut of this file measured beamed
%    columns and read their head-only reach as the flag's. An eighth followed by a QUARTER
%    cannot beam, so the flag survives and the columns stay adjacent.
\score { \sweep "FSN8"  { \time 4/4 c''8 dis'8 r4 r2 } }      % beamed — kept as the counter-example
\score { \sweep "FSF8"  { \time 4/4 c''8 dis'4 r4 r8 } }      % flagged, neighbour LOW
\score { \sweep "FSFH8" { \time 4/4 c''8 dis''4 r4 r8 } }     % flagged, neighbour HIGH (control)
\score { \sweep "FSFP8" { \time 4/4 c''8 d'4 r4 r8 } }        % flagged, low, no accidental
\score { \sweep "FSFU8" { \time 4/4 d'8 fis''4 r4 r8 } }      % flagged UP stem, neighbour high

% ---------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND, AND WHICH BOOKS ARE THE POINT (2026-08-02, session 70)
%
% THE GAPS (column x at 1/8 minus column x at 0, all four with the same 1/8 duration space):
%   FSFH8  c''8 dis''4   3.354200   neighbour HIGH  — its accidental faces the NOTEHEAD
%   FSF8   c''8 dis'4    3.181800   neighbour LOW   — its accidental faces the FLAG
%   FSFP8  c''8 d'4      2.504200   neighbour LOW, no accidental
%   FSFU8  d'8 fis''4    4.117400   UP stem, where the flag is beside the head and reaches
%                                   2.167400 — the up-stem case has no draw/reserve split
%
% ⇒ FSF8 AND FSFH8 ARE A PAIR THAT MOVES ONE THING. Same durations, same accidental, same
%   flag; only the neighbour's PITCH differs, and the gap closes by 0.172400. So the left
%   column's reach is 1.404200 at the head's height and 1.404200 - 0.172400 = 1.231800 at the
%   flag's — the FLAG is what the low accidental binds on, and nothing else in the book can
%   produce that difference.
%
% ⇒ THE POINT IS FLOOR-BOUND, CHECKED RATHER THAN ASSUMED (the trap this header opened with):
%   a spring-bound gap cannot depend on the neighbour's pitch at all, since the duration space
%   is a function of the durations alone (lily/spacing-options.cc:71-107). The gap moved by
%   0.1724 when only a pitch moved, so the rod is what is being read in both books.
%
% ⚠️ AND A AND B DO NOT MEASURE THE FLAG. FSD8/FSD16/FSD32 report skyR = 1.404200 = the
%   notehead's 1.304200 + 0.1, identical at every duration: a DOWN stem stands at the head's
%   LEFT edge, so its flag hangs inside the head's shadow and never sets the reach at the
%   head's own height. Those books are kept because that null result is the reason the naive
%   texture (a flagged note and any old neighbour) observes nothing.
% ⚠️ FSN8 is kept for the same reason: `c''8 dis'8` is ONE BEAT, LilyPond beams it, the Flag
%   grob suicides and the stem direction is re-decided for the beam. It reads like a flagged
%   book and is not one.

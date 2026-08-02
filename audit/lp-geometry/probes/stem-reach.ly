\version "2.26.0"
%
% DOES A COLUMN'S *STEM* HOLD THE NEXT COLUMN OFF?
%
% THE DEFECT THIS WAS OPENED FOR (2026-08-02, session 75). Lily#'s column skyline was built
% from a HAND-WRITTEN LIST of which parts to include -- heads, dots, flag, accidental -- and
% the stem was not on it. LilyPond does not have such a list. Every acknowledged Item goes
% into its column's `elements` and every element becomes a box:
%   lily/paper-column-engraver.cc:246-261 Paper_column_engraver::stop_translation_timestep --
%     the only diversions are Accidental_placement and Arpeggio (to `conditional-elements`)
%     and a bare Accidental (dropped). A Stem is an Item, so it is in, unfiltered.
%   lily/separation-item.cc:152-187 Separation_item::boxes -- one box per element; axis
%     groups are skipped at :160-161 so the head, the stem and the dots enter SEPARATELY.
%   lily/stem.cc:387-447 Stem::internal_pure_height -- the box's Y for a stem.
% So in LilyPond a stem is in the skyline because NOTHING TOOK IT OUT.
%
% ⚠️ THE STEM NEVER REACHES PAST ITS OWN HEAD HORIZONTALLY -- an up stem's right edge IS the
% head's right edge (lily/stem.cc:889-906 Stem::width, ±thickness/2 about an X-offset that
% puts it there). It can therefore only ever be READ AT A Y THE HEAD DOES NOT OCCUPY. That is
% what makes this pair hard to build and why the omission survived: in most textures the head
% answers first and the stem changes nothing.
%
% THE PAIR MOVES EXACTLY ONE THING -- the neighbour's PITCH. Same first note, same accidental
% glyph, same durations.
%   SRA  the neighbour is 3 staff positions up: OUTSIDE the head's Y band, INSIDE the stem's.
%        Only the stem can hold it off.
%   SRH  the neighbour is 1 staff position up: INSIDE the head's Y band. The head answers and
%        the stem adds nothing (it stands at the head's own right edge), so this book reads
%        the same number whether or not a stem is in the skyline. It is the control, and it
%        has to stay EXACT across the fix or the reading of SRA is not about stems.
%
% Output, one line per note column:
%   PROBESR <name> COL <index> when=<moment> x=<column x in the system>
%                  skyL=<left reach> skyR=<right reach> stem=<up|down|none>
\paper { indent = 0 ragged-right = ##t }

#(define (dump-col name idx)
   (lambda (grob)
     (let* ((col (ly:item-get-column grob))
            (sys (ly:grob-system grob))
            (sk (ly:grob-property col 'horizontal-skylines))
            (stem (ly:grob-object grob 'stem #f)))
       (format #t "PROBESR ~a COL ~a when=~a x=~a skyL=~a skyR=~a stem=~a\n" name idx
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

% A. THE POINT. g' sits two positions below the middle line, so its stem is UP and stands at
%    the head's right edge, running seven positions upward. ceses'' is three positions above
%    g' -- the two HEADS are 1.5 staff spaces apart and never share a Y, while the double
%    flat's ink sits squarely in the stem's band. A double flat rather than a flat because it
%    is wide enough to lift the floor clear of the quarter's duration space (3.002245), so the
%    gap is decided by the skyline and not by the spring.
\score { \sweep "SRA" { \time 4/4 g'4 ceses''4 r2 } }

% B. THE CONTROL. Only the neighbour's pitch moves: aeses' is ONE position above g', so the
%    heads share a Y and the HEAD sets the distance. The stem stands at that same right edge,
%    so it cannot change the answer -- this book must read identically with the stem in the
%    skyline and with it out.
\score { \sweep "SRH" { \time 4/4 g'4 aeses'4 r2 } }

\version "2.26.0"
%% LP FIDELITY PROBE — a TextScript's PLACED BASELINE and RESERVED INK, per string, above a
%% flat staff. The pair that measures OutsideStaffStacker's "Own tuning" letter-class
%% constants (CapHeightEm 0.71 / TextAscentEm 0.75 / TextDescentEm 0.25) against what
%% LilyPond actually does, which is to read the TEXT'S OWN INK from the face.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe textscript-ink.ly (three tiny books, seconds),
%% or ../Measure-LilyPondPageGeometry.ps1 -Probe textscript-ink.ly for the parsed form.
%%
%% WHAT IS BEING MEASURED
%%
%% LILYPOND-REF: scm/define-grobs.scm:3800-3833 TextScript — (side-axis . Y),
%%   (Y-offset . side-position-interface::y-aligned-side), (padding . 0.3),
%%   (staff-padding . 0.5), (outside-staff-priority . 450),
%%   (Y-extent . grob::always-Y-extent-from-stencil),
%%   (vertical-skylines . grob::always-vertical-skylines-from-stencil).
%% LILYPOND-REF: lily/side-position-interface.cc:288-427 general_side_position /
%%   aligned_side — the grob's EDGE nearest the staff (its ink bottom, for direction UP)
%%   clears the support by `padding`, with `staff-padding` keeping at least that much
%%   between the staff and the SAME edge.
%% LILYPOND-REF: lily/axis-group-interface.cc:700 add_grobs_of_one_priority and :648
%%   avoid_outside_staff_collisions — a second outside-staff grob over the same X clears
%%   the FIRST one's ink by the grob's own `outside-staff-padding`, default 0.46
%%   (:45 default_outside_staff_padding_; TextScript declares only the horizontal 0.2).
%%
%% Everything above says INK — ly:text-interface::print's stencil extent — never a
%% letter-class table. Lily#'s OutsideStaffStacker instead prices every text grob at flat
%% 0.75 em up / 0.25 em down ("no single LP grob source", its own comment says), so a
%% descender and its absence read identically there. These books make LilyPond answer
%% per string.
%%
%% THE MUSIC keeps the support FLAT: c''4 takes a DOWN stem (c'' sits half a space above
%% the middle line), so nothing but the staff's own top line stands under the text and the
%% side-position support is the same at every X that matters. ⚠️ Do NOT use a' here — its
%% UP stem reaches 3.0 above the middle and becomes the support, and the entry silently
%% turns into a measurement of a stem tip (HANDOFF §5.0: "probe が何を測っているか確かめて
%% から信じる").
%%
%% THE TEXTS are italic, because Lily#'s `_"text"` draws serif ITALIC (SharedRenderer
%% DrawCustomTexts, FontStyle.Italic) — the pair must measure the same face the engine
%% draws, or the readings bundle a face swap on top of the mechanism.
%%   "dolce" — ascenders (d, l), no descender: ink bottom is only the round letters'
%%             overshoot below the baseline (predicted a few hundredths of a staff space).
%%   "poco"  — a descender (p), no ascender: ink bottom is the p's full descent.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%
%%   * Baseline(TXP "poco") sits HIGHER than baseline(TXD "dolce") by exactly
%%     descent(poco) - descent(dolce): LilyPond puts the INK BOTTOM one padding above the
%%     support, so the baseline rides the string's own descent. At font-size 0 the text em
%%     is 2.330827 ss (LyricText's measured 2.469417 at font-size 1.0, divided by 1.05946),
%%     and an italic serif p descends roughly 0.2 em, so the step is predicted around
%%     +0.4..+0.55 ss, sign certain. FALSIFIER: a step of 0.000000 means the placed edge is
%%     NOT the ink bottom (a nominal box would read so) and the whole port premise is wrong.
%%   * The two books' ext=(bottom . top) about the grob's own refpoint ARE the ink terms:
%%     ext bottom(TXD) ≈ -0.03 (overshoot only), ext bottom(TXP) ≈ -0.5 (the descender).
%%     ext top(TXD) > ext top(TXP): ascender against x-height.
%%   * TXS stacks a second script on the same note: upper baseline - lower baseline =
%%     inkTop(lower "dolce") + outside-staff-padding 0.46 + descent(upper "poco"),
%%     predicted ≈ 1.7 + 0.46 + 0.5 ≈ 2.66 ss, IF the touch is box-like. The two texts are
%%     left-aligned on the same notehead (X-align-on-main-noteheads) and both extremes —
%%     d's ascender, p's descender — stand at the LEFT of their strings, so the pointwise
%%     skyline term should be near zero here; a reading clearly BELOW the box arithmetic
%%     measures the outline-vs-box term instead (name it, don't fit it).
%%     ⚠️ Lily# today reads this step as flat 0.75em·2.4 + 0.46 + 0.25em·2.4 = 2.860000
%%     whatever the strings are.
%%   * TXL stacks "poco" over "mum": the lower string has NO ascender and its x-height top
%%     runs FLAT across its whole width, so the upper text's descender lands on the lower's
%%     ink top wherever it falls and the pointwise term collapses to the box arithmetic:
%%     step = inkTop("mum") + 0.46 + descent("poco"), to within the overshoot. This is the
%%     book that reads the ASCENT/immediately-consumable form of the claim; TXS beside it
%%     reads how far the OUTLINE beats the box when the extremes do NOT align.
%%   * Both TXD and TXP put ONE system on one page and the SAME staff refpoint below the
%%     top margin; if those differ, the text reached further than intended and the books
%%     are not a pair.
%%
%% MEASURED 2026-07-29 (first run, before TXL was added):
%%   TXD baseline = 2.550000 over the staff refpoint, SIX-DIGIT ROUND: the staff symbol's
%%     ink 2.050000 plus staff-padding 0.5 applied to the REFPOINT —
%%     LILYPOND-REF: lily/side-position-interface.cc:401-453 aligned_side ("Ensure
%%     'staff-padding' from my refpoint to the staff"): total_off, the REFPOINT offset,
%%     is floored at staff_extent[dir] + staff_padding. A floor Lily# does not have at all.
%%   TXP ink bottom = 2.510000, six-digit round again: staff ink 2.050000 + 0.46 —
%%     LILYPOND-REF: lily/axis-group-interface.cc:45 default_outside_staff_padding_ 0.46,
%%     :739-806 (the collision pass the baseline rides when the descender pushes the edge
%%     constraint past the staff-padding floor: max(2.550000, 2.510000 + 0.444430)).
%%   TXS step = 2.104975 against box arithmetic 1.621440 + 0.46 + 0.444430 = 2.525870:
%%     the outline term is 0.420895 here — avoid_outside_staff_collisions measures the two
%%     texts' OUTLINE skylines pointwise, and "poco"'s left-standing descender falls over
%%     "dolce"'s bowl, not its ascender. Lily#'s interval stacker (boxes) cannot read this;
%%     name it, do not fit it.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEV PAPER top-margin=~a paper-height=~a line-width=~a\n"
           (ly:output-def-lookup layout 'top-margin)
           (ly:output-def-lookup layout 'paper-height)
           (ly:output-def-lookup layout 'line-width))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEV PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEV SYS ~a ~a y=~a ext=(~a . ~a) staff=(~a . ~a)\n"
                           n i
                           (ly:prob-property sys 'Y-offset 0.0)
                           (car ext) (cdr ext)
                           (car staff) (cdr staff))
                   ;; Every TextScript, with its baseline about the SYSTEM refpoint (rel)
                   ;; and its ink about its own refpoint (ext) — rel minus the staff
                   ;; refpoint is the staff-to-baseline the ledger wants, and ext is the
                   ;; per-string ink the Own-tuning constants stand in for.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    ;; NoteHead rows exist for the X pair: the script's
                                    ;; X-offset is 0 about its parent (self/parent-
                                    ;; alignment-X both #f), so its x-left must EQUAL the
                                    ;; anchor note column's origin = its head's left edge.
                                    (if (memq nm '(TextScript NoteHead))
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a)\n"
                                                n i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (car (ly:grob-extent g g X)))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (cdr (ly:grob-extent g g X)))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

%% The serif font is pinned because the TEXT IS the binding ink here: on the svg backend
%% LilyPond's fonts.serif falls back to whatever fontconfig resolves on this machine
%% (ly/paper-defaults-init.ly:174-177), and these entries would silently measure Verdana's
%% cousin instead of C059. Same pin as page-vertical.ly and system-clef-floor.ly.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% TXD — NO DESCENDER. The baseline sits one padding-decided step above the support, plus
%%     only the round letters' overshoot.
\book {
  \probeTag "TXD"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "dolce" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TXP — THE DESCENDER. Identical music and paper; the string is the only difference, so
%%     LilyPond's baseline step between TXD and TXP is the descent itself (its side of the
%%     pair is an identity in everything else).
\book {
  \probeTag "TXP"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "poco" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TXS — TWO SCRIPTS STACKED on one note: the upper one clears the lower one's INK by
%%     outside-staff-padding — but POINTWISE, outline against outline, so this book
%%     measures how far LilyPond's skyline beats the box when the two strings' extremes
%%     stand at different x (p's descender over d's bowl).
\book {
  \probeTag "TXS"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \italic "dolce" ^\markup \italic "poco" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TXL — THE BOX-LIKE STACK: "mum" has no ascender and no descender, its x-height top runs
%%     flat across the whole string, so wherever "poco"'s descender falls the distance is
%%     inkTop(mum) + 0.46 + descent(poco) — the form Lily#'s interval stacker CAN represent.
%%     This is the reading the ink port must land on; TXS above is the reading it cannot.
\book {
  \probeTag "TXL"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \italic "mum" ^\markup \italic "poco" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

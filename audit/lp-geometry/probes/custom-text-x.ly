\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A CUSTOM TEXT'S SILHOUETTE STANDS ALONG X.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe custom-text-x.ly -Prefix PROBECX
%%
%% THE REGIME THIS OPENS (2026-08-28, session 272 act 5 — the last piece of the
%% custom-text family, named in place by act 4 and left unmoved for want of an
%% observer). Lily#'s X-aware inter-system silhouette gives the text a box CENTRED
%% on its pen origin — `[ct.X - advance/2 - 0.2, ct.X + advance/2 + 0.2]`
%% (LayoutEngine.PagingSkylines' custom-text arm) — while the DRAW is
%% START-anchored: DrawCustomTexts writes with TextAnchor.Start at ct.X, and the
%% ledger's textscript.x.pen-to-notehead-left pins that pen origin ON the anchor
%% column to fifteen digits. So the reserved box sits HALF AN ADVANCE (here 6.04
%% staff spaces) left of the ink — the X half of the defect whose Y half act 4
%% retired, and the same shape the mark arm's X closed in session 204
%% (MusicMarkEngraver.MarkXExtent). No tracked book binds on it: every custom-text
%% book in the corpus puts uniform ink under the text, so the shift has nowhere to
%% show. These books are built to make it show, in BOTH directions.
%%
%% THE GEOMETRY, MEASURED IN LILY# FIRST (scratch/p272, page x in staff spaces):
%%   text pen origin  30.36, advance 12.087  ->  REAL ink span 30.36 .. 42.45
%%   Lily#'s box      24.12 .. 36.60         (the same span shifted left by 6.04)
%%   CTXL's deep note (ledger lines) 24.51 .. 26.47  -> inside the box, LEFT of all
%%                                                      the ink: Lily# charges, LP
%%                                                      must not.
%%   CTXR's deep note                37.34 .. 39.30  -> inside the ink, PAST the
%%                                                      box's right edge: LP charges
%%                                                      the text's full height, Lily#
%%                                                      only what is left of the box's
%%                                                      45° padded flank there.
%% One variable between the two books: WHICH SLOT of system 1 carries the low c,
%% (bar 1's fourth note against bar 2's second). Everything else is identical, and
%% each has a no-text control so "the text bound" is a measurement, not a claim.
%%
%% PREDICTION, written before running (HANDOFF 5.0②), and the Lily# side is already
%% measured: Lily# reads gap-first 16.039000000 (CTXL) / 15.970199011 (CTXR) against
%% 14.545000000 for BOTH controls — i.e. it charges the text 1.494 where the ink is
%% not, and 1.425 (the decayed flank) where the ink is.
%%   ⑴ LP CTXL == LP CTXLN exactly. The text's ink begins 4 staff spaces right of the
%%      deep note's column and LilyPond's skyline carries no box, so there is nothing
%%      there to push against (outside-staff padding 0.46 cannot reach 4 ss).
%%      ⇒ the residual on CTXL is the WHOLE Lily#-side charge, +1.494, and it is a
%%      pure X defect: no font quantity, no envelope, no frame.
%%   ⑵ LP CTXR > Lily# CTXR. LilyPond charges the text's real ink top at the deep
%%      note's x, the same quantity Lily# charges in CTXL — so LP CTXR should land
%%      near 16.04 and the residual should be NEGATIVE, about −0.07.
%%   ⇒ THE PAIR'S CLAIM IS THE SIGN FLIP: one shift, over-charging on the left and
%%      under-charging on the right. A port that centres nothing and starts the box
%%      at the pen origin must close BOTH, and the two controls must not move.
%% FALSIFIER: if LP CTXL exceeds LP CTXLN, LilyPond is reaching left of the ink and
%% the box is not simply mis-anchored — the port target would then be whatever
%% LilyPond pads with, not the anchor.
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).
%% ⚠️ Lily# g / a / c,, (octave absolute) = LilyPond g' / a' / c, (HANDOFF 5.5) —
%% verified by exporting the Lily# twin with `lysc ly` rather than by hand.

#(define (dump-grobs tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (memq nm '(TextScript StaffSymbol))
                              (format #t "PROBECX ~a GROB ~a rel=~a xext=(~a . ~a) yext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g sg X))
                                      (cdr (ly:grob-extent g sg X))
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

#(define (dump-pages tag layout pages)
   (let ((tm (ly:output-def-lookup layout 'top-margin)))
     (format #t "PROBECX ~a top-margin=~a\n" tag tm)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let* ((y (ly:prob-property sys 'Y-offset 0.0))
                  (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
             (format #t "PROBECX ~a SYS y=~a staff=(~a . ~a) first-staff-refpoint-below-edge=~a\n"
                     tag y (car staff) (cdr staff)
                     (+ tm y (- (cdr staff))))))
         (ly:prob-property page 'lines)))
      pages)
     (dump-grobs tag layout pages)))

probeCX =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-bottom = ##t
               indent = 0
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBECX BOOK ~a\n" tag)
                                      (dump-pages tag layout pages)) } #})

%% CTXL — the deep note LEFT of all the text's ink (bar 1's fourth note).
\book {
  \probeCX "CTXL"
  \score {
    \new Staff { g'4 a' g' c, | g'4 a' g' a' | g'4 a' g' a' | g'4 a' g' a' | \break
                 g'4 a' g' a' |
                 g'4^\markup \italic "meno mosso" a' g' a' |
                 g'4 a' g' a' | g'4 a' g' a' }
  }
}

%% CTXLN — CTXL with the text taken out and nothing else changed.
\book {
  \probeCX "CTXLN"
  \score {
    \new Staff { g'4 a' g' c, | g'4 a' g' a' | g'4 a' g' a' | g'4 a' g' a' | \break
                 g'4 a' g' a' | g'4 a' g' a' | g'4 a' g' a' | g'4 a' g' a' }
  }
}

%% CTXR — the deep note UNDER the text's ink (bar 2's second note), one variable
%% from CTXL: which slot of system 1 carries the low c.
\book {
  \probeCX "CTXR"
  \score {
    \new Staff { g'4 a' g' a' | g'4 c, g' a' | g'4 a' g' a' | g'4 a' g' a' | \break
                 g'4 a' g' a' |
                 g'4^\markup \italic "meno mosso" a' g' a' |
                 g'4 a' g' a' | g'4 a' g' a' }
  }
}

%% CTXRN — CTXR with the text taken out and nothing else changed.
\book {
  \probeCX "CTXRN"
  \score {
    \new Staff { g'4 a' g' a' | g'4 c, g' a' | g'4 a' g' a' | g'4 a' g' a' | \break
                 g'4 a' g' a' | g'4 a' g' a' | g'4 a' g' a' | g'4 a' g' a' }
  }
}

\version "2.26.0"
%% LP FIDELITY PROBE — the note-to-note MINIMUM, which is observable in one regime only.
%%
%% WHY THIS PROBE EXISTS. GlyphMetrics.MinItemGap = 0.4 is a Lily# invention: LilyPond has
%% no such knob and builds the 0.2 between two columns out of extra-spacing-width instead
%% (lily/note-spacing.cc:78-83, scm/define-grobs.scm's (-0.1 . 0.1) default). Measured on
%% probe N2N, LilyPond's column distance for two same-pitch quarter heads is a spring
%% minimum of 1.504200 and a rod of 1.604200, where Lily# computes 1.704200 for BOTH.
%% Removing the knob needs a ledger key, and the corpus has never had one:
%%
%%   * every note-to-note point here is ragged-right, i.e. at force 0, where a spring sits
%%     on its IDEAL and its minimum is never consulted;
%%   * the justified pair (justified.note-to-note.quarter/eighth, opened 2026-07-25) does
%%     not reach it either, and that was measured, not assumed — both opened EXACT. On a
%%     stretched line LilyPond sets `inverse_stretch_strength = fraction * max (0.1,
%%     len - min)` with min = the spacing INCREMENT, not the skyline minimum
%%     (lily/spacing-basic.cc), so the minimum is invisible however hard the line stretches.
%%
%% The minimum binds only where a spring is COMPRESSED onto it. Simple_spacer::compress_line
%% walks the springs in blocking-force order and each one stops at its own min_distance
%% (lily/simple-spacer.cc:232-287); a rod is a further hard floor underneath that
%% (Spaceable_grob::add_rod). So a line squeezed hard enough saturates at the minimum, and
%% the drawn gap IS the quantity — no inference needed.
%%
%% WHAT IS DUMPED. One bar of eight quarter notes at three line widths, from comfortably
%% compressed to as tight as LilyPond will set it. Each row is one line's note head x
%% positions, so the gap and its saturation are read directly, and the widths are dumped
%% back so a row can never be attributed to the wrong one.
%%
%% PREDICTION, written before the run (section 5.0): the gaps fall as the width falls and
%% stop at 1.604200 — the rod — not at the spring minimum 1.504200, because a rod is a hard
%% constraint the spacer cannot cross. Lily# will saturate at 1.704200 instead, i.e. 0.1
%% high, and the SHAPE of the residual should be flat once both saturate: if instead the
%% Lily# gap keeps tracking the width the defect is not the minimum but the compression
%% strength, and this pair is measuring the wrong thing.
%%
%% Dumps go to STDOUT, ONE RECORD PER LINE (a split record gets cut in half by LilyPond's
%% own diagnostics on stderr — see the note in barline-spacing.ly).

\header { tagline = ##f }

#(define probe-done (make-hash-table))

#(define (nf x)
   (cond ((not (real? x)) "?")
         ((inf? x) (if (> x 0) "+inf" "-inf"))
         (else (format #f "~,6f" x))))

#(define (grobs-of col sym)
   (let ((ga (ly:grob-object col sym #f)))
     (if (ly:grob-array? ga) (ly:grob-array->list ga) '())))

#(define ((dump-heads tag) g)
   (if (not (hash-ref probe-done (cons tag (ly:grob-system g)) #f))
       (begin
         (hash-set! probe-done (cons tag (ly:grob-system g)) #t)
         (let* ((sys (ly:grob-system g))
                (cols (ly:grob-array->list (ly:grob-object sys 'columns)))
                (heads '()))
           (for-each
            (lambda (c)
              (if (grob::has-interface c 'musical-paper-column-interface)
                  (for-each
                   (lambda (e)
                     (if (grob::has-interface e 'note-head-interface)
                         (set! heads (cons (ly:grob-relative-coordinate e sys X) heads))))
                   (grobs-of c 'elements))))
            cols)
           ;; The RODS, which are the only part of the spacing model Scheme can read as
           ;; numbers: Spaceable_grob::add_rod stores (other-column . distance) pairs in
           ;; 'minimum-distances (lily/spaceable-grob.cc:51-65). A spring smob prints as a
           ;; bare #<Spring> and has setters only (see JZ in line-start-mindist.ly), so the
           ;; spring's own min_distance cannot be dumped -- but Spring::length saturates at
           ;; exactly that min (lily/spring.cc:236), and a rod is a further floor under it,
           ;; so a saturated gap must equal max(spring min, rod). Printing the rods says
           ;; which of the two the plateau is.
           (for-each
            (lambda (c)
              (if (grob::has-interface c 'musical-paper-column-interface)
                  (let ((mins (ly:grob-object c 'minimum-distances '())))
                    (if (pair? mins)
                        (format #t "\nPROBE ~a ROD x=~a dists=~a\n" tag
                                (nf (ly:grob-relative-coordinate c sys X))
                                (string-join
                                 (map (lambda (p) (if (pair? p) (nf (cdr p)) "?")) mins)
                                 " "))))))
            cols)
           (let* ((xs (reverse heads))
                  (gaps (if (< (length xs) 2) '()
                            (map - (cdr xs) (reverse (cdr (reverse xs)))))))
             (format #t "\nPROBE ~a ROW width=~a n=~a xs=~a gaps=~a\n"
                     tag
                     (nf (ly:output-def-lookup (ly:grob-layout g) 'line-width))
                     (length xs)
                     (string-join (map nf xs) " ")
                     (string-join (map nf gaps) " "))))))
   '())

%% ⚠️ g', NOT c'. The first cut of this probe used c' and measured a rod of 1.956300, which
%% is NOT the plain head-to-head minimum: middle C in a treble staff carries a LEDGER LINE,
%% and a ledger sticks out past the head on both sides and joins the column's
%% horizontal-skylines like any other ink. So that rod was head + two ledger protrusions and
%% it conflated this point with the unported ledger geometry (handoff section 2E). g' sits on
%% a staff line inside the staff, stem up like c', and has no ledger at all.
%% (Handoff 5.0: confirm what a probe measures before believing it.)
cnbar = { \time 8/4 g'4 g' g' g' g' g' g' g' \bar "|." }

%% Three widths on the SAME music. CN1 is barely compressed, CN3 as tight as LilyPond will
%% set this bar; CN2 sits between them so the trend can be read rather than a single point
%% trusted. The width is chosen in mm because that is what \paper takes; at the default
%% output-scale 1.757299017 mm per staff space these are 34.14, 25.61 and 19.92 ss.
cnlay =
#(define-scheme-function (tag w) (string? number?)
   #{ \layout {
        indent = 0
        ragged-right = ##f
        line-width = #(* w 1.0) \mm
        \context { \Score \override NoteHead.after-line-breaking = #(dump-heads tag) }
      } #})

\score { \new Staff \cnbar \cnlay "CN1" #60 }
\score { \new Staff \cnbar \cnlay "CN2" #45 }
\score { \new Staff \cnbar \cnlay "CN3" #35 }
%% MEASURED at those three: 3.048125, 2.037936, 1.956300 — falling but NOT yet saturated
%% (the rod is 1.604200), and the fall is slowing because the line start compresses too
%% (the first head moves 8.489735 -> 8.035421 -> 7.489735). These three go tighter until the
%% gap stops moving; the width at which it stops is the width the ledger point uses.
\score { \new Staff \cnbar \cnlay "CN4" #30 }
\score { \new Staff \cnbar \cnlay "CN5" #26 }
\score { \new Staff \cnbar \cnlay "CN6" #22 }

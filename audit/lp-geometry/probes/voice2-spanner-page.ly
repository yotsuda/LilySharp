\version "2.26.0"
%% LP FIDELITY PROBE — a SECOND VOICE's slur/tie drooping DOWN out of a system, against the
%% inter-system spring. The voice-2 version of page-vertical.ly's SSD (slur) and TSID (tie).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe voice2-spanner-page.ly (four books, ~15 s)
%% — the probe formats its own output: PROBEV GAP lines carry the staff-refpoint
%% to staff-refpoint distances, PROBEV GROB lines each bow's own ink about its system.
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% Lily#'s preliminary annotation pass — the one whose skylines the PAGE spaces systems by —
%% lays ties and slurs out on the PRIMARY VOICE ONLY (LayoutEngine.RunPreliminaryAnnotationPass
%% builds `staffScore` from staff.PrimaryVoice; its own comment says "Ties/slurs keep the
%% primary-voice prelim score (unchanged)"), while the FINAL pass draws them from
%% `staffSpannerScore` = every voice. Beams were already lifted to the staff quantity ("expose
%% every voice so voice 2's beam protrusions join the spacing extents"); ties and slurs were
%% deliberately left behind. So a voice-2 bow that is the deepest ink of a system is DRAWN
%% into the gap but reserved NOWHERE — HANDOFF §1 ⒪, named by session 136's third delivery,
%% first seen moving in session 139's scratch book (LilyPond +2.11 ss, Lily# unmoved, the
%% drawn slur crossing into the next system's band).
%%
%% All four books: one staff, 16 bars of 12/4 cut 4+4+4+4 by explicit \break, ragged-bottom
%% (single page, so every gap is the spring's own natural length — the SSD/TSID regime),
%% ragged-right = ##f and indent 0 so all four systems are spacing-identical. Voice one holds
%% three middle-line whole notes per bar (inside the staff, no stems); voice two carries the
%% probe's ink. Music bodies are `lysc ly` output from scratch/lpreg/{vssd,vssc,vtsid,vtsic}.lys
%% — generated, not written (HANDOFF: the octave trap); only the variable names are edited.
%% Ledger reads the INTERIOR gap (systems 1 -> 2): the first system carries the meter and the
%% last the final bar line, and a bow's arc is span-dependent (the SSD carve-out).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2):
%%
%%   * VSSC (slur control, parens removed): every gap exactly 12.000000. g,, (LilyPond g,, in
%%     \fixed c' = G2) sits 8 ss below the middle line; heads alone reach
%%     8.545 + 2.05 + 1 = 11.595 < basic-distance 12, so the floor wins on both engines.
%%     A VSSC gap off 12.000000 means the two-voice texture itself reached the spacing and
%%     the PAIR is broken — fix the frame before believing VSSD.
%%   * VSSD (voice-2 slur): the slur binds. Near SSD's 13.122501 — not necessarily equal
%%     (two-voice columns may space the span a hair differently), but well past 12. A reading
%%     of exactly 12.000000 falsifies the design (the slur did not bind and the book measures
%%     nothing).
%%   * VTSIC (tie control, tie removed): every gap exactly 12.595000 = 9 + 0.545 + 2.05 + 1.
%%     e,, (E2) sits 9 ss below the middle line, so the HEADS beat the floor — which also
%%     checks that voice-2 NOTEHEADS join the spacing extents (only the bows are claimed
%%     missing; a VTSIC reading of 12.000000 would say heads are missing too, a second,
%%     bigger defect).
%%   * VTSID (voice-2 tie): the tie's droop reads ON TOP of the bound heads, near TSID's
%%     13.512560 (the TID design: flat bows barely clear a floor, so put the heads past it
%%     and read the whole droop).
%%   * All four books must print UNIFORM gaps (identical systems; \omit is not needed — the
%%     meter and final bar line only touch gaps 0 and 2, and the ledger reads gap 1... but a
%%     non-uniformity BEYOND those two gaps says the systems are not identical and the pair
%%     is mis-specified).
%%   * Lily# side (recorded in the ledger before its measurement): VSSC and VTSIC exact;
%%     VSSD stays ON THE FLOOR at 12.000000 (residual = -(LilyPond - 12)); VTSID stays ON
%%     THE HEADS at 12.595000 (residual = -(LilyPond - 12.595)). Direction: NEGATIVE — a
%%     missing reservation can only compress. If prelim ties/slurs become per-voice like the
%%     final pass, Lily# moves to LilyPond's side and the remainder is bow-shape difference.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEV PAPER top-margin=~a bottom-margin=~a paper-height=~a paper-width=~a output-scale=~a line-width=~a\n"
           (ly:output-def-lookup layout 'top-margin)
           (ly:output-def-lookup layout 'bottom-margin)
           (ly:output-def-lookup layout 'paper-height)
           (ly:output-def-lookup layout 'paper-width)
           (ly:output-def-lookup layout 'output-scale)
           (ly:output-def-lookup layout 'line-width))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEV PAGE ~a systems=~a\n" n (length lines))
           ;; staff refpoint below the page top, up to the shared top-margin: Y-offset minus
           ;; the staff-refpoint-extent's UP end (negative: the staff sits below the system
           ;; origin). Consecutive differences are the refpoint-to-refpoint gaps the ledger
           ;; wants; computed here from the raw doubles (Measure-LilyPondPageGeometry.ps1's
           ;; warning about arithmetic on rounded prints).
           (let inner ((ls lines) (i 0) (prev #f))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0)))
                        (refpos (- (ly:prob-property sys 'Y-offset 0.0) (cdr staff))))
                   (format #t "PROBEV SYS ~a ~a y=~a staff=(~a . ~a)\n"
                           n i (ly:prob-property sys 'Y-offset 0.0) (car staff) (cdr staff))
                   (if prev
                       (format #t "PROBEV GAP ~a ~a->~a ~a\n" n (1- i) i (- refpos prev)))
                   ;; Each system's Slur/Tie ink about the system refpoint, so the dump also
                   ;; says how deep LilyPond droops the bow — the term the gap is made of.
                   (let* ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (or (eq? nm 'Slur) (eq? nm 'Tie))
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a)\n"
                                                n i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i) refpos))))
           (loop (cdr ps) (1+ n))))))

%% The serif font is pinned for the reason page-vertical.ly's header gives: on the svg backend
%% LilyPond's `fonts.serif` falls back to whatever fontconfig resolves. Nothing in THIS file
%% has text at all — it is pinned so the next book added to the file cannot inherit the bug.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% ---- music bodies: `lysc ly` output, verbatim except the variable names ----

vssdMelody = \fixed c' {
  \time 12/4
  \key c \major
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | g,,1 g,,1 ( g,,1 ) | } >>
}

vsscMelody = \fixed c' {
  \time 12/4
  \key c \major
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | g,,1 g,,1 g,,1 | } >>
}

vtsidMelody = \fixed c' {
  \time 12/4
  \key c \major
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | e,,1 e,,1 ~ e,,1 | } >>
}

vtsicMelody = \fixed c' {
  \time 12/4
  \key c \major
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | } >> \break
  << { b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | b1 b1 b1 | } \\ { e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | e,,1 e,,1 e,,1 | } >>
}

%% VSSD — the voice-2 SLUR. Reserved nowhere by Lily#'s spacing pass; drawn by its final pass.
\book {
  \probeTag "VSSD"
  \paper { ragged-bottom = ##t ragged-right = ##f }
  \score {
      \new Staff { \vssdMelody }
    \layout { indent = 0\mm }
  }
}

%% VSSC — the control: the same music with only the slur parens removed. Floor regime.
\book {
  \probeTag "VSSC"
  \paper { ragged-bottom = ##t ragged-right = ##f }
  \score {
      \new Staff { \vsscMelody }
    \layout { indent = 0\mm }
  }
}

%% VTSID — the voice-2 TIE, on heads already past the floor (the TID route).
\book {
  \probeTag "VTSID"
  \paper { ragged-bottom = ##t ragged-right = ##f }
  \score {
      \new Staff { \vtsidMelody }
    \layout { indent = 0\mm }
  }
}

%% VTSIC — the control: the same music with only the tie removed. Head-bound regime.
\book {
  \probeTag "VTSIC"
  \paper { ragged-bottom = ##t ragged-right = ##f }
  \score {
      \new Staff { \vtsicMelody }
    \layout { indent = 0\mm }
  }
}

\version "2.26.0"
%% LP FIDELITY PROBE — the support a DYNAMIC clears: is it the column's REAL extent
%% (shortened forced-direction stem / quanted beam face), or a raw default stem length?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dynamic-support.ly (three books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% LilyPond's dynamic hangs off a DynamicLineSpanner whose side-position supports are the
%% note COLUMNS — heads plus the real Stem grob, whose extent is the drawn one: a forced-
%% direction stem is SHORTENED (stem.cc:519-555, the full stem-shorten 1.0 for a deep
%% head), a beamed stem ends at the quanted beam face. Lily#'s DynamicEngraver support
%% (NoteColumnLayout.RawSupportEdgeUp since session 35 — named LILYSHARP-OWN there) still
%% extends the head by the RAW DefaultStemLength 3.5: no shortening, no middle-line pull,
%% and it is blind to beams. Session 35 named it the LAST raw model of the four homes;
%% these are the points that gate switching that read (ledger DY closed only the
%% phantom-stem-on-a-whole-note half; the STEMMED half never had a point).
%%
%% THE FLOOR IS MADE TO BIND exactly as tuplet-bracket-encompass.ly does it: the two
%% staves' default-staff-staff-spacing loses basic-distance and minimum-distance, keeping
%% the shipping padding 1, so the gap the dump prints IS the skyline distance plus 1. The
%% lower staff is BASS (the treble/treble clef-pair trap is documented in
%% tuplet-number-beamed.ly). The dynamic music is a TWO-VOICE texture: the ledger's DY
%% entry already showed why — a DOWN stem deep enough to out-reach everything needs a LOW
%% head, which the default direction rule would stem UP; \voiceTwo forces it down.
%%
%% THE BOOKS (one claim, one quantity):
%%   DSQ — << {\voiceOne b'1} \\ {\voiceTwo a4\f r4 r2} >> over bass d1: the support is
%%         the real QUARTER stem of the deep a, forced down => shortened by the FULL
%%         stem-shorten 1.0 (whichStep clamps at |pos| 8), so the tip sits 4.0 + 2.5 =
%%         6.5 below the middle — not 4.0 + 3.5.
%%   DSW — the same texture with a WHOLE a1\f: no stem exists at all; the support is the
%%         head's own ink. The dynamic's OWN ink rides both books identically, so
%%         DSQ − DSW isolates the stem term with the dynamic metrics cancelled.
%%   DSB — << {\voiceOne b'1} \\ {\voiceTwo a8[ a8] r4 r2} >>: a flat manual beam, forced
%%         down; the support is the quanted BEAM's lower face at the dynamic's column.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs and forks):
%%   * DSQ: the dumped Stem row's lower end sits at −6.500000 about the staff refpoint,
%%     SIX-DIGIT. FALSIFIER FORK: −7.500000 means the forced quarter is NOT shortened in
%%     this regime — then Lily#'s raw 3.5 is LilyPond's own number here, the port claim
%%     dies for unbeamed stems, and the point closes as an identity guard instead.
%%   * DSQ gap = DynamicText ink bottom + 2.05 + 1 (decompose from the dumped rows) — the
%%     dynamic binds, not the clef pair 6.590000 (sign certain: stem 6.5 + padding alone
%%     out-reaches the treble clef's 3.540).
%%   * DSW: ZERO Stem rows in the book (structural claim); gap = whole-head ink bottom +
%%     dynamic chain + 3.05.
%%   * DSB: gap = the dumped Beam row's LOWER face + the same dynamic chain, six-digit.
%%     FORK: if the gap instead closes on a raw-length stem the support did NOT trigger
%%     quantized-positions and Lily#'s beam-blindness is LilyPond's own — record which.
%%   * Lily# mirrors (recorded in the ledger whys before measuring): DSQ residual
%%     ≈ +1.000000 (raw 3.5 − shortened 2.5); DSW residual = the dynamic-ink dowry alone
%%     (Lily#'s dynamic text face vs LilyPond's — the stacker's box-pair debt), NOT zero;
%%     DSB residual = raw 3.5 vs the quant face — sign expected positive (ideal beamed
%%     length 3.26 < 3.5 and forced beams shorten further) but NOT asserted; measured.
%%   * Every book: ONE system, TWO staves. DSQ exactly 1 Stem row, DSW 0, DSB 2 Stem
%%     rows and 1 Beam row. Extra or missing rows => the voices fought the texture —
%%     treat the book as unmeasured, do not record it.
%%
%% ⚠️ The \f is feta TEXT-path ink (DynamicText has no font-size escape hatch here); the
%% serif pin is kept load-bearing exactly as in the sibling probes.
%%
%% MEASURED (2026-07-29, session 35 — the fork fell on a branch the predictions did
%% not list): the DSQ stem tip IS -6.500000 six-digit, but the gap never reads it —
%% the spanner near edge = head ink - 0.6 in both DSQ and DSW, and their head-ink
%% difference 0.022285 propagates 1:1 into the gap; DSB's spanner edge = beam face
%% -6.74 - 0.46 (the outside-staff pass). Lily# read DSQ = DSB = 13.738000000
%% nine-digit identical (the blindness in person) with residuals
%% +2.977210 / -0.000076 / +0.899924.
%%
%% ⚠️ MECHANISM CORRECTED (2026-07-29, session 36 — books DMF/DMW below). Session 35
%% read the DSQ landing as "the dynamic engraver does not acknowledge the stem". THE
%% SOURCE SAYS OTHERWISE and a measurement confirmed the source:
%%   * dynamic-align-engraver.cc:108-117 acknowledge_rhythmic_head AND acknowledge_stem
%%     both push into support_, and :222-223 hands every one of them to
%%     Side_position_interface::add_support — the Stem IS a side-position support of
%%     DynamicLineSpanner.
%%   * grob.cc:81-85 gives every grob (Stem included) default vertical-skylines from
%%     extents; side-position-interface.cc:273-281 keeps a stem whose direction MATCHES
%%     the spanner's; :285-308 reads each support's skyline and :353-358 takes a
%%     POINTWISE Skyline::distance to my_dim = the spanner's own facing skyline, which
%%     is the DynamicText's REAL OUTLINE (define-grobs.scm:1412-1413 DynamicLineSpanner
%%     vertical-skylines from-element-stencils; :1446 DynamicText from-stencil).
%%   So in DSQ the stem tip -6.5 IS in the support skyline, at the stem's own thin X
%%   (0.13 wide): the f's outline is LOW at its left edge, the pointwise distance there
%%   never binds, and the HEAD's box wins — "head alone" was this REGIME's landing, not
%%   the mechanism. DMF is the regime where the same computation lands on the STEM.
%%
%% DMF/DMW (session 36, prediction fork written before running): the same texture with
%% \fff — wide, so the forced-down stem's X (the head's LEFT edge) falls under TALL
%% dynamic ink instead of the f's low left tail.
%%   * Branch A (stem IS a support, pointwise — the source reading): DMF's spanner must
%%     clear the stem tip at the stem's X => DMF - DMW >> 0.022285, order 1.5-2.0.
%%   * Branch B (stem structurally absent — session 35's account): DMF - DMW = 0.022285
%%     exactly, the head-ink difference, fff ink cancelling as in DSQ - DSW.
%% MEASURED: Branch A, six-digit. DMF DynamicText top = -10.844670 = stem tip -10.276
%% (rel -3.776 - 6.5) - padding 0.6 - 0.055330 (the fff outline's own local drop at the
%% stem's X — pointwise in person); DMW text top = -8.921053 = whole-head ink bottom
%% -8.321053 - 0.6, the head chain exact. DMF - DMW = 1.923617.
%%
%% ⇒ THE PORT THESE FIVE POINTS GATE (corrected): not "take the stem out" — make the
%% support POINTWISE (heads + real stems as extent boxes at their own X, staff extent
%% as minimum, distance against the dynamic's own outline), and give the below side a
%% real outside-staff pass over the staff's down profile (0.46, pointwise) for DSB.
%% What must land together: DSQ/DSW/DMW on the head chain, DMF on the stem, DSB on the
%% beam face + 0.46.
%%
%% PORTED (2026-07-29, session 37) — both halves landed together
%% (DynamicEngraver.ColumnSupportSkylines + the below collision pass over the staff's
%% real down profile, seed and draw on one spelling): DSB 0.899924 -> -0.000076 (the
%% face-sliver family exactly; Lily# separates DSQ from DSB by the same 2.077 LilyPond
%% does), DSQ +2.977210 -> +0.001512, DMF +1.031307 -> +0.001793, DSW/DMW unmoved.
%% The remaining e-3 pair is the PANGO X-EXTENT CENTERING term, decomposed against this
%% probe's own dump: LP centres the SHAPED width (DSQ DynamicText x=(8.723849 .
%% 9.987151), width 1.263302) on the head centre while Lily# centres the advance run
%% 1.280 — the pen sits 0.008349 left, and the stem's X (dump (8.7034 . 8.8334), the
%% same 0.13 sliver Lily# attaches) reads the f's left-tail slope that much further in.
%% Unfittable without Pango (this header's own instruction: do NOT bake the measured
%% widths); the family is named in the DSQ/DMF whys.

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
                   ;; DynamicText / DynamicLineSpanner / Stem / Beam ride along so the
                   ;; reading can be decomposed: rel is the grob about the SYSTEM
                   ;; refpoint, ext its own ink — together they say where the support
                   ;; ends and how far past it the dynamic sits.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (or (eq? nm 'DynamicText) (eq? nm 'DynamicLineSpanner)
                                            (eq? nm 'Stem) (eq? nm 'Beam))
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

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

zeroStaffStaff = \layout {
  \context {
    \Staff
    \override VerticalAxisGroup.default-staff-staff-spacing =
      #'((basic-distance . 0) (minimum-distance . 0) (padding . 1))
  }
}

%% DSQ — the dynamic under a forced-down, SHORTENED quarter stem: the support is the
%%     real tip 6.5 below the middle, and the gap reads (dynamic ink bottom) + 2.05 + 1.
\book {
  \probeTag "DSQ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff << { \voiceOne b'1 \bar "|." } \\ { \voiceTwo a4\f r4 r2 } >>
      \new Staff { \clef bass d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% DSW — THE CONTROL: the same texture, whole note, NO stem anywhere. The dynamic's own
%%     ink cancels in DSQ − DSW; what remains is the stem term alone.
\book {
  \probeTag "DSW"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff << { \voiceOne b'1 \bar "|." } \\ { \voiceTwo a1\f } >>
      \new Staff { \clef bass d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% DSB — the dynamic under a forced-down BEAMED pair: the support is the quanted beam's
%%     lower face at the dynamic's column.
\book {
  \probeTag "DSB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff << { \voiceOne b'1 \bar "|." } \\ { \voiceTwo a8\f[ a8] r4 r2 } >>
      \new Staff { \clef bass d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% DMF — the MECHANISM book (session 36): \fff is wide enough to put tall dynamic ink
%%     under the stem's X, so the pointwise support distance lands on the STEM tip
%%     (-6.5 - 0.6 - the fff outline's local drop 0.055330) instead of the head.
\book {
  \probeTag "DMF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff << { \voiceOne b'1 \bar "|." } \\ { \voiceTwo a4\fff r4 r2 } >>
      \new Staff { \clef bass d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% DMW — DMF's control: the same \fff on a WHOLE note, no stem anywhere. Reads the head
%%     chain (whole-head ink - 0.6) exactly like DSW — fff's extra width changes nothing
%%     without a stem to hit — so DMF - DMW isolates the pointwise stem term.
\book {
  \probeTag "DMW"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff << { \voiceOne b'1 \bar "|." } \\ { \voiceTwo a1\fff } >>
      \new Staff { \clef bass d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

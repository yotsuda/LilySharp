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

%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 3 (2026-08-04, session 92) — WHOSE PROFILE IS A SCRIPT? The books for the
%% LILYSHARP-OWN that OutsideStaffStacker's below seed has carried since session 40.
%%
%% A below-staff script that declares NO outside-staff-priority (staccato, marcato,
%% accent, the ornaments) is INSIDE-staff ink in LilyPond: axis-group-interface.cc:914-935
%% seeds all_v_skylines with exactly the grobs whose priority is unset, and the movers at
%% 250 (DynamicLineSpanner) then clear that accumulated profile by outside-staff-padding
%% 0.46. The profile a Script contributes is its STENCIL skyline — define-grobs.scm:3006
%% grob::always-vertical-skylines-from-stencil, i.e. the glyph's real OUTLINE, the same
%% object side-position hands the mover half of the same grob (script-priority.ly's SPS is
%% what proved it must be pointwise: a flat box cannot straddle a thin stem).
%%
%% Lily# spells that one profile THREE ways today, and this pair observes the third:
%%   1. ArticulationEngraver.ScriptSkylines — the real outline (movers: the fermata family)
%%   2. SkylineBuilder.AddArticulationLayoutsToSkyline — the designed INK BOX (staff skyline)
%%   3. OutsideStaffStacker's below seed — a NOMINAL +-0.6 BOX around the glyph origin,
%%      which is neither: `VerticalSkyline.FromBox(a.X +- 0.6, aYup +- 0.6)`. Its own
%%      comment says it waits for "a dynamic under a script, which the dynamic island's
%%      books do not have". These two books ARE that dynamic under a script.
%%
%% THE PAIR STRADDLES THE BOX (HANDOFF 5.0: put the points on BOTH sides of the boundary,
%% because then the finding is "the shape is wrong", not "the number is wrong"):
%%   DSK — a STACCATO forced below the whole a1: Emmentaler's dot is +-0.2, so the nominal
%%         box reaches 0.4 DEEPER than the ink. Lily# must push the \f too far DOWN.
%%   DSM — a MARCATO forced below the same note: dmarcato's box is (-0.5 . 0)x(-1.1 . 0)
%%         with the origin at its TOP, so the nominal box stops 0.5 SHORT of the V's tip
%%         (and claims 0.6 of ink ABOVE the origin, where the glyph has none). Lily# must
%%         push the \f too LITTLE — the opposite sign, from the same one constant.
%% Both are DSW plus one character, so LilyPond's whole no-stem chain (whole-head ink +
%% 0.6 + dynamic ink + 3.05) is the base and the pair's difference from DSW is the SCRIPT
%% TERM alone. Whole notes, so the structural claim of DSW holds here too: ZERO Stem rows.
%% The direction is forced with `_` rather than left to \voiceTwo, because a script's
%% default side is the one opposite the stem and there is no stem in these books.
%%
%% PREDICTIONS, written before running:
%%   * BOTH: gap > DSW's 10.783076, i.e. the script IS in the collision pass. ★ FALSIFIER,
%%     and it is the strong one: if either book reads 10.783076 to six digits, then a
%%     priority-less below script reaches a dynamic in LilyPond NOT AT ALL, Lily#'s
%%     MergeSupport is an invention with no LilyPond behind it, and the port is a DELETION
%%     rather than a re-spelling. Record which book, because the two glyphs differ in
%%     whether their ink leaves the note's own vertical band.
%%   * DSK: the dot's ink bottom - 0.46 decides the DynamicText top, so
%%     DSK - DSW = (head ink bottom - dot ink bottom) + (0.6 - 0.46) - as read off the
%%     dumped Script row, NOT fitted. Order 0.5-1.0.
%%   * DSM: the same chain with the V's tip, so DSM - DSK = the two glyphs' ink-bottom
%%     difference exactly (both are centred on the same head, both clear by 0.46).
%%     ⚠️ The pointwise term rides here and NOT in DSK: the dot is 0.4 wide and sits over
%%     the f's own centre, where the f's ink top is a plateau; the V is 1.0 wide and its
%%     underside RISES away from the tip, so if LilyPond's distance is pointwise the \f
%%     may tuck INTO the V and DSM - DSK comes out SMALLER than the ink-bottom difference.
%%     That tuck, if it happens, is the pointwise falsifier for the box shape itself.
%%   * Lily# mirrors (written before its run, both signs named): the nominal box makes the
%%     \f clear `origin - 0.6 - 0.46` in BOTH books whatever the glyph is, so
%%     DSK and DSM must read NINE-DIGIT IDENTICAL to each other — the structural half of
%%     the defect, exactly as DSQ/DSB read identical before session 37 — with residual
%%     POSITIVE on DSK (box 0.4 deeper than the dot) and NEGATIVE on DSM (box 0.5 short of
%%     the V), around +0.4 and -0.5 net of the placement term. ⚠️ Two of them reading
%%     identical is the claim; the two signs are what says "one constant, two errors".
%%   * The script's own PLACEMENT is a separate quantity and gets its own two entries
%%     (script.staccato-below.* / script.marcato-below.*) off the same books, so the gap
%%     residual can be decomposed into placement + profile rather than fitted as one lump.
%%
%% MEASURED (2026-08-04, session 92). The falsifier did NOT fire and the POINTWISE fork DID:
%%   DSK 10.932103. Script rel -8.721 ext (-0.2 . 0.2) => ink bottom -8.921000; DynamicText
%%     rel -10.966100 + top 1.896021 => ink top -9.070080, i.e. 0.149080 below the dot --
%%     NOT 0.46. The dot is 0.4 wide over the f's centre and the f's outline runs 0.310920
%%     below its own global top there, so the pass paid 0.46 AT THE DOT'S X. The move from
%%     DSW is 0.149027, and before it the f's top stood level with the dot's bottom to
%%     5.3e-5 -- the clean statement that the script IS in the collision pass.
%%   DSM 11.415903. Script ink bottom -9.621000, DynamicText ink top -9.553880: the label's
%%     ink is 0.067120 ABOVE the V's tip, TUCKED INTO the chevron. DSM - DSK = 0.483800
%%     against the glyphs' ink-bottom difference 0.700000, so 0.216200 of the V is never
%%     paid for. ⇒ No box of any size reproduces this pair: the seed's SHAPE is the defect.
%%   Both books' Script ink TOP is -4.745000 about the staff refpoint = the whole head's ink
%%     bottom -4.545053 less the script padding 0.200000 -- the SAME number for both glyphs,
%%     i.e. LilyPond's placement half is an IDENTITY and any spread Lily# shows is defect.
%%
%% ⚠️⚠️ THE Lily# MIRROR LANDED ON A BRANCH NEITHER PREDICTION LISTED, and it is a SECOND
%% DEFECT, upstream of the one these books were written for. Lily# reads DSK = DSM =
%% 10.783000000 = the CONTROL: the script moves the label by NOTHING. The two books reading
%% identical is what was predicted; the reason is not. The scripts are not where Lily# thinks:
%% the placement pair reads -1.300000 (dot) and -2.200000 (chevron) against LilyPond's single
%% -4.745000. ★ THE DOT IS HUNG ON THE WRONG VOICE'S NOTE -- ArticulationItem carries no
%% voice index, so the engraver resolves its anchor over the staff's PRIMARY voice while the
%% item index was recorded against the writing voice, and here that is voice one's middle-line
%% b' instead of voice two's a. Every term follows exactly (noteUp 0, head half 0.5, near
%% extent 0.2, padding 0.2, quantize floor(-1.8) = -2 => -1.0, on a line => -1.5, ink top
%% -1.300000 to nine digits), and the drawn page shows the dot under the b'. The \f on the
%% same note landed correctly because DynamicItem HAS a VoiceIndex -- one family, one member
%% fixed (the session-91 shape again). ⇒ PORT ORDER: the voice anchor first; only then can
%% these books see whether the seed is a box or an outline, because a script parked inside the
%% staff has its +-0.6 box swallowed by the staff's own profile either way.
%%
%% PORTED, PART 1 (2026-08-05, same session): the voice anchor. Both placement readings land
%% on ONE number (-4.700000000, LilyPond's identity reproduced) and the two gaps start
%% moving: DSK 10.913894527 (-0.018208473), DSM 11.834649074 (+0.418746074), one nominal
%% +-0.6 box now showing both signs exactly as the pair was designed to make it.
%%
%% ⚠️ PART 2 IS MEASURED AND HELD (same session). Two things were learned by trying it:
%%   ⒜ THE +-0.6 BOX IS NOT WHAT THESE BOOKS READ. Widening it to +-3.0 moves neither book:
%%      the below tracker's base is already the staff's own down profile, and the script
%%      reached it through the STAFF SKYLINE's designed ink box
%%      (SkylineBuilder.AddArticulationLayoutsToSkyline). That box is the third spelling of
%%      one grob's profile, and it is the load-bearing one here.
%%   ⒝ SWITCHING THAT BOX TO THE OUTLINE SPLITS THE PAIR: DSM 11.384312413 (-0.031591, a 13x
%%      improvement) and DSK 10.795332563 (-0.136770, WORSE by 0.12).
%%   ⒞ ⚠️ THE FIRST EXPLANATION OF ⒝ WAS WRONG AND IS RETRACTED. It said Lily# walks the
%%      curve more finely than LilyPond (freetype.cc:121-146 quantizes cubics into
%%      max(2, chord/0.2) lines). LilyPond does — and TextOutlineSkylines ALREADY PORTS it,
%%      so it cannot be the difference. What IS: LILYPOND'S OBSTACLE IS NOT THE GLYPH
%%      OUTLINE. Dumped from this very book, the Script's `vertical-skylines` property —
%%      the one axis-group-interface.cc:914-935 collects — reads
%%          0.200 flat over ±0.10 | 0.142 out to ±0.24 | 0.084 still at ±0.30
%%      i.e. WIDER than the glyph's own ±0.2 X-extent, where `ly:skylines-for-stencil` on
%%      the same grob's stencil gives the true dot (0.159 at ±0.10, EMPTY past ±0.2).
%%      The two engines' LABEL outlines were sampled point by point and agree to 5e-5, so
%%      the label is not the difference either. ⇒ The designed ink box is CLOSER to
%%      LilyPond's real obstacle than the exact outline is — which is why the box wins on
%%      the 0.4-wide dot and loses on the 1.0-wide chevron. Not skyline-horizontal-padding
%%      (Script declares none; default 0.0, stencil-integral.cc:881-893) and not a
%%      max-window pad of the stencil profile (checked against the dumped values).
%%   ⒟ ★★★ THE MECHANISM, FOUND THE SAME DAY: skyline-horizontal-padding. scm/script.scm
%%      declares it for exactly THREE scripts — staccato 0.10 (:407), staccatissimo 0.10
%%      (:392), downbow 0.20 (:86-94) — and stencil-integral.cc:881-893
%%      Grob::vertical_skylines_from_stencil applies it with Skyline::padded, whose shape is
%%      "flat h, then 45°-sloped h" on each side of every building (skyline.cc:558-615).
%%      padded(0.1) of the dumped stencil polygon (-0.2 . 0.2) IS the dumped property
%%      (-0.4 . 0.4) with 0.2 flat across ±0.1 — corner for corner. The MARCATO declares
%%      none, which is why its side was already right under the bare outline.
%%   ⇒ PORTED (2026-08-05): ArticulationEngraver.ScriptSkylines is now the PADDED outline,
%%     and all FOUR consumers read it — the staff skyline (SkylineBuilder), the below
%%     stacker's seed, the system skyline (LayoutEngine.AugmentSkylinesWithScripts) and the
%%     movers. Lily# already had VerticalSkyline.Padded as a literal port of Skyline::padded;
%%     nothing had ever handed it a script.
%%     DSK 10.895972811 (-0.036130) and DSM 11.384312413 (-0.031591): ★ THE PAIR NOW AGREES
%%     WITH ITSELF, where the box read -0.018 / +0.419. What is left is the SAME number on
%%     both books and it is the placement term already open next door — the nominal notehead
%%     half 0.5 against LilyPond's LILC 0.545053 (script.{staccato,marcato}-below both
%%     +0.045000): a script seated 0.045 too high lets the dynamic under it sit that much
%%     shallower. Close the head half-ink and both close. Snapshots: 4, all approved and
%%     all in the same direction (the dynamics move ~0.4 CLOSER to the staff, which is the
%%     old ±0.6 box's over-reservation coming off).

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
                                            (eq? nm 'Stem) (eq? nm 'Beam)
                                            ;; round 3: the Script whose profile the
                                            ;; dynamic clears (no rows in books 1-5).
                                            (eq? nm 'Script)
                                            ;; ⚠️ ADDED 2026-08-05 (session 95/96), AND ITS
                                            ;; ABSENCE COST TWO SESSIONS. This dump printed
                                            ;; the DynamicText, the Script, the Stem and the
                                            ;; Beam — everything EXCEPT the thing all of them
                                            ;; are positioned against. The head's X-extent is
                                            ;; what says a NoteHead's grob extent is its INK
                                            ;; (1.9620 whole / 1.3042 black) and not its
                                            ;; advance (1.960 / 1.304), and with the row
                                            ;; missing the residual it caused was read as
                                            ;; "the two scripts' profiles" for two sessions.
                                            ;; The ledger whys for
                                            ;; staff.staff.dynamic-{staccato,marcato}-avoid
                                            ;; quote these rows; they now come out of THIS
                                            ;; probe instead of a scratch copy of it.
                                            (eq? nm 'NoteHead))
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

%% DSK — round 3: DSW plus a STACCATO forced below. The dot's ink is +-0.2, where Lily#'s
%%     below seed claims a nominal +-0.6 box: the box is 0.4 too DEEP.
\book {
  \probeTag "DSK"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff << { \voiceOne b'1 \bar "|." } \\ { \voiceTwo a1_.\f } >>
      \new Staff { \clef bass d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% DSM — round 3's other side: the same book with a MARCATO below. dmarcato hangs 1.1
%%     under its origin, so the same nominal box stops 0.5 SHORT — the opposite sign.
\book {
  \probeTag "DSM"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff << { \voiceOne b'1 \bar "|." } \\ { \voiceTwo a1_^\f } >>
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

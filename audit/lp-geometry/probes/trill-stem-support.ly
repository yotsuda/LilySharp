\version "2.26.0"
%% LP FIDELITY PROBE — the support a TRILL SPANNER clears on the UP side: is it the
%% column's REAL extent (shortened forced-direction stem / quanted beam face), or the raw
%% default stem length 3.5?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe trill-stem-support.ly (three books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% The trill spanner's aligned_side supports are its spanned note columns floored by the
%% staff extent (ledger trill.{quiet,support}.staff-to-line, probe spanner-floors.ly),
%% and its facing DOWN profile is flat (the left-bound text is wrapped in LilyPond's own
%% "straight line as the vertical skyline" device, define-grobs.scm:4054-4068), so the
%% support reading is the SCALAR max of the column edges — which is why, unlike the
%% dynamics (probe dynamic-support.ly), a scalar edge CAN serve the trill. But Lily#'s
%% scalar edge is the wrong scalar: NoteColumnLayout.RawSupportEdgeUp — the LAST
%% consumer of the raw DefaultStemLength 3.5 after the dynamics left it in session 37 —
%% extends the head by 3.5 with no unnatural-direction shortening and no beam quant,
%% where LilyPond's Stem extent is the DRAWN one (stem.cc:519-555: shorten whenever
%% dir * hp[dir] >= 0, full 2.0 half-spaces for a quarter at |pos| >= 8, and a beamed
%% stem ends at the quanted face). spanner-floors.ly's TRC control had a natural DOWN
%% stem under the trill (up edge = head box top), so no existing point reaches the
%% stemmed half. These are the points that gate switching the trill's read.
%%
%% THE TEXTURE (same on both sides, HANDOFF 5.0 trap 5): voice one carries the music
%% with stems FORCED UP (\voiceOne here = Lily#'s first of two voices; a per-note
%% \stemUp cannot serve, because Lily#'s beam direction ignores the per-note override
%% while its VOICE forcing steers the beam — BeamDetector.DefaultBeamStemUp), voice two
%% is spacer rests only: it exists to make both engines run their two-voice forcing and
%% contributes no ink anywhere.
%%
%% THE BOOKS (one claim, one quantity — all columns under the span identical, so the
%% scalar max cannot hide an X-dependence):
%%   TLS — \voiceOne c'''4 (drawn C6, position +8) under the trill: forced-up quarter
%%         takes the FULL stem-shorten (which_step = min(1, 7-4-2) + 8 = 9, shorten =
%%         min(0.3333*9, 2.0) = 2.0 half-spaces), so the drawn tip is 4.0 + 2.5 = 6.5.
%%   TLB — the same columns as beamed eighth PAIRS (\voiceOne c'''8[ c'''] x4): the
%%         support is the quanted BEAM's upper face / stem-to-face tip, read from the
%%         dumped Stem and Beam rows.
%%   TLW — THE CONTROL: the same pitch as WHOLE notes, no stem anywhere; the support is
%%         the head's own box top. TLS - TLW isolates the stem term with the trill's
%%         padding + reach chain cancelled (the DSQ - DSW shape, up side).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs and forks):
%%   * TLS: TrillSpanner rel - staff refpoint = 8.000000 SIX-DIGIT ROUND = shortened tip
%%     6.5 + trill padding 0.5 + glyph reach 1.0. The dumped Stem rows' upper ends sit
%%     at 6.500000 about the staff refpoint. FALSIFIER FORKS: 9.000000 means the forced
%%     quarter is NOT shortened in this regime — then Lily#'s raw 3.5 is LilyPond's own
%%     number here and the point closes as an identity guard; 7.960000 (= 6.5 + 0.46 +
%%     1.0, NOT round) means the outside-staff 0.46 pass outbid aligned_side — record
%%     the mechanism, the port target moves to the pass.
%%   * TLB: the reading must decompose SIX-DIGIT onto one of two named chains —
%%     aligned_side: dumped stem tip + 0.5 + 1.0; or the 0.46 pass: dumped beam upper
%%     face + 0.46 + 1.0. Record which wins (DSB's below-side answer was the pass).
%%     Sign vs Lily# certain POSITIVE either way (every candidate < 8.5 vs Lily# 9.0).
%%   * TLW: dumped whole-head box top (4.0 + half-ink ~0.545) + 0.5 + 1.0 ~ 6.045;
%%     ZERO Stem rows in the book (structural claim).
%%   * Lily# mirrors (recorded in the ledger whys before measuring): TLS = TLB =
%%     9.000000000 NINE-DIGIT IDENTICAL (head 4.0 + raw 3.5 + 0.5 + 1.0 — the raw
%%     model's beam blindness in person, the DSQ = DSB identity on the up side);
%%     residuals ~ +1.0 (TLS) / support-vs-quant (TLB). TLW residual = the LILC
%%     face-sliver family (e-4), NOT zero — do not fit it.
%%   * Every book: ONE system, ONE staff. TLS exactly 1 TrillSpanner + 4 Stem + 0 Beam
%%     + 4 NoteHead rows; TLB 1 + 8 + 4 + 8; TLW 1 + 0 + 0 + 2. Extra or missing rows
%%     => the voices fought the texture — treat the book as unmeasured, do not record.
%%
%% ⚠️ The serif pin is kept as in the sibling probes; the trill glyph and noteheads are
%% Emmentaler and do not depend on it.
%%
%% MEASURED (2026-07-30, session 38 — every prediction landed on its primary branch):
%% TLS 8.000000 (Stem rows' upper ends 6.500000 exactly; the 0.46-pass candidate lost),
%% TLB 8.240000 = quanted beam OUTER face 6.74 + 0.5 + 1.0 — ALIGNED_SIDE wins, and the
%% two chains' supports coincide because the drawn Stem extends TO the outer face (Stem
%% ext upper == Beam ext upper == 6.740000); the pass candidate 8.2 lost by exactly the
%% padding difference 0.04. TLW 6.045000; its Stem grobs dumped EMPTY extents. Lily#
%% (pre-port): TLS = TLB = 9.000000000 nine-digit identical — the blindness identity
%% fired — residuals +1.000000000 / +0.760000000 / 0.
%%
%% PORTED (same day): NoteColumnLayout.SupportEdgeUp's stem branch converts
%% OutwardTipDeviceY (one house, two frames), and TrillSpannerEngraver hands
%% ColumnUpEdge the beam-member lookup (DynamicEngraver.BuildBeamMembers). All three
%% landed 0.000000000: TLS and TLB NINE-DIGIT EXACT (the shorten clamps at exactly 2.0
%% half-spaces; Lily#'s beam quanter reproduces the 6.74 face to the digit in this flat
%% forced-up regime), TLW unmoved as the pair demanded. The corpus stayed byte-identical
%% as a RESULT — no fixture spans a trill over a shortened or beamed same-direction stem
%% column; these three entries are the only observers.
%%
%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 2 (same day, session 38 continuation) — the three regimes the first round's
%% own audit named unmeasured. TLS/TLB/TLW were X-UNIFORM (identical columns), so they
%% could not distinguish "scalar support + uniform facing reach 1.0" (Lily#'s shape)
%% from "pointwise skyline distance against a 2-piece facing profile: glyph plateau at
%% -1.0 over the glyph's X-range, wave ink elsewhere" (session 36's reading of
%% side-position-interface.cc:285-308,:353-358 for the dynamics; the trill's left-bound
%% text is skyline-wrapped, define-grobs.scm:4054-4068, but the wrapper's straight line
%% spans the TEXT, not necessarily the whole spanner). TLB's beam was FLAT, so it could
%% not test the sloped quant. And no book made a NON-SUPPORT grob (a Script) the
%% binding obstacle, which is where Lily#'s single-pass tracker pays the trill's OWN
%% padding 0.5 where LilyPond's outside-staff pass pays 0.46 (the approximation named
%% in ledger trill.support's why since session 32).
%%
%% THE BOOKS:
%%   TXG — the tall (forced-up, fully shortened) column FIRST, under the tr GLYPH;
%%         the rest low naturals. Both hypotheses put the glyph's reach 1.0 over the
%%         binding column, so TXG = 8.000000 either way — the control.
%%   TXW — the same tall column LAST, under the WAVE. THE FORK IS AN IDENTITY:
%%         TXW == TXG (8.000000) => the facing reach is X-UNIFORM (Lily#'s scalar
%%         shape is LilyPond's); TXW < TXG => the facing profile is pointwise and the
%%         reach over the wave is the wave's own ink — then Lily# OVER-reserves this
%%         regime by (1.0 − wave reach) and the entry opens a defect, decomposed from
%%         the dump (line − tall tip 6.5 − padding 0.5 = the measured wave reach).
%%   TSB — SLOPED beams (forced-up descending pairs c'''8[ a''], the HIGH member
%%         first, so the max face sits in the glyph zone under both hypotheses and
%%         the X question stays out of this claim): the support is the quanted face
%%         at the HIGH member's X, read from the dumped Stem/Beam rows. Measures the
%%         sloped-quant agreement the flat TLB could not.
%%   TSP — a FERMATA on the first note of a trill over natural-DOWN columns (no
%%         voice-two: the fermata must sit ABOVE, and Lily# puts voice-1-forced-up
%%         articulations below). The Script is NOT a side-support of the trill, so
%%         LilyPond reaches it only through avoid_outside_staff_collisions:
%%         line = Script ink top + 0.46 + 1.0 (Script row rides in the dump).
%%         FALSIFIER: 3.550000 means the fermata never out-reached the quiet chain —
%%         treat as unmeasured. Lily#'s stacker pays 0.5 against everything, so its
%%         mirror should read Script top + 0.5 + 1.0: residual +0.04 EXACTLY names
%%         the single-pass approximation; the fix (pay outside-staff 0.46 in the
%%         stacker's collision pass — the quiet 0.5 already lives in the engraver's
%%         aligned_side) is LilyPond's own two-stage split, gated on this point.
%%
%% PREDICTIONS (before running): TXG 8.000000 six-digit; TXW EITHER 8.000000 (identity
%% with TXG) OR 6.5 + 0.5 + (wave ink reach, order 0.0-0.2) ~ 7.0-7.2, NOT round;
%% TSB = dumped max Stem upper end + 0.5 + 1.0, six-digit; TSP = dumped Script top +
%% 0.46 + 1.0, six-digit, NOT reachable by any 0.5 chain. Row counts: TXG/TXW 4 Stem
%% (the three low naturals carry stems too) + 1 TrillSpanner; TSB 8 Stem + 4 Beam;
%% TSP 8 Stem (two bars of quarters) + 1 Script + 1 TrillSpanner.
%%
%% MEASURED (2026-07-30, round 2 — two forks fell on branches the predictions did not
%% list, and the bisection books TXN/TXE/TXS decomposed both):
%%   TXG 8.000000 (the control, as predicted, Lily# 0 exact).
%%   TXW 4.720721 — a THIRD branch, decomposed below. TXN (natural-down tall) and TXE
%%     (tall outside the span) both read 3.550000 quiet; TXS (everything shifted a
%%     measure right) repeats 4.720721 to THIRTEEN digits, killing the absolute-X
%%     (pure xc=0) hypothesis. Lily#'s scalar-max mirror reads 8.000000000: residual
%%     +3.279279 IS the X-blindness. ⚠️ Round 2 read the chain as LEDGER top 4.05 +
%%     the trill's own padding 0.5 + a wave reach 0.170721 (aligned_side gone
%%     pointwise); see the ROUND 3 correction below — that sum is right and all three
%%     terms are wrong.
%%   TSB 8.221189 = the HIGH member's dumped Stem upper end 6.721188658669575 + 1.5,
%%     FIFTEEN-digit — the support is the STEM's own end at ITS X, not the Beam
%%     envelope corner 6.74 (0.019 of slope over half a stem-width apart). Lily#'s
%%     sloped quant face agrees to 3e-10.
%%   TSP: THE FALSIFIER FIRED — the trill stays at 3.550000 and the FERMATA clears the
%%     trill (Script bottom 5.111 ≈ tr glyph top 4.65 + 0.46): fermata declares
%%     (outside-staff-priority . 75) > trill 50 (scm/script.scm). Lily# seeds scripts
%%     immovable, so its trill lifts over the fermata instead (5.235, +1.685) — the
%%     priority-inversion defect, gated.
%%
%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 3 (2026-07-30, session 39) — TXW's decomposition was WRONG IN EVERY TERM and
%% right in its sum, which is exactly the failure HANDOFF 5.2 warns about: two errors
%% that cancel. Round 2 inferred the chain (4.05 + 0.5 + 0.170721); round 3 READ IT,
%% by dumping the grobs' own skylines with ly:skyline->points instead of reasoning from
%% extents. What the dump says:
%%   * ALIGNED_SIDE gives the QUIET 3.550000 in TXW. The support set is the spanned
%%     NOTE COLUMNS (scheme-engravers.scm:1830 side-support-elements — the column grob,
%%     so the Stem-direction skip at side-position-interface.cc:273-281 never applies),
%%     and the tall column's ink is entirely RIGHT of the line's end: column x left
%%     17.841735 vs TrillSpanner x right 17.793100 (the 0.0486 gap round 2 saw). So the
%%     staff extent decides: 2.05 + padding 0.5 + glyph plateau 1.0.
%%   * The remaining 1.170721 is the OUTSIDE-STAFF COLLISION PASS, and the obstacle is
%%     the LEDGER. ⚠️ LedgerLineSpanner declares X-extent #f and Y-extent #f but
%%     vertical-skylines FROM STENCIL (define-grobs.scm:2072-2074): ledger lines are
%%     INVISIBLE to every extent computation and PRESENT in the staff skyline. That is
%%     why no extent-based reading could find them, and why they can bind only here.
%%     Dumped ledger skyline: x (17.515685 . 19.471985) = the head extent widened by
%%     length-fraction 0.25 (define-grobs.scm:2068), UP height 4.100000 = position 8 +
%%     half of ledger-line-thickness (1.0 . 0.1) = 1.0*line-thickness + 0.1*staff-space
%%     = 0.2 (staff-symbol.cc:337-344 get_ledger_line_thickness). NOT 4.05.
%%   * The mover's own profile is its vertical-skylines (axis-group-interface.cc:770-773
%%     add_grobs_of_one_priority), a real 2-piece OUTLINE: flat -1.000000 over the
%%     glyph's true X extent, then the repeated scripts.trill_element as a wavy polygon
%%     (line-interface.cc:48-108 make_trill_line, elt aligned Y CENTER). At the ledger's
%%     left edge it reads -0.160721 — on the rising building between the dumped points
%%     (8.764100 . -0.360000) and (9.192100 . 0.152000), grob X origin 8.585000. There
%%     is no constant wave reach: the binding value is wherever the obstacle starts.
%%   ⇒ 4.100000 + outside-staff-padding 0.460000 (axis-group-interface.cc:747-749) +
%%     0.160721 = 4.720721 SIX-DIGIT.
%%   ⇒ AND THIS SETTLES THE 0.46-vs-0.5 QUESTION carried since session 32: the ledger
%%     declares no outside-staff-priority, so TXW *is* the priority-less obstacle TSP
%%     asked a slur book for. The pass pays 0.46. No new book needed.
%% PORT, three named halves (only (a) is this probe's island):
%%   (a) the engraver's aligned_side goes POINTWISE (support = the spanned columns'
%%       head/stem boxes at their own X, floored by the staff extent; my_dim = the
%%       2-piece facing profile). Gate: TXW must land on the QUIET 3.550000, TXG must
%%       NOT move, TLS/TLB/TLW/TSB/TSP must NOT move. Residual after (a): -1.170721.
%%       ★ DONE (session 39, same day): TrillSpannerEngraver.AlignedSideLineY. TXW landed
%%       3.550000000 and all seven other trill entries held. ⚠️ The left bound's
%%       attach-dir CENTER had to come WITH it, not after: LilyPond centres the bound
%%       text on the bound COLUMN (line-spanner.cc:155-175), Lily# had it centred on the
%%       column's LEFT EDGE, and a glyph plateau that misses its own column's stem would
%%       have dropped TXG from 8.000000 to 6.045000 — the halves regress apart
%%       (HANDOFF 5.0's ossia lesson). The line now starts at the glyph's true right
%%       (:621-626) instead of Lily#'s invented 1.6 + 0.3.
%%   (b) LEDGER ink into Lily#'s staff skylines (SkylineBuilder) — an unported LP
%%       calculation that moves the whole corpus, not only trills.
%%       ⚠️ WRONG — it was ALREADY PORTED (found session 39 by reading Lily# instead of
%%       assuming): SkylineBuilder.AddNoteBoxToSkylines has seeded ledger boxes all
%%       along, same length-fraction widening, same thickness. What hid the obstacle was
%%       the LINE's X — Lily# stopped the wave a BoundPadding 0.5 short of the stop
%%       column, LilyPond attaches the right bound AT the column's left edge
%%       (line-spanner.cc:155-175 attach-dir LEFT, :561-562 no bound-details padding),
%%       and the ledger reaches only 0.326 left of the column. The wave ended 0.174 shy
%%       of the ink it was supposed to clear. ★ DONE (session 39).
%%   (c) the stacker's trill profile becomes the real trill_element outline instead of
%%       the flat wave box. ⚠️ This was TWO things:
%%       (c1) the stacker passed ONE profile for the collision and ANOTHER for the
%%            registration — the collision ran on a flat glyph-high box over the WHOLE
%%            span, where LilyPond hands the same v_skylines to
%%            avoid_outside_staff_collisions and to all_v_skylines
%%            (axis-group-interface.cc:770-773,:798-803). ★ DONE (session 39).
%%       (c2) the wave box (Lily#'s TrillWaveAmplitude 0.2 + half thickness) vs the real
%%            scripts.trill_element outline. Predicted 4.100000 + 0.460000 + 0.250000 =
%%            4.810000 before running, MEASURED 4.810000000, residual +0.089279000 =
%%            exactly 0.25 − 0.160721. ★ DONE (session 39): TrillWaveOutline is
%%            make_trill_line — the element baked (23 DOWN + 23 UP buildings), copies
%%            stepped by the LILC width 1.0 with the first copy's own length the OUTLINE
%%            width 1.448, Y CENTER on the line, run quantized to whole elements (which
%%            is also the 0.0486 by which LilyPond's line stops short of its bound).
%%            MEASURED: 4.720541312, residual −0.000179688 — the FLATTENING family (the
%%            binding value sits on a building of slope ~1.2, so a sub-1e-4 difference in
%%            the ledger's left edge or in a flattened vertex shows at this size; LP's
%%            recorded figure is six-digit rounded). Not fitted.
%% ─────────────────────────────────────────────────────────────────────────────────
%% STILL NOT LITERAL after session 39, and the ONE BOOK that measures both (write this
%% before touching the support code — HANDOFF §5.0: the point comes first):
%%   ⑴ THE SUPPORT SET. LilyPond's side-support here is each NoteColumn's whole skyline,
%%      so every element of the column is in it; Lily# builds the HEAD and the STEM only
%%      (DynamicEngraver.ColumnSupportSkylines, which is literal where it was written —
%%      dynamic-align-engraver.cc:108-117 acknowledges heads and stems SEVERALLY — so
%%      reusing it for the trill imported the gap).
%%      ★ NARROWED, which is what makes the book small: of the elements a NoteColumn
%%      carries, only an ACCIDENTAL can bind on the trill's side. Dots sit at the head's
%%      own height to its right (never out-reach it); a flag sits on the stem's side
%%      within the stem's X and below its tip (an up stem's flag is under the tip, a down
%%      stem's is below the head); but an accidental's ink is TALLER than the head it
%%      belongs to — a sharp reaches about 0.7 either side of its centre against the
%%      head's LILC 0.545 — so it out-reaches head AND stem exactly when the stem points
%%      DOWN. Expected size of the defect: about 0.15, the sharp's half-height less the
%%      head's.
%%   ⑵ THE LEFT BOUND'S X. attach-dir CENTER centres the tr glyph on the bound COLUMN's
%%      extent (line-spanner.cc:171-175 robust_relative_extent . linear_combination), and
%%      an accidental widens that extent to the LEFT; Lily# reads column X + half the head
%%      advance, so its glyph sits right of LilyPond's by half the accidental's width.
%%   THE BOOK (one score answers both — the session-27 trick of adding ONE thing to a
%%   book already understood): TXA = TXN's texture (single voice, natural DOWN stems, so
%%   no stem reaches up) with a SHARP on one spanned column, placed under the WAVE for ⑴
%%   and a second book with it on the START column for ⑵.
%%     * ⑴ FORK: line = accidental ink top + 0.5 + the wave's local reach => the
%%       accidental IS a support and Lily# under-reserves by ~0.15, the gap above; line =
%%       head ink top + 0.5 + reach (i.e. TXN's own number) => the accidental is NOT in
%%       the support set, the gap does not exist, and the CODE COMMENT is what needs
%%       correcting. Either branch is a finding; neither is a patch.
%%     * ⑵ FORK: the dumped TrillSpanner x left vs the start column's dumped x left tells
%%       directly whether the centre includes the accidental.
%%   ⚠️ Do NOT widen the support without this book. Lily# would then reserve for an
%%   element LilyPond may not read, and no existing point could tell which of us moved.
%%
%%            ⚠️ WORTH THE RECORD: storing the FITTED end in the layout (so the fit ran
%%            once in the engraver and again in the profile builder) made this entry fall
%%            back to its quiet 3.550000 — and it read exactly like "the ledger does not
%%            overlap at Lily#'s spacing", which sent the first diagnosis after the
%%            X-spacing island. It is NOT that, and it is NOT the obvious re-fit
%%            arithmetic either (measured: fitting a fitted length returns the same length
%%            for every span these books use). The mechanism is unisolated; the shipped
%%            shape is the literal one — the layout keeps the BOUND and each consumer fits
%%            from it, once, as make_trill_line does. Recorded as an observation, not a
%%            story (HANDOFF §5.3: do not write a cause you have not pinned).

%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 3 (2026-07-30, session 41) — the trill's face of the SAME hack the fermata's SPL
%% priced. Lily#'s ABOVE outside-staff tracker is one per SYSTEM (seeded from the system's
%% up-skyline), so a grob on the lower staff would be "cleared" over the TOP staff's ink;
%% three movers dodge that with one line each (PlaceTrills / PlaceOttavas /
%% PlaceArticulations: `if (StaffIndex != 0) continue`), which holds every lower-staff
%% trill, ottava and fermata OUT of the collision pass entirely. LilyPond has no such
%% problem: Axis_group_interface::skyline_spacing runs on the staff's own VerticalAxisGroup, once per
%% staff (axis-group-interface.cc:836-985), so its support is that staff's ink and nothing
%% else. script.lower-staff.staff-to-ink-bottom (book SPL of script-priority.ly) opened
%% this for the Script; the three guards are three faces of ONE defect, so removing one
%% without the other two leaves the other two unexplainable — hence this book and
%% ottava-floor.ly's OTL, in the same commit.
%%
%% ⚠️ FIRST ATTEMPT, DISCARDED — AND WORTH KEEPING AS AN OBSERVATION. The obvious book was
%% "TXW's texture on the lower staff", and LilyPond read the QUIET 3.550000 for it: not
%% because its pass looked across staves, but because the book had left TXW's regime. The
%% dump says it exactly. TXW's wave ends at x 17.793100 and its stop column starts at
%% 17.841735, i.e. the wave stops 0.048600 short of the bound and the tall column's LEDGER
%% overhangs its head by 0.326 — so the overlap that binds is 0.277. On two staves the
%% system-start bar takes width, the stop column lands at 17.716735 instead (0.125000
%% left), the run quantizer (1.448 + n × 1.000, session 39) therefore fits ONE FEWER
%% element and the wave ends at 16.793100 — 0.923600 short of the bound, well clear of the
%% ledger's 0.326 overhang. Nothing binds, and the reading is the quiet value.
%% ⇒ TWO things to carry forward: TXW's own binding has only 0.277 of slack in X (its
%% −0.000180 residual sits on a slope for the same reason), and HANDOFF 5.0's "check that
%% both sides of a pair are the same MUSIC" needs "… and the same SPACING": the pitches and
%% the voices matched exactly, and the book still measured a different regime.
%%
%% SO THE OBSTACLE HAS TO BE WIDE, and there is exactly one wide obstacle a trill's
%% collision pass can see that its aligned_side cannot. TrillSpanner is priority 50, the
%% LOWEST in the table, so no other outside-staff grob is ever placed before it; and it
%% pays 0.5 in aligned_side against 0.46 in the pass, so for anything inside its own
%% support set the engraver always wins. What is left is ink on its own staff that is NOT in
%% its side-support — and the support is per-VOICE (Trill_spanner_engraver lives in the
%% Voice context, engraver-init.ly:376; scheme-engravers.scm:1816,1824-1830 collects that
%% voice's note columns). A tall column in the OTHER VOICE of the same staff is therefore
%% invisible to aligned_side and fully visible to the pass, across a whole notehead's width
%% of X. That is the pair:
%%
%%   TXV — the single-staff base: voice one carries the trill over four low c' columns
%%         (its own support is low, so aligned_side alone would read the quiet 3.55), and
%%         voice two puts a tall c''' (drawn position 8, head ink top 4.545, two ledgers)
%%         under the middle of the span.
%%   TVL — TXV moved to the LOWER staff of a two-staff system, the upper staff deliberately
%%         QUIET (middle-line quarters): its ink is what the per-system tracker would
%%         wrongly let this trill clear. Read about the LOWER staff's own refpoint, so the
%%         inter-staff distance cannot enter the reading.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs and forks):
%%   * TXV == TVL to the digit. This is the claim, and it is the strongest shape of pair —
%%     LilyPond being identity on the thing that changed (which staff the music is on), any
%%     Lily# difference IS the defect's size. FALSIFIER: they differ ⇒ LilyPond's own pass
%%     does see cross-staff ink and the three guards approximate something real; record the
%%     mechanism before touching the trackers. (SPL asked this for the Script and the
%%     falsifier did not fire; asking it for a second grob family is what makes it a claim
%%     about the PASS rather than about scripts.)
%%   * The VALUE: ≈ 5.0-5.2 = voice two's head ink top 4.545000 + outside-staff padding
%%     0.460000 + the wave's own underside where that head starts (≈ 0.16 — NOT a constant;
%%     session 39 established the binding value is read at the obstacle's X off the
%%     trill_element outline). FORK: 6.045000 (= 4.545 + the trill's own padding 0.5 + the
%%     stencil-offset reach 1.0) means the other voice IS in the side-support after all,
%%     aligned_side decides, the pass contributes nothing — then this book cannot price the
%%     guard either, and session 39's per-voice support claim needs revisiting. FORK:
%%     3.550000 means the tall column is invisible to BOTH stages, which no reading of
%%     axis-group-interface.cc allows — treat the book as unmeasured and dump the skylines.
%%   * Lily# mirrors (predicted, written before their run): TXV lands on LilyPond's number
%%     (session 39's port made staff 0's pass pointwise over the real staff profile, and
%%     session 39 also made the trill's support per-voice, so both stages should already be
%%     literal here); TVL reads the quiet 3.550000000, because the guard holds it out of the
%%     pass entirely, giving a residual of about −1.5. FORK: if TVL matches TXV, the guard
%%     is NOT load bearing and the price lives somewhere else — find it before deleting the
%%     line.
%%
%% MEASURED (2026-07-30, session 41). THE IDENTITY HELD AND THE VALUE FORK MISSED — both
%% halves are findings:
%%   TXV 6.005000 and TVL 6.005000, equal to FIFTEEN digits (rel −1.100000 about staff
%%     −7.105000, and rel −9.209333 about the lower staff's −15.214333). ⇒ LilyPond's
%%     outside-staff pass sees its own staff's ink and nothing else, for the TrillSpanner as
%%     it does for the Script (SPL). The falsifier did not fire; the guards are pure hacks.
%%   THE VALUE is 6.005000 = the other voice's column ink top 4.545000 (LilyPond's own dump
%%     prints that NoteColumn's ext as (0.0 . 4.545)) + outside-staff-padding 0.460000 + the
%%     trill's stencil-offset reach 1.000000 — i.e. the obstacle binds under the "tr" GLYPH,
%%     not under the wave, so the profile that clears it is the flat plateau at line − 1.0,
%%     the same 1.0 that puts the quiet trill at 2.05 + 0.5 + 1.0. Voice two's FIRST column
%%     (x 8.585000 .. 9.889200) overlaps the glyph's outline (x 8.392528 .. ≈9.840528) by
%%     1.25, and the glyph zone's demand (6.005) is far above what the same head would ask
%%     under the wave (≈4.545 + 0.46 + 0.16 ≈ 5.165). The ≈5.0-5.2 prediction had simply
%%     forgotten that the other voice's first note lies under the glyph.
%%   ⇒ ★ AND THAT MAKES THE BOOK ROBUST, which the discarded first attempt was not: the
%%     plateau is FLAT over 1.25 of X, so the reading cannot be moved by a tenth of spacing
%%     or by the run quantizer. A pair whose binding lives on a flat plateau is the shape to
%%     reach for when the point has to survive a texture edit.
%%   ⇒ The 0.5-vs-0.46 question is answered a second time, here in the glyph zone: 6.045000
%%     (the aligned_side fork) did NOT happen, so the other voice really is outside the
%%     side-support and the collision pass really pays 0.46.
%%
%% PORTED (2026-07-30, same session): the above pass keeps ONE TRACKER PER (SYSTEM, STAFF),
%% built on first use from that staff's own BuildStaffSkylines profile, and all FOUR guards
%% are gone (the fourth, PlaceTextSpanners, had never been named). Lily#: TXV unmoved at
%% 6.005000000, TVL 3.550000000 -> 6.005000000, both residual 0. The other two families
%% closed in the same commit (script.lower-staff -0.261 -> +8e-9, ottava.lower-staff
%% -1.727520 -> +0.027480 = OTC's residual to the digit).

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
                   ;; TrillSpanner / Stem / Beam / NoteHead ride along so the reading
                   ;; can be decomposed: rel is the grob about the SYSTEM refpoint,
                   ;; ext its own ink — together they say where the support ends and
                   ;; how far past it the trill line sits.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(TrillSpanner Stem Beam NoteHead Script NoteColumn))
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

%% TLS — the trill over forced-up, fully SHORTENED quarter stems: the support is the
%%     drawn tip 6.5 above the middle, and the line reads 6.5 + 0.5 + 1.0.
\book {
  \probeTag "TLS"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne c'''4\startTrillSpan c''' c''' c'''\stopTrillSpan \bar "|." }
      \\ { s1 }
    >>
  }
}

%% TLB — the same columns BEAMED (forced-up pairs): the support is the quanted beam's
%%     upper face / stem-to-face tip at the columns.
\book {
  \probeTag "TLB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne c'''8[\startTrillSpan c'''] c'''[ c'''] c'''[ c'''] c'''[ c'''\stopTrillSpan] \bar "|." }
      \\ { s1 }
    >>
  }
}

%% TLW — THE CONTROL: the same pitch as whole notes, NO stem anywhere. The trill's own
%%     padding + reach chain cancels in TLS - TLW; what remains is the stem term alone.
\book {
  \probeTag "TLW"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne c'''1\startTrillSpan | c''1\stopTrillSpan \bar "|." }
      \\ { s1 | s1 }
    >>
  }
}

%% TXG — round 2, the X control: the tall shortened column FIRST (under the tr glyph),
%%     the rest low naturals. Both hypotheses read 8.000000 here.
\book {
  \probeTag "TXG"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne c'''4\startTrillSpan c' c' c'\stopTrillSpan \bar "|." }
      \\ { s1 }
    >>
  }
}

%% TXW — round 2, the X probe: the same tall column LAST (under the WAVE). The fork is
%%     the identity with TXG — equal = X-uniform reach; lower = pointwise wave reach.
\book {
  \probeTag "TXW"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne c'4\startTrillSpan c' c' c'''\stopTrillSpan \bar "|." }
      \\ { s1 }
    >>
  }
}

%% TSB — round 2, the SLOPED beams: forced-up descending pairs, high member first so
%%     the max quanted face sits in the glyph zone under both hypotheses.
\book {
  \probeTag "TSB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne c'''8[\startTrillSpan a''] c'''[ a''] c'''[ a''] c'''[ a''\stopTrillSpan] \bar "|." }
      \\ { s1 }
    >>
  }
}

%% TSP — round 2, the NON-SUPPORT obstacle: a fermata on the first note of a trill over
%%     natural-down columns. LilyPond reaches the Script only through the 0.46
%%     outside-staff pass; Lily#'s single-pass tracker pays the trill's own 0.5.
\book {
  \probeTag "TSP"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { c''4\fermata\startTrillSpan c'' c'' c''\stopTrillSpan | c'4 c' c' c' \bar "|." }
  }
}

%% TXN — TXW's bisect ①: the tall column keeps its HEAD but loses the up stem (single
%%     voice, natural DOWN direction). If TXW's 4.72 is head-driven this repeats it;
%%     if stem-driven this reads 3.55.
\book {
  \probeTag "TXN"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { c'4\startTrillSpan c' c' c'''4\stopTrillSpan \bar "|." }
  }
}

%% TXE — TXW's bisect ②: the trill STOPS one note earlier, so the tall column is
%%     neither a support nor a bound. If 4.72 persists it is a pure collision-pass
%%     effect; if this reads 3.55 the mechanism lives in the bound/support set.
\book {
  \probeTag "TXE"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne c'4\startTrillSpan c' c'\stopTrillSpan c''' \bar "|." }
      \\ { s1 }
    >>
  }
}

%% TXV — round 3, the single-staff base: the trill's own voice is LOW (so aligned_side
%%     alone would read the quiet 3.55) and the OTHER voice carries a tall c''' under the
%%     middle of the span. Per-voice support ⇒ invisible to aligned_side, visible to the
%%     collision pass, over a whole notehead's width of X.
\book {
  \probeTag "TXV"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne c'4\startTrillSpan c' c' c'\stopTrillSpan \bar "|." }
      \\ { \voiceTwo c'''4 c''' c''' c''' }
    >>
  }
}

%% TVL — round 3: TXV on the LOWER staff of a two-staff system. Read about the LOWER
%%     staff's own refpoint, so the inter-staff distance cannot enter the reading. The
%%     upper staff is deliberately QUIET (middle-line quarters) — its ink is what the
%%     per-system tracker would wrongly let this trill clear.
\book {
  \probeTag "TVL"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { b'4 b' b' b' \bar "|." }
      \new Staff <<
        { \voiceOne c'4\startTrillSpan c' c' c'\stopTrillSpan \bar "|." }
        \\ { \voiceTwo c'''4 c''' c''' c''' }
      >>
    >>
  }
}

%% TXS — TXW's bisect ③: the identical configuration shifted one measure right. If the
%%     line's height (about the refpoint) moves with the shift, the mechanism reads an
%%     ABSOLUTE X somewhere (the pure evaluation's xc=0 shortcut,
%%     side-position-interface.cc:243-246); if it repeats 4.720721, the quantity is
%%     intrinsic to the bound/support configuration.
\book {
  \probeTag "TXS"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne r4 r r r | c'4\startTrillSpan c' c' c'''\stopTrillSpan \bar "|." }
      \\ { s1 | s1 }
    >>
  }
}

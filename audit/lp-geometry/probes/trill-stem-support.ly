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
%%   TXW 4.720721 — a THIRD branch: = the stop column's LEDGER ink top 4.05 (4.0 +
%%     StaffLineThickness/2) + 0.5 + the WAVE's own ink reach 0.170721. Aligned_side is
%%     POINTWISE for the trill exactly as for the dynamics: the tall head and stem lie
%%     entirely beyond the spanner's ink (gap 0.0486) and impose NOTHING — the stem tip
%%     6.5 pokes above the trill line in LilyPond's own output — while the ledger line
%%     reaches ~0.35 left into the wave. TXN (natural-down tall, wider gap) and TXE
%%     (tall outside the span) both read 3.550000 quiet; TXS (everything shifted a
%%     measure right) repeats 4.720721 to THIRTEEN digits, killing the absolute-X
%%     (pure xc=0) hypothesis. Lily#'s scalar-max mirror reads 8.000000000: residual
%%     +3.279279 IS the X-blindness, gated for the pointwise-trill-support port.
%%   TSB 8.221189 = the HIGH member's dumped Stem upper end 6.721188658669575 + 1.5,
%%     FIFTEEN-digit — the support is the STEM's own end at ITS X, not the Beam
%%     envelope corner 6.74 (0.019 of slope over half a stem-width apart). Lily#'s
%%     sloped quant face agrees to 3e-10.
%%   TSP: THE FALSIFIER FIRED — the trill stays at 3.550000 and the FERMATA clears the
%%     trill (Script bottom 5.111 ≈ tr glyph top 4.65 + 0.46): fermata declares
%%     (outside-staff-priority . 75) > trill 50 (scm/script.scm). Lily# seeds scripts
%%     immovable, so its trill lifts over the fermata instead (5.235, +1.685) — the
%%     priority-inversion defect, gated. The 0.46-vs-0.5 single-pass question remains
%%     unmeasured and needs a priority-LESS obstacle (a slur bow) — do not conflate.

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

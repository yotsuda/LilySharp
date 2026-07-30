\version "2.26.0"
%% LP FIDELITY PROBE — where a FERMATA's ink sits over (and under) the staff: the books that
%% gate making a declared-priority Script a MOVER in the outside-staff pass instead of an
%% immovable seed.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe script-priority.ly (four books, ~12 s).
%%
%% WHY, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% Session 38's round 2 found the priority inversion through the trill (ledger
%% trill.fermata-priority.staff-to-line, book TSP of trill-stem-support.ly): a fermata
%% declares (outside-staff-priority . 75) in scm/script.scm — the ONLY script family that
%% declares one at all, seven entries (fermata, shortfermata, longfermata, verylongfermata,
%% veryshortfermata, henzelongfermata, henzeshortfermata), all 75 — so LilyPond places the
%% trill (50) first and the FERMATA clears it, while Lily# seeds every script as immovable
%% occupancy (OutsideStaffStacker.SeedAboveTrackers) and lifts the trill over the fermata
%% instead. That entry observes the TRILL. Nothing observes the FERMATA — and joining the
%% pass moves EVERY fermata in the corpus, because add_grobs_of_one_priority pays
%% outside-staff-padding 0.46 against the accumulated skylines, which START from the
%% INSIDE-staff ones (axis-group-interface.cc:914-950): the staff symbol itself, the heads,
%% the stems, the ledgers. A grob whose aligned_side put it 0.40 over its support is
%% therefore RAISED to 0.46 over that same ink by the pass. These four books are the
%% observers for that move.
%%
%% ⚠️ TWO STAGES, and both are pointwise in LilyPond: aligned_side pays the script's own
%% padding (scm/script.scm fermata 0.40) against its side-support skylines — the heads and
%% the stem (script-engraver.cc:181-192,:234-250 acknowledge_stem / _rhythmic_head, Script
%% declaring add-stem-support #t) floored by include_staff (staff-padding 0.25,
%% side-position-interface.cc:219-222,:323-330) — and THEN the collision pass pays 0.46
%% against everything. Script's profile in both is its STENCIL skyline
%% (define-grobs.scm:3006 grob::always-vertical-skylines-from-stencil), i.e. the glyph's
%% real OUTLINE, whose dumped DOWN side (session 40, ly:skyline->points over the TSP book)
%% is NOT a box: the ufermata's underside reads -0.076 at the centre where the DOT hangs,
%% rises to +1.014 under the arch (|x| ~ 0.27..1.18), and drops back to -0.012 at the legs.
%% Its Y-extent bottom is -0.075 — a thousandth above the flattened skyline's -0.076, and
%% that sliver rides in every prediction below.
%%
%% THE TEXTURE. One staff, one voice, quarters, no dynamics/marks/spanners, so the ONLY
%% grobs in the pass are the staff, the columns and the fermata. SPS and SPD force the stem
%% with a two-voice split whose voice two is spacer rests (HANDOFF 5.0 trap 5 — the same
%% device trill-stem-support.ly uses: a per-note override cannot serve both engines).
%%
%% THE BOOKS (one claim each; the quantity is the fermata's INK BOTTOM above the staff
%% refpoint, or its ink TOP below the refpoint for SPD):
%%   SPQ — THE CORPUS-WIDE ONE: a fermata over a MIDDLE-LINE head (natural down stem, so the
%%         stem is on the far side and only the head and the staff can support it).
%%         Everything is low, so the STAFF decides — the regime almost every fermata in the
%%         corpus is in.
%%   SPH — a fermata over a HIGH head (LP c''' = drawn position 8, two ledgers). Now the
%%         HEAD decides, and the 0.40-vs-0.46 question is asked with nothing else nearby.
%%   SPS — a fermata over a FORCED-UP stem (voice one, middle-line head): the support is the
%%         drawn stem TIP, and the stem is a THIN box at the head's right edge — which is
%%         where the fermata's arch is 0.8-0.9 HIGH. This is the pointwise falsifier: a
%%         scalar "support edge + near extent" model cannot straddle a stem.
%%   SPD — the DOWN mirror of SPQ: a fermata forced BELOW a middle-line head whose stem is
%%         forced UP (so the stem is again on the far side). Measures whether the pass runs
%%         on the down side too (LilyPond's is ONE pass over both directions).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs and forks; the Lily#
%% mirrors go into the ledger whys before its run):
%%   * SPQ: ink bottom 2.511000 SIX-DIGIT = staff ink 2.050000 + outside-staff-padding
%%     0.460000 + the outline/LILC sliver 0.001. FALSIFIER FORKS: 2.451000 (= 2.05 + 0.40 +
%%     0.001) means the pass does NOT reach the staff symbol and only aligned_side ran —
%%     then the port target is not the pass and this whole plan is wrong; 2.375000
%%     (= 2.05 + 0.25 + 0.075) means the staff-padding FLOOR decides and the padding 0.40 is
%%     not spent against the staff at all.
%%   * SPH: ink bottom 5.006000 = head ink top 4.545000 (drawn position 8 = 4.0 plus the
%%     dumped head half-ink 0.545) + 0.460000 + 0.001. FORK: 4.946000 (+0.40) means
%%     aligned_side's padding stands and the pass did not fire — which would contradict SPQ's
%%     primary branch, so the PAIR is the claim, not either number alone. The two ledgers
%%     (ink top 4.100000, session 39) cannot bind: they lie under the arch, 0.44+ up the
%%     fermata's own underside.
%%   * SPS: NOT a round number, and BELOW the stem-tip chain. The dumped Stem upper end t
%%     (predicted 3.333333 = the middle line's 10/3, stem.cc:519-555) is cleared at the
%%     STEM's X, where the fermata's underside is ~0.87 up its arch, so the pass gives
%%     origin ~ t + 0.46 - 0.87 and the ink bottom lands ~2.85 — LOWER than the head-side
%%     chain would put it and ~0.9 BELOW a scalar tip + 0.46 + 0.076 reading (3.868).
%%     FORK: 3.868000 (or 3.808000 with 0.40) means my_dim is a BOX for scripts after all —
%%     record it, because then Lily#'s scalar shape is right and only its numbers are wrong.
%%   * SPD: ink TOP at -2.511000, the exact mirror of SPQ. FORK: -2.451000 means the pass
%%     runs on the UP side only (it does not — one pass, both dirs — but the corpus has a
%%     down-fermata fixture and this is the point that says so).
%%   * Row counts, every book: ONE system, ONE staff, exactly 1 Script row and 4 Stem rows
%%     (all four books are four quarters; SPS/SPD's voice two is spacers = no ink anywhere).
%%     Anything else means the voices fought the texture — treat the book as unmeasured.
%%
%% ⚠️ The serif pin is kept as in the sibling probes; every glyph read here is Emmentaler
%% and does not depend on it.
%%
%% MEASURED (2026-07-30, session 40). THREE books landed on their primary branch and SPS
%% fell on a value NO fork listed — the one that decided the port's shape:
%%   SPQ 2.511000 = staff ink 2.050000 + 0.460000 + the sliver 0.001, as predicted. The
%%     collision pass DOES reach the staff symbol's own ink, and it dominates both
%%     aligned_side's 0.40 and the 0.25 floor.
%%   SPH 5.006000 = head ink top 4.545000 + 0.460000 + the sliver, as predicted. With SPQ
%%     this is the pair: the pass fires in both regimes and pays 0.46, never the 0.40.
%%   SPD -2.511000, SPQ's exact mirror: the pass runs on the down side too.
%%   SPS 3.734333 — NOT ~2.85 (the arch reading) and NOT 3.868 (the flat-box fork), but
%%     the dumped Stem upper end 3.333333 + the script's OWN padding 0.400000 + the
%%     sliver: aligned_side wins here and the collision pass adds NOTHING. THE MECHANISM,
%%     read from LilyPond's source rather than guessed (HANDOFF §5.3): add-stem-support
%%     makes aligned_side flatten the stem's skyline to its tip across ALL X —
%%     side-position-interface.cc:302-305 `skyp[dir].set_minimum_height (skyp[dir].
%%     max_height ())` — so the fermata's GLOBAL lowest point (the dot at -0.076) is what
%%     clears the tip, and the answer looks scalar. The COLLISION pass gets the stem's
%%     real thin skyline (all_v_skylines starts from the inside-staff skylines, untouched
%%     by that flattening), the fermata's ARCH straddles it, and no move is needed.
%%     ⇒ There is no single padding for a fermata: 0.46 where the obstacle is WIDE
%%     (SPQ's staff, SPH's head), the engraver's 0.40 where it is THIN (this stem).
%%     ⇒ And the mover's profile must be the glyph's real OUTLINE: a flat box — the ink
%%     box or even the outline bbox — would be pushed to tip + 0.46 = 3.793 here, turning
%%     a -0.001 residual into +0.059. That is what chose ArticulationEngraver.
%%     ScriptSkylines (and why the SEEDS read the same house: one grob, one spelling).
%%
%% Lily# mirrors, PRE-port, every one on its predicted number to the digit (the whys carry
%% the three-term decompositions): SPQ 2.250000000 (-0.261), SPH 4.900000000 (-0.106),
%% SPS 3.733333333 (-0.001 = the sliver alone), SPD -2.250000000 (+0.261).
%% AFTER the port (same day): SPQ 2.511000008, SPH 5.006000008, SPD -2.511000008 — all
%% NINE-DIGIT, the residual being the outline-FLATTENING family (the two engines walk the
%% same outline with different arithmetic: Lily# over the font path, LilyPond over
%% FreeType's) — and SPS 3.733333333 UNMOVED, which was the gate. Not fitted.
%% ⚠️ WHAT THE PORT DID NOT FIX, and no longer observes: the engraver's own aligned_side
%% still clamps against the staff's LINE CENTRE 2.0 (not its ink 2.05) spending
%% staff-padding 0.25 where LilyPond spends padding 0.40 against a support that INCLUDES
%% the staff, and it still reads a head's half-ink as the nominal 0.5 instead of the LILC
%% 0.545. Both were terms of SPQ's and SPH's pre-port residuals; the pass now dominates
%% them, so the entries closed WITHOUT them being ported. They are named in the whys. A
%% book that measures them needs a regime where aligned_side WINS and the support is the
%% staff or a head — i.e. a script whose own padding is large enough to beat 0.46 over a
%% wide obstacle (portato's 0.45 is still under it; a TextScript's 0.5 is the family that
%% already has such points).

%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 2 (2026-07-30, session 40, after the port shipped) — the book for the HACK the
%% port had to keep. Lily#'s ABOVE outside-staff tracker is seeded per SYSTEM (the system's
%% up-skyline), so its support profile is the topmost ink of the WHOLE system and a script
%% on the lower staff gets "cleared" over the upper staff's notes; measured on a two-staff
%% score, an above fermata on the bass staff landed above the TOP staff. Three movers dodge
%% this with the same one line (PlaceTrills / PlaceOttavas / PlaceArticulations:
%% `if (StaffIndex != 0) continue`), which leaves every lower-staff script at its engraver
%% position — i.e. the pre-port behaviour, in a regime no fixture and no ledger entry
%% reaches. LilyPond has no such problem: Axis_group_interface::skyline_spacing runs on the STAFF's own
%% VerticalAxisGroup (axis-group-interface.cc:836-985), once per staff, so its support is
%% that staff's ink and nothing else.
%%
%%   SPL — SPQ moved to the LOWER staff of a two-staff system: treble staff with plain
%%         notes, bass staff carrying the fermata over a middle-line head (natural down
%%         stem). Measured about the BASS staff's own refpoint, so the inter-staff gap
%%         cannot enter the reading.
%%
%% PREDICTION (before running): 2.511000 — IDENTICAL to SPQ, because in LilyPond the pass
%% is per-staff and this fermata's obstacle is its own staff's ink, exactly as in SPQ.
%% FALSIFIER: anything else means LilyPond's own pass does see cross-staff ink here, and
%% then the guard is not a hack but a (badly spelled) approximation of something real —
%% record the mechanism before touching the trackers.
%% Lily# mirror (predicted, written before its run): the guard keeps the engraver's clamp,
%% so 2.250000 and residual -0.261000 — the SAME three terms SPQ had before the port
%% (staff ink 0.05 + 0.46-vs-0.25 0.21 + the sliver 0.001). That residual IS the guard's
%% price, and it closes when the above pass goes per-(system, staff).
%%
%% MEASURED (2026-07-30, session 40): SPL 2.511000 — the prediction, and the falsifier did
%% NOT fire. The dumped staff refpoints are -3.776 (upper) and -12.776 (lower), the Script
%% sits at rel -10.190 with ext bottom -0.075, so 12.776 - 10.265 = 2.511 about its OWN
%% staff: LilyPond's pass sees this staff's ink and nothing else. ⇒ THE GUARD IS A PURE
%% HACK, not an approximation of something real. Lily# reads 2.250000000, residual
%% -0.261000000 — the engraver's clamp, exactly as predicted.

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
                   ;; Script rides with Stem / NoteHead / NoteColumn / LedgerLineSpanner so
                   ;; the reading can be DECOMPOSED: rel is the grob about the SYSTEM
                   ;; refpoint, ext its own ink.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(Script Stem NoteHead NoteColumn
                                                   LedgerLineSpanner StaffSymbol))
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

%% SPQ — the corpus-wide regime: everything low, so the STAFF decides.
\book {
  \probeTag "SPQ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { b'4\fermata b' b' b' \bar "|." }
  }
}

%% SPH — a HIGH head (drawn position 8, two ledgers): the HEAD decides.
\book {
  \probeTag "SPH"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { c'''4\fermata c''' c''' c''' \bar "|." }
  }
}

%% SPS — a FORCED-UP stem under the fermata: the pointwise falsifier (the stem is thin and
%%     sits where the fermata's arch is high).
\book {
  \probeTag "SPS"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne b'4\fermata b' b' b' \bar "|." }
      \\ { s1 }
    >>
  }
}

%% SPD — the DOWN mirror of SPQ: fermata forced below, stem forced up (far side).
\book {
  \probeTag "SPD"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff <<
      { \voiceOne b'4_\fermata b' b' b' \bar "|." }
      \\ { s1 }
    >>
  }
}

%% SPL — round 2: SPQ on the LOWER staff of a two-staff system. The quantity is read about
%%     the BASS staff's own refpoint, so the inter-staff distance cannot enter it.
\book {
  \probeTag "SPL"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { b'4 b' b' b' \bar "|." }
      \new Staff { \clef bass d4\fermata d d d \bar "|." }
    >>
  }
}

%% SPA — the ACCIDENTAL as the binding obstacle (added session 40, after the port's own
%%     corpus diff showed a 1.41 move in fixture fermata-note-spacing that none of the four
%%     books above observes). A FLAT on a high head: its ink top is 1.83 over the head's
%%     centre against the head's own 0.545, so it out-reaches head AND staff, and it is NOT
%%     a side-support of the Script (script-engraver.cc acknowledges heads and stems) — it
%%     can only be reached through the collision pass, exactly like the trill's ledger
%%     (trill.x.wave-zone). PREDICTION (before running): ink bottom 5.791000 = flat ink top
%%     5.330000 (drawn position 7 = 3.5 plus the accidental's LILC top 1.83) + 0.460000 +
%%     the sliver. FORKS: 4.506000 (= head 4.045 + 0.46 + sliver) means the accidental's ink
%%     is NOT in the pass at this X — then the pass's own X overlap is the finding, since
%%     LilyPond centres the script on the note COLUMN (which includes the accidental) while
%%     Lily# centres it on the head; 4.446000 (= head + 0.40) would mean no pass at all,
%%     contradicting SPQ/SPH.
\book {
  \probeTag "SPA"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { bes''4\fermata bes'' bes'' bes'' \bar "|." }
  }
}

\version "2.26.0"
%% LP FIDELITY PROBE — where a CHORD's fingerings go. The books for the placement rule
%% Lily# does not have at all (raised 2026-08-10, session 132).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe chord-fingering.ly (two books, ~10 s).
%%
%% WHY THIS EXISTS
%%
%% Found by eye in the side-by-side report (audit/lp-regression/lp-vs-lilysharp.html):
%% chord-repetition's first chord <c-1 e-3 g-5> prints its three digits ON the noteheads and
%% the stem, unreadable. Measured there on the paper-aligned pair, LilyPond puts the two
%% upper notes' digits ABOVE the staff and the lowest note's BELOW it, while Lily# sets each
%% digit at its OWN head's height — see HANDOFF §1. That is a placement RULE Lily# is
%% missing, not a constant that is off, and the ledger has no point measuring a chord's
%% fingering, so per HANDOFF §5.0 the point comes first.
%%
%% ⚠️ THE CORPUS BOOK IS NOT THE MEASUREMENT. chord-repetition's chord is BEAMED (so
%% add-stem-support #only-if-beamed fires and the beam joins the supports), slurred (Fingering
%% is avoid-slur #around) and carries a \p. Three mechanisms ride on the number found there.
%% These two books carry none of them: ONE whole-note chord, one voice, no stem, no beam, no
%% slur, no dynamic — so what is left is the split, the stack and the two floors.
%%
%% THE MUSIC IS GENERATED, NOT WRITTEN (HANDOFF: the octave trap). Both books come from
%% `lysc ly` on the .lys twins recorded in LpGeometryProbes.cs (ChordFingeringScore).
%% ⚠️ AND THE GENERATOR HAD TO BE FIXED FIRST, which is the session-96 story one level down:
%% `lysc ly` DROPPED a chord MEMBER's @finger — and unlike the note-level hole it dropped it
%% IN SILENCE, because an unrecognised member node fell out of an `if` that raised nothing.
%% <g@finger(1) b@finger(3) d'@finger(5)>1 exported as a bare <g b d'>1: a twin that compiles
%% and is DIFFERENT MUSIC, and a twin that would have made these points read against a chord
%% with no Fingering grob on it at all. LilyPondExporter now writes the member post-event and
%% WARNS for every member node it does not recognise.
%%
%% THE RULE BEING MEASURED, read from LilyPond's source before running (HANDOFF §5.3):
%%   * lily/new-fingering-engraver.cc:182-268 position_scripts — the scripts are sorted by
%%     their head's staff-position and, for fingeringOrientations '(up down) (the Voice
%%     default, ly/engraver-init.ly), SPLIT at center = size/2: the lower half goes DOWN, the
%%     upper half UP. Three fingerings ⇒ 1 down, 2 up.
%%   * :334-335 script-priority = 100 + d * position, so within one direction the digit
%%     NEARER the staff is placed first and the far one stacks on it.
%%   * lily/script-column.cc:131-187 order_grobs — Fingering declares no
%%     outside-staff-priority, so each script in a direction takes ALL the earlier ones as
%%     side-support. The stack step is therefore one digit's height plus Fingering's own
%%     padding 0.5 (scm/define-grobs.scm:1540-1568).
%%   * lily/side-position-interface.cc:433-451 — the staff-padding clamp (Fingering
%%     staff-padding 0.5) is applied to the grob's OFFSET, not to its ink, against the
%%     StaffSymbol's own extent (±2.05, dumped by probes/glyph-skyline.ly).
%%   * Fingering has outside-staff-interface but NO outside-staff-priority, so
%%     axis-group-interface.cc:541-561 gives it -inf and the collision pass never moves it:
%%     everything here is aligned_side alone. (The 0.46 outside-staff-padding of the fermata
%%     books does NOT appear in this island.)
%%
%% THE BOOKS (one chord each; the quantity is a digit's INK edge about the staff refpoint,
%% signed up-positive, the same reading script-priority.ly's fermatas use):
%%   CFL — <g b d'>1 with fingers 1/3/5. Every head is INSIDE the staff and the whole chord
%%         sits low, so on the UP side the staff-padding clamp beats the head chain by a full
%%         staff space (heads reach 1.545, the clamp 2.55) and on the DOWN side the head
%%         chain reaches -2.045 while the clamp on the ORIGIN sits at -2.55. ⇒ up = staff,
%%         down = HEAD. Nothing sits within 0.045 of its rival (HANDOFF §5.0: do not seat a
%%         reading on a floor it shares with the answer).
%%   CFH — <d' f' a'>1, the same three digits a fourth higher: now the top head is 3.0 above
%%         the middle line, so the UP side is decided by the HEAD, and the DOWN side is the
%%         one the clamp has to drag out of a chord that floats entirely above the middle
%%         line. ⇒ up = head, down = STAFF. The two books' roles are exchanged, which is
%%         what makes them a pair rather than two readings of one regime.
%%
%% PREDICTIONS, WRITTEN BEFORE RUNNING (HANDOFF §5.0-2, with the forks named). `h` is the
%% digit's own ink height, which the dump reports (ext) rather than my asserting it; the
%% arithmetic below uses the 1.12 that a 2.0 design-ss feta numeral gives at font-size -5.
%%   * THE SPLIT, both books: exactly TWO digits above the staff and ONE below, and the one
%%     below is the LOWEST note's — "1" in both books. The digit nearer the staff is the
%%     LOWER of the two upper notes ("3"), with "5" stacked outside it.
%%     ★ FALSIFIER: three digits on one side, or "5" inner and "3" outer, means the split is
%%     not size/2-of-sorted or the priority sign is the other way — record which.
%%   * CFL up inner ("3"): ink bottom 2.550000 = staff ink 2.050000 + staff-padding 0.500000.
%%     FORK: 2.500000 means the clamp reads the staff's LINE CENTRES, not its ink (the same
%%     0.05 the fermata books settled the other way); 2.045000 (= top head ink 1.545 +
%%     padding 0.5) means the clamp does not fire at all and a chord's digits are NOT pushed
%%     outside the staff — which would refute the whole finding.
%%   * CFL up outer ("5"): ink bottom = inner ink TOP + padding 0.5 = 2.55 + h + 0.5 ≈
%%     4.170000. ⇒ THE STEP (outer ink bottom - inner ink bottom) = h + 0.5.
%%     FORK: a step of exactly h means the stack pays no padding; a step of h + 0.2 means it
%%     pays FingeringColumn's 0.2 (i.e. these are in a FingeringColumn after all, which for
%%     '(up down) they should not be — that grob serves the LEFT/RIGHT orientations).
%%   * CFL down ("1"): ink top -2.045000 = lowest head ink bottom -1.545000 - padding 0.5,
%%     the HEAD chain, because the clamp acts on the ORIGIN and the origin implied by that
%%     ink top (-2.045 - h ≈ -3.165) is already past -2.55.
%%     ★ FALSIFIER, and it is the sharp one: -2.550000 means the clamp is on the INK, and
%%     then CFL down and CFH down are the SAME NUMBER. Compare the two books before
%%     believing either.
%%   * CFH up inner ("3"): ink bottom 4.045000 = top head (a', staff position 6 = 3.0) ink
%%     top 3.545000 + padding 0.5. FORK: 4.000000 means the head's half-ink is the nominal
%%     0.5 rather than the LILC 0.545 (the fermata books' SPH settled this at 0.545 — a
%%     second reading of it here).
%%   * CFH up outer ("5"): 4.045 + h + 0.5 ≈ 5.665000, THE SAME STEP as CFL's. Two books,
%%     one number: if the two steps differ, the step is not a padding.
%%   * CFH down ("1"): ink top -1.430000 = clamp origin -2.550000 + h. The chord floats
%%     entirely above the middle line and its lowest head's chain would put this digit at
%%     ink top -0.045 — INSIDE the staff — so this is the book where "outside the staff"
%%     is the whole of the answer. FORK: -0.045000 means there is no clamp and LilyPond
%%     simply hangs the digit off its head, as Lily# does today.
%%   * Every book: exactly THREE Fingering rows and THREE NoteHead rows, one system, one
%%     staff. Anything else means the twin is not the music this header describes — treat
%%     the book as unmeasured rather than recording it (the exporter hole above is exactly
%%     how that happens).
%%
%% ⚠️ The serif pin is kept as in the sibling probes; every glyph read here is Emmentaler
%% (the digits are fetaText) and does not depend on it.
%%
%% MEASURED: see the ledger entries fingering.chord.* — the numbers and their decompositions
%% live there, and the Lily# mirrors are written into those whys before Lily#'s own run.
%%
%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 2 (2026-08-10, session 132, after the port shipped) — WHAT A FINGERING DISPLACES.
%% The user looked at the rebuilt side-by-side report and found the next thing: on
%% chord-repetition the down digit "1" and the \p now OVERPRINT in Lily#, and they do not in
%% LilyPond. The books above measure where a digit GOES; these three measure what has to get
%% out of its way, which is the other half of the same claim (HANDOFF §5.0: placement and
%% reservation are ONE claim, and the port that shipped the first half named this gap in its
%% own commit message).
%%
%% THE MECHANISM, read from the source before measuring: Fingering declares
%% outside-staff-interface but NO outside-staff-priority, so axis-group-interface.cc:541-561
%% leaves it at -infinity and the collision pass never MOVES it — which is exactly why its
%% own placement is side-position alone. The same fact puts its ink into the INSIDE-staff
%% skylines the pass starts from (:914-950), so a grob that DOES declare a priority —
%% DynamicLineSpanner, 250 — has to clear it. ⚠️ NOT through side-position support:
%% Dynamic_align_engraver acknowledges heads and stems only (dynamic-align-engraver.cc:108-117),
%% which is the note DynamicEngraver.ColumnSupportSkylines already carries. The fingering
%% reaches the dynamic through the PASS or not at all.
%%
%% THE BOOKS (the quantity is the \p's ink TOP about the staff refpoint, up-positive, so
%% "more negative" means "pushed further down"):
%%   DYF — the corpus book's first beat, isolated: <c e g> with 1/3/5 and \p, no slur and no
%%         beam, so the digit is the ONLY thing between the chord and the dynamic.
%%   DYN — the CONTROL: same chord, same dynamic, digits removed.
%%   DYU — the same chord with ONE fingering on the TOP note. One script means centre = 0
%%         (1/2 == 0), so it goes UP and there is nothing below the staff — the book that
%%         says the difference is the DOWN DIGIT and not the presence of a fingering.
%%
%% ⚠️ THESE THREE LP NUMBERS WERE MEASURED BEFORE THIS FILE CARRIED THEM, in scratch, as the
%% diagnosis of the sighting — so unlike round 1 they are not a prediction that stood a test.
%% Saying so rather than dressing them up: what IS written ahead of its run is each book's
%% Lily# mirror, in the ledger whys, and the mechanism above, which was read out of
%% LilyPond's source before either engine was asked.
%%
%% MEASURED (LilyPond 2.26.0): DYF -5.641022, DYN -4.145023, DYU -4.145023.
%%   ⇒ the down digit pushes the dynamic 1.495999 further down, and an UP digit pushes it by
%%   NOTHING (DYU is DYN to fifteen digits). The decomposition: the digit's own ink bottom is
%%   at -5.180999 and the dynamic's ink top lands at -5.641022 — a clearance of 0.460023,
%%   which is outside-staff-padding 0.46 (the pass's number, not side-position's 0.5).
%%
%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 3 (2026-08-10, session 133) — THE STEM AS A SUPPORT, AND THE GATE ON IT.
%% What the corpus book still owes after round 1 shipped: on chord-repetition the up digits
%% sit 0.190000 lower in Lily# than in LilyPond, and the two books above cannot say why
%% because they were built with NO STEM on purpose. HANDOFF §1 named the remainder as
%% add-stem-support #only-if-beamed and had already isolated it in scratch (cr-probe.ly, books
%% CR/CRN/CRB: the corpus chord reads 2.740000, the same chord with beam+slur+dynamic removed
%% reads 2.550000, and putting back ONLY the beam reads 2.740000 again — so neither the slur
%% nor the dynamic moves this digit by a hair).
%%
%% THE MECHANISM, read from the source before measuring:
%%   * lily/new-fingering-engraver.cc:180-190 position_scripts — the STEM, and its flag, are
%%     added as side-position supports of every chord fingering UNCONDITIONALLY. There is no
%%     beam test here, and the beam is never itself a support.
%%   * scm/define-grobs.scm:1543 Fingering (add-stem-support . ,only-if-beamed);
%%     scm/output-lib.scm:463 only-if-beamed — true iff SOME side-support element has a
%%     `beam` object, i.e. iff the stem is beamed.
%%   * lily/side-position-interface.cc:302-305 — that property does NOT decide whether the
%%     stem supports. It decides HOW MUCH of it does: when it holds, the stem's skyline is
%%     FLATTENED to its own maximum (set_minimum_height (max_height ())), so the digit clears
%%     the stem's full reach at EVERY x rather than only where the thin line stands.
%%   ⇒ THE CLAIM IS ABOUT A SKYLINE'S SHAPE, NOT ABOUT A NEW SUPPORT. A port that simply
%%     "adds the beam" would spell the same number here and be a different rule.
%%
%% THE BOOKS (the corpus chord <c-1 e-3 g-5>, one voice; the quantity is a digit's INK edge
%% about the staff refpoint, up-positive, the same reading rounds 1 and 2 use):
%%   CFM — BEAMED: a second eighth on the same beat, so the two beam and the gate holds.
%%   CFF — FLAGGED: the CONTROL. The same chord, the same duration, the same stem, a rest
%%         after it — so nothing beams and the gate does not hold. ⚠️ Not a QUARTER: cr-probe's
%%         CRN changed the duration as well as the beaming, so it could not tell a stem-length
%%         effect from the gate. Same duration, same stem, same flag-or-not is the difference.
%%
%% PREDICTIONS, WRITTEN BEFORE RUNNING (HANDOFF §5.0-2, forks named).
%%   * CFM up inner ("3") ink bottom = 2.740000. ⚠️ DISCLOSED: this number is NOT a prediction
%%     that stood a test — it was measured in scratch first, as the diagnosis (cr-probe.ly
%%     CRB). What it buys here is the DECOMPOSITION: 2.740000 = the beamed stem's ink top
%%     2.240000 (= the Beam's own Y-extent top, dumped below) + Fingering's padding 0.500000.
%%     FORK: 2.500000 would mean the flattened stem stops at the beam's CENTRE, not its ink.
%%   * CFF up inner ("3") ink bottom = 2.550000 — THE STAFF CLAMP, the same answer CFL's up
%%     side gives, because with the gate open the stem is a thin line at the chord's right
%%     edge and the digit is centred on the heads, so nothing of it stands under the digit.
%%     ★ THIS ONE IS A REAL PREDICTION — no scratch book measured a flagged chord.
%%     ★ FALSIFIER, and it is the whole point of the pair: 2.740000 (or anything above 2.55)
%%     means the stem lifts the digit whether or not it is beamed — then the rule is "stem
%%     support" and only-if-beamed is not what Lily# is missing. A value BETWEEN 2.55 and 2.74
%%     means the FLAG's ink is what decides, which is a third mechanism and would need its
%%     own point.
%%   * CFM stack step (up outer "5" ink bottom - up inner "3" ink bottom) = the SAME step CFL
%%     and CFH both read, h + 0.500000. ⇒ the beam raises the FLOOR of the stack and nothing
%%     else. FORK: a different step means the beam is in the chain between the two digits,
%%     which no reading of the source above allows.
%%   * Both books: exactly THREE Fingering rows on the first chord. CFM prints a Beam row and
%%     CFF does not — read that off the dump rather than trusting the duration spelling.
%%
%% MEASURED: see the ledger entries fingering.chord.beamed.* / fingering.chord.flagged.* .

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEC SYS ~a ~a staff=(~a . ~a)\n"
                           n i (car staff) (cdr staff))
                   ;; Fingering rides with NoteHead and StaffSymbol so the reading can be
                   ;; DECOMPOSED: rel is the grob about the SYSTEM refpoint, ext its own ink,
                   ;; and `text` says WHICH digit each row is — the split is read off the
                   ;; dump rather than inferred from an order.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    ;; Beam and Stem joined the list in round 3: the claim
                                    ;; there is about the STEM's skyline and the number is
                                    ;; decomposed against the BEAM's own ink, so both have to
                                    ;; be read off the dump rather than asserted.
                                    (if (memq nm '(Fingering NoteHead StaffSymbol DynamicText
                                                   Beam Stem))
                                        (format #t "PROBEC GROB ~a ~a name=~a text=~a rel=~a ext=(~a . ~a) x=~a\n"
                                                n i nm
                                                (if (eq? nm 'Fingering)
                                                    (ly:grob-property g 'text)
                                                    "-")
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (ly:grob-relative-coordinate g sg X)))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEC BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% CFL — generated from cfl.lys by `lysc ly`; the chord sits low, so the STAFF decides above
%%       and the HEAD decides below.
\book {
  \probeTag "CFL"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major <g-1 b-3 d'-5>1 | } }
    \layout { indent = 0\mm }
  }
}

%% CFH — generated from cfh.lys; the same three digits a fourth higher, so the HEAD decides
%%       above and the staff-padding CLAMP decides below.
\book {
  \probeTag "CFH"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major <d'-1 f'-3 a'-5>1 | } }
    \layout { indent = 0\mm }
  }
}

%% ── ROUND 2: what a fingering DISPLACES (see the header) ──

%% DYF — generated from dyf.lys; the digit is the only thing between the chord and the \p.
\book {
  \probeTag "DYF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major <c-1 e-3 g-5>4\p r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% DYN — generated from dyn.lys; the control, digits removed.
\book {
  \probeTag "DYN"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major <c e g>4\p r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% DYU — generated from dyu.lys; ONE fingering, which goes UP, so nothing is below the staff.
\book {
  \probeTag "DYU"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major <c e g-5>4\p r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% ── ROUND 3: the stem as a support, and the gate on it (see the header) ──

%% CFM — generated from cfm.lys; BEAMED, so only-if-beamed holds and the stem's skyline is
%%       flattened to its full reach.
\book {
  \probeTag "CFM"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major <c-1 e-3 g-5>8 <c e g>8 r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% CFF — generated from cff.lys; the CONTROL. Same chord, same duration, same stem, a rest
%%       after it, so nothing beams and the gate does not hold.
\book {
  \probeTag "CFF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major <c-1 e-3 g-5>8 r8 r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

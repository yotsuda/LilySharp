\version "2.26.0"
%% LP FIDELITY PROBE — THE SAME-DIRECTION OPTICAL CORRECTION:
%% WHEN DOES A NOTE-TO-NOTE GAP MOVE BY 0.25, AND WHICH WAY?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe same-direction-correction.ly -Prefix PROBESD
%%
%% THE QUANTITY THIS ISOLATES (opened 2026-09-12, session 373, from a user ticket about
%% scratch/ベースタブLy/accidental.lys): in that book one eighth step read 2.777581 where
%% its neighbours read 3.027581 — a flat 0.250000 shortfall in a run of equal eighths.
%% The number is NoteSpacing's `same-direction-correction` (define-grobs.scm, 0.25), and
%% the SAME book's other same-direction steps did NOT carry it, so the ledger had no point
%% that said which ones do. Lily# ports the rule at
%% Svg/Layout/SpacingRules.Springs.cs CalculateStemCorrection, whose every arm below was
%% reproduced exactly on first measurement — these books are the guard, not a repair.
%%
%% THE RULE (lily/note-spacing.cc:162-197 same_direction_correction, :305-308 accidental
%% gate): for two adjacent columns whose stems point the SAME way, the spring moves by
%% ±same-direction-correction — but ONLY when the head positions are disjoint by MORE THAN
%% ONE staff position, and NOT when the RIGHT column carries an accidental. The sign is
%% which side is lower: ascending widens, descending tightens. An accidental on the LEFT
%% column does not gate anything. (A flag hanging from the left stem kills every arm,
%% :260-266 — that one is already priced by the ledger's flagged-stem points, and every
%% note here is an unflagged half.)
%%
%% THE BOOKS. Treble, C major, halves throughout: every head sits on or below G4, so every
%% stem is UP without a single \stemUp, and a half carries no flag. Two halves fill each
%% bar, so the measured step is the one INSIDE a bar and an accidental never crosses into
%% the next (this is what lets the gate arms sit beside their controls).
%%   SDN  e e | e f | f e |          Δ0, Δ1 up, Δ1 down — the THREE NEGATIVE CONTROLS.
%%   SDU  e g | e a |                Δ2 up, Δ3 up — widened.
%%   SDD  g e | a e |                Δ2 down, Δ3 down — tightened.
%%   SDG  e ais | e ges |            Δ3 up, Δ2 up, RIGHT column accidental — gated off.
%%   SDL  ais e | ges e |            Δ3 down, Δ2 down, LEFT column accidental — NOT gated.
%% SDG/SDL are the pair the README's "add both sides" rule asks for: the gate reads the
%% right column only, so a port that tested "either column" would pass SDG and fail SDL.
%%
%% THE QUANTITY: notehead anchors, one bar at a time — step i = anchor(2i+1) − anchor(2i).
%%
%% PREDICTIONS, written before running (RULES 5.0-2), mechanism first. The natural half
%% step is the ledger's own dotted.natural.half-gap, 4.275444999134611:
%%   * SDN: all three steps EXACTLY that number. FALSIFIER: any of them off by 0.25 means
%%     the disjoint-by-more-than-one test is not the gate and the ticket's reading is wrong.
%%   * SDU: both steps 4.525444999134611 (+0.25). SDD: both 4.025444999134611 (−0.25).
%%     Δ2 and Δ3 alike — the correction is a FLAT 0.25, not a function of the interval.
%%   * SDG: both steps back at 4.275444999134611 — the accidental gate, not a rod: the
%%     accidental rod (half head 1.3774 + 0.4 + sharp 1.1 + 0.35 = 3.2274) sits well under
%%     the spring, so anything that moves here moves as a spring.
%%   * SDL: both steps 4.025444999134611 — the left-hand accidental changes nothing.
%%
%% FIRST MEASURED as an 11-bar single book in scratch/p374 (sdc.lys + sdc-probe.ly, with
%% `\override NoteSpacing.same-direction-correction = #0.0` as the one-variable control:
%% with it zeroed all twenty-two of that book's steps read one number). This file re-takes
%% the same arrangement in books small enough to stay ONE SYSTEM on the ledger's paper.
%%
%% ragged-right, indent 0: force 0 = ideals and floors alone decide every X — the regime
%% every note-to-note point in the ledger uses.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define ((sd-dump tag) grob)
   (let ((sys (ly:grob-system grob)))
     ;; ~a, not ~,Nf: Guile prints the shortest round-tripping decimal, so the STEP
     ;; subtracted downstream carries the full double instead of a truncation wobble.
     (format #t "PROBESD ~a sys=~a x=~a\n"
             tag (object-address sys)
             (ly:grob-relative-coordinate grob sys X))))

sdn = \fixed c' { \time 4/4 \key c \major e2 e | e2 f | f2 e | }
sdu = \fixed c' { \time 4/4 \key c \major e2 g | e2 a | }
sdd = \fixed c' { \time 4/4 \key c \major g2 e | a2 e | }
sdg = \fixed c' { \time 4/4 \key c \major e2 ais | e2 ges | }
sdl = \fixed c' { \time 4/4 \key c \major ais2 e | ges2 e | }

sdBook =
#(define-music-function (tag music) (string? ly:music?)
   #{ \new Staff { \clef "treble"
                   \override NoteHead.after-line-breaking = #(sd-dump tag)
                   #music } #})

%% SDN — THE NEGATIVE CONTROLS: unison and both seconds.
\book { \paper { ragged-right = ##t indent = 0 } \score { \sdBook "SDN" \sdn } }

%% SDU — ASCENDING by a third and a fourth: the spring WIDENS.
\book { \paper { ragged-right = ##t indent = 0 } \score { \sdBook "SDU" \sdu } }

%% SDD — DESCENDING by a third and a fourth: the spring TIGHTENS.
\book { \paper { ragged-right = ##t indent = 0 } \score { \sdBook "SDD" \sdd } }

%% SDG — THE ACCIDENTAL GATE: the same ascents with an accidental on the RIGHT column.
\book { \paper { ragged-right = ##t indent = 0 } \score { \sdBook "SDG" \sdg } }

%% SDL — THE GATE'S OTHER SIDE: the accidental on the LEFT column gates nothing.
\book { \paper { ragged-right = ##t indent = 0 } \score { \sdBook "SDL" \sdl } }

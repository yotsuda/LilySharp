\version "2.26.0"
%% LP FIDELITY PROBE — where the OTTAVA BRACKET's LINE sits above the staff, in the two
%% regimes of side-position-interface.cc aligned_side: the staff-padding FLOOR and the
%% note-column SUPPORT.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe ottava-floor.ly (two tiny books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% OttavaBracket declares (staff-padding . 2.0) and (padding . 0.5)
%% (scm/define-grobs.scm), consumed by side-position-interface.cc:401-453 aligned_side:
%% the grob's REFPOINT is floored at staff_extent[UP] + staff-padding, over whatever the
%% support (the note columns) asked for. Lily#'s OutsideStaffStacker has no such floor for
%% any grob but TextScript (ported 2026-07-29, session 30, ledger
%% textscript.no-descender.staff-to-baseline); Ottava (2.0), TrillSpanner (1.0),
%% TextSpanner (0.8) and DynamicLineSpanner (0.1) were left NAMED but unmeasured. This
%% probe opens the largest of the four.
%%
%% THE ANCHOR: ottava-bracket.cc print puts the dashed LINE at the stencil's own Y=0 and
%% centres the text on it (text.align_to (Y_AXIS, CENTER), line built at Offset(len, 0)),
%% so the grob's relative coordinate IS the drawn line — the same physical anchor Lily#'s
%% DrawOttavaBrackets places (its YUp is the line's Y). ⚠️ Lily# draws the TEXT's
%% BASELINE on the line where LilyPond centres the text's INK on it — that second claim
%% is a DRAWING difference the rel dump cannot see; the ext dump rides along for it
%% (a centred text reads a roughly symmetric ext about 0, a baseline-anchored one would
%% not).
%%
%% THE PAIR: OTF engraves \ottava 1 over notes DRAWN third-space c'' (written c''' minus
%% the ottava octave) — column top ≈ 1.05 above the refpoint, so every support-side
%% constraint (+0.5 side padding, +0.46 outside-staff) is far below the floor and the
%% floor decides. OTC is the same music two octaves up (drawn c''', two ledger lines):
%% column top ≈ 4.5, the support decides, the floor loses. Bar 2 of each book is loco at
%% the drawn pitch, so both books carry one bracket over bar 1 only.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0, with signs):
%%   * OTF: rel − staffRefpoint = 4.050000 EXACT (staff ink 2.050000 + staff-padding 2.0)
%%     — six-digit round, like TextScript's 2.550000. If it reads HIGHER, something else
%%     (the text's own skyline, an outside-staff term) stands on the floor; if LOWER, the
%%     floor is not on the refpoint and the TextScript reading does not generalize.
%%   * OTC: strictly HIGHER than 4.05 (sign certain), around drawn-column-top + a padding
%%     term (≈ 5.0). The decomposition (0.5 side padding vs 0.46 outside-staff) is left
%%     to the measurement — both candidates are written here so the answer picks one.
%%   * FORK: OTF at 4.050000 → port is the TextScript floor with 2.0, same three lines.
%%     OTF ≈ OTC's shape → the floor never binds for ottava and the port target is the
%%     support arithmetic instead.
%%   * FALSIFIER: OTF == OTC means the pitch edit did not switch the regime and the pair
%%     measured nothing — treat as unmeasured, do not record.
%%
%% ⚠️ The ottava label is bold italic serif TEXT; its ink is in the grob's extent and in
%% skylines, so the serif pin is load-bearing (svg backend resolves fonts.serif via this
%% machine's fontconfig otherwise; page-vertical.ly's header has the history).

%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 2 (2026-07-30, session 41) — the ottava's face of the hack the fermata's SPL
%% priced. Lily#'s ABOVE outside-staff tracker is one per SYSTEM (seeded from the system's
%% up-skyline), so a grob on the lower staff would be "cleared" over the TOP staff's ink;
%% three movers dodge that with one line each (PlaceTrills / PlaceOttavas /
%% PlaceArticulations: `if (StaffIndex != 0) continue`). LilyPond has no such problem — its
%% pass runs on the staff's own VerticalAxisGroup, once per staff
%% (axis-group-interface.cc:836-985). The three guards are three faces of ONE defect, so
%% they come out together and each grob family needs its own observer: SPL (script-
%% priority.ly), TXL (trill-stem-support.ly) and this one.
%%
%%   OTL — OTC moved to the LOWER staff of a two-staff system: a quiet treble staff above,
%%         and below it OTC's exact music (\ottava 1 over drawn c''', two ledger lines,
%%         loco bar 2). OTC and not OTF, because OTC is the regime where Lily#'s answer
%%         COMES FROM THE PASS: OttavaBracketEngraver computes only the staff-padding floor
%%         (AboveStaffYUp = 4.05), and everything above it — the whole support side — is the
%%         collision pass's work. In OTF the floor already stands and the pass moves
%%         nothing, so the guard would be free there and the book would measure nothing
%%         (HANDOFF 5.0: do not seat the reading on a floor).
%%
%% PREDICTION (before running): 5.777520 — IDENTICAL to OTC, LilyPond's pass being
%% per-staff. FALSIFIER: anything else means its pass does see cross-staff ink, and then
%% the guards approximate something real (SPL says otherwise for the Script; this is the
%% third family asked).
%% Lily# mirror (predicted, written before its run): the guard skips the pass entirely, so
%% the bracket falls all the way back to its ENGRAVER value — the bare staff-padding FLOOR
%% 4.050000 — and the residual is −1.727520. That is far bigger than SPL's −0.261 or TXL's
%% −1.170721 because for this grob the pass is not a correction on top of an engraver
%% chain, it IS the chain. ⚠️ It also predicts a VISIBLE defect in the corpus: a lower-staff
%% 8va over ledgered notes draws its bracket THROUGH the noteheads. No fixture has one (the
%% same blind spot that shipped the lower-staff fermata bug), so the book is the only
%% observer until the port lands. FORK: if Lily# reads ≈5.8, the bracket is being placed by
%% something other than the pass on the lower staff and this whole reading of PlaceOttavas
%% is wrong — find that before deleting the guard.
%%
%% MEASURED (2026-07-30, session 41): OTL 5.777519990798647, and OTC re-read in the same run
%% is 5.777519990798646 — the SAME NUMBER TO FIFTEEN DIGITS (rel −8.891706 about the lower
%% staff's refpoint −14.669226, against rel −0.792039 about −6.569559). ⇒ LilyPond's
%% outside-staff pass sees its own staff's ink and nothing else, for the OttavaBracket as it
%% does for the Script (SPL) and the TrillSpanner (TVL). The falsifier did not fire: the
%% guards are pure hacks, not approximations of a real cross-staff term.
%% Lily#, PRE-port: 4.050000000, residual -1.727520000 -- the bare staff-padding floor, the
%% prediction to the digit, and a png confirmed the bracket drawn THROUGH the noteheads.
%%
%% PORTED (2026-07-30, same session): the above pass keeps one tracker per (system, staff),
%% built from that staff's own BuildStaffSkylines profile, and the four StaffIndex != 0 guards
%% are gone. Lily#: 5.805000000, residual +0.027480000 = OTC's residual TO THE DIGIT. ⇒ A
%% lower-staff bracket now costs exactly what a top-staff one costs, which is the shape of the
%% claim; what is left is what OTC already names, not a staff-position term.

%% ─────────────────────────────────────────────────────────────────────────────────
%% ROUND 3 (2026-08-02, session 73) — WHAT THE +0.027480 IS. The ledger's OTC entry had
%% named it as the net of THREE term-pairs, the largest being "box-vs-outline support
%% +0.0595", and told the next reader it "decomposes only when the support skyline holds
%% outlines". That was WRONG, and it would have sent someone into an unrelated port.
%%
%% The books below now dump, besides the bracket's refpoint, the two profiles the
%% constraint is actually made of: the bracket's OWN DOWN skyline (PROBEV DOWN) and every
%% note column's UP skyline (PROBEV SUP). MEASURED in OTC (system frame, staff refpoint
%% −6.569559342):
%%   NoteHead / NoteColumn UP top   −2.024559342  = staff + 4.545000000  ← the BOX
%%       (yext = (−0.545 . 0.545); NoteHead declares no vertical-skylines, so LilyPond
%%        reads its EXTENT — scm/define-grobs.scm, HANDOFF 5.2. Lily# reads 4.545 too:
%%        THE SUPPORT TERM IS ZERO. There is no box-vs-outline difference here.)
%%   OttavaBracket rely              −0.792039352  = staff + 5.777519991, ext ±0.792031364
%%   OttavaBracket DOWN profile      NOT a flat box: the "8va" label's own OUTLINE over
%%       x∈[7.785, 8.997], empty over the gap, −0.05 under the dashed line, −0.85 at the hook.
%% ⇒ The binding pair is the FIRST NOTEHEAD'S LEFT EDGE (x = 8.585) against the label's
%%   sloped outline. Interpolating the profile segment (8.277822, −1.584070715) →
%%   (8.600421587, −1.521571629) at x = 8.585 gives −1.524559342, i.e. EXACTLY 0.500000000
%%   above the notehead's −2.024559342. That 0.5 is OttavaBracket's `padding` spent in
%%   side-position-interface.cc:354-370 aligned_side (dim.distance(my_dim) + dir·ss·padding),
%%   NOT the outside-staff pass's 0.46 — 0.5 > 0.46, so the pass then moves nothing.
%%   The 0.059511373 the ledger had called a support term is the LABEL'S OWN outline rising
%%   off its lowest point by the time it reaches that x. It is on the MOVER's side.
%%
%%   LP    5.777519991 = 4.545000000 + 0.500000000 + (0.792031364 − 0.059511373 = 0.732519991)
%%   Lily# 5.805000000 = 4.545000000 + 0.460000000 + 0.800000000  (flat box at the HOOK depth)
%%   residual +0.027480009 = TWO terms, not three:
%%       A  padding   0.46 (outside-staff pass) vs 0.5 (aligned_side)   −0.040000000
%%       B  own reach flat hook 0.8 vs the label's outline at that x    +0.067480009
%%
%% ⇒ THE PORT IS THREE PIECES, and piece 3 has no observer yet (point first):
%%   1. the anchor is aligned_side's: support distance + the grob's OWN padding 0.5,
%%      floored by staff-padding — the engraver's business, not the collision pass's
%%      (OutsideStaffStacker's own note beside DynamicLineSpanner states that split);
%%   2. the mover's skyline pair is the label's OUTLINE ∪ the dashed line ∪ the hook,
%%      the way PlaceCustomTexts already builds one (TextOutlineSkylines.Place) —
%%      the flat 0.8 box over the whole span over-reserves everywhere but the hook;
%%   3. ⚠️ DrawOttavaBrackets puts the label's BASELINE on the line where LilyPond
%%      centres its INK on it (ext ±0.792031364 is symmetric — that IS the centring).
%%      Porting 2 without 3 splits what is measured from what is drawn, which is the
%%      state HANDOFF 5.0 calls the worst one. No ledger point reads it yet.

%% One grob's vertical skyline on side DIR, in the SYSTEM frame: the stored profile is
%% about the grob's own origin, so it is shifted by rel X and raised by rel Y — exactly
%% what axis-group-interface.cc:794-795 and side-position-interface.cc:306-307 do before
%% taking a distance. A skyline pair is a Scheme pair, car = DOWN, cdr = UP (scm/c++.scm:242)
%% — spelled out here because `index-cell` is not bound in a .ly's module.
#(define (probe-dump-profile tag n i g sg dir)
   (let ((skyp (ly:grob-property g 'vertical-skylines)))
     (if (ly:skyline-pair? skyp)
         (let ((relx (ly:grob-relative-coordinate g sg X))
               (rely (ly:grob-relative-coordinate g sg Y)))
           (format #t "PROBEV ~a ~a ~a relx=~a rely=~a" tag n i relx rely)
           (for-each (lambda (p)
                       (format #t " (~a,~a)" (+ (car p) relx) (+ (cdr p) rely)))
                     (ly:skyline->points (if (> dir 0) (cdr skyp) (car skyp)) X))
           (format #t "\n")))))

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
                   ;; The OttavaBracket rides along: rel is its refpoint (= its dashed
                   ;; line) about the SYSTEM refpoint, ext its own ink about that line.
                   ;; ROUND 3: the two PROFILES the constraint is made of ride along too —
                   ;; the bracket's own DOWN skyline (the label's outline, the line, the
                   ;; hook) and each note column's UP skyline. Points are printed in the
                   ;; SYSTEM frame (the grob's own skyline shifted by its rel X and raised
                   ;; by its rel Y), so a reader can take the distance between them by hand
                   ;; and see which x binds and by how much.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (eq? nm 'OttavaBracket)
                                        (begin
                                          (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a)\n"
                                                  n i nm
                                                  (ly:grob-relative-coordinate g sg Y)
                                                  (car (ly:grob-extent g g Y))
                                                  (cdr (ly:grob-extent g g Y))
                                                  (+ (ly:grob-relative-coordinate g sg X)
                                                     (car (ly:grob-extent g g X)))
                                                  (+ (ly:grob-relative-coordinate g sg X)
                                                     (cdr (ly:grob-extent g g X))))
                                          (probe-dump-profile "DOWN" n i g sg DOWN)))
                                    (if (eq? nm 'NoteColumn)
                                        (probe-dump-profile "SUP" n i g sg UP))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% OTF — the FLOOR regime: drawn third-space c'' under the bracket, every support
%%     constraint far below staff ink + 2.0.
\book {
  \probeTag "OTF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \ottava 1 c'''4 c''' c''' c''' \ottava 0 | c''4 c'' c'' c'' \bar "|." }
  }
}

%% OTC — THE CONTROL, the SUPPORT regime: the same music two octaves up (drawn c''',
%%     two ledger lines), the column decides, the floor loses.
\book {
  \probeTag "OTC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \ottava 1 c''''4 c'''' c'''' c'''' \ottava 0 | c'''4 c''' c''' c''' \bar "|." }
  }
}

%% OTL — round 2: OTC on the LOWER staff of a two-staff system. Read about the LOWER
%%     staff's own refpoint, so the inter-staff distance cannot enter the reading. The
%%     upper staff is deliberately QUIET (middle-line quarters) — its ink is what the
%%     per-system tracker would wrongly let this bracket clear.
\book {
  \probeTag "OTL"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { b'4 b' b' b' | b' b' b' b' \bar "|." }
      \new Staff { \ottava 1 c''''4 c'''' c'''' c'''' \ottava 0 | c'''4 c''' c''' c''' \bar "|." }
    >>
  }
}

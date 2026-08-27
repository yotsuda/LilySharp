\version "2.26.0"
%% LP FIDELITY PROBE — THE GAP A ROWS-ONLY SYSTEM KEEPS TO THE NEXT SYSTEM, on a
%% MULTI-PAGE book (Lily#'s spring-chain path), with the next system ROW-LED.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe rows-only-page.ly -Prefix PROBERP
%% (two books, under a minute).
%%
%% WHY THIS BOOK EXISTS (session 266). LayoutUtilities.InterSystemPairMinimum names two
%% divergences its two callers kept when the pair minimum was folded into one home:
%% (1) the empty-silhouette fallback converts with ToFirst on the single-page path and
%% HalfFirst on the chain, and (2) the rows-only scalar floor (the session-240 repair)
%% exists only on the single-page path. Neither had an observer: the rerender corpus is
%% single-system (blind to pairs), and every existing page.* book keeps a staff on every
%% system. Session 266 measured the reachability of both before building this:
%%   * (1) NEVER FIRES — 612 books (the 82-book corpus + the user's lead sheets + the
%%     p257 variants) plus adversarial constructions (rows-only scores, hara-kiri'd
%%     rows-only systems, chain and single-page, titles) produced not one empty
%%     silhouette: a system with a staff seeds staff-symbol ink, and a rows-only
%%     system's silhouette is fed by the paging augment families.
%%   * (2)'s REGIME IS REACHABLE (this book's shape) but on today's code the chain's
%%     unfloored answer and the single-page floor AGREE NUMERICALLY on every
%%     constructible book (the rows-only silhouette degrades to exactly the scalar
%%     extents: dist == inkBelow + upExt + originToLast, measured on one- and two-row
%%     shapes). So there is no Lily#-vs-Lily# fork to observe — what was missing is the
%%     LP REFEREE for the quantity both arms compute, which is this probe.
%%
%% THE SHAPE. Six systems, explicit breaks, four to a page (so page 2 exists and Lily#
%% lays page 1 through PageLayouter's spring chain, not the single-page stack). Every
%% system opens with TWO chord rows over the staff (row-led: origin-to-first-refpoint
%% spans both bands, the geometry divergence (1) names). System 2's staff is all
%% multi-measure rests and \RemoveEmptyStaves takes it, leaving the two chord rows
%% alone — the rows-only system. The pair (system 2 -> system 3) is the reading.
%%
%% THE REGIME. ragged-right (line springs at force 0), ragged-bottom (page springs at
%% force 0), indent 0, DEFAULT vertical spacing — the product regime, deliberately.
%% ⚠️ A first draft lowered system-system basic/minimum to 1 to expose the skyline
%% floor, and the picture it produced was measured and REJECTED as a referee: with the
%% inter-system gap crushed, LilyPond's page assembly redistributes the LOOSE LINES
%% between spaceable anchors (page-layout-problem.cc:860-880) and the system after the
%% rows-only one had its chord rows laid ON TOP of its own staff — a regime dominated
%% by the loose-line redistribution Lily# does not have at all (HANDOFF 2D names that
%% absence), which would price a missing subsystem instead of the pair minimum. Under
%% the default spacing the redistribution stays quiet and the reading prices what the
%% pair's spring rests on.
%%
%% ⚠️ R1, NOT r1, and it is load bearing on the OTHER engine: LilyPond's hara-kiri
%% keeps a staff alive for ordinary rests and removes it for multi-measure rests only,
%% while Lily#'s removeEmpty was measured (session 266) to remove the staff for both
%% spellings. R1 is the spelling on which the two engines agree.
%%
%% ⚠️ THE CHORD LETTERS ARE C/D/F/G ON PURPOSE: no descenders anywhere in either row,
%% so the facing ink is baselines against cap-tops and the reading does not price the
%% two engines' chord faces (Nimbus Sans vs TeX Gyre Heros) beyond the round-capital
%% overshoot the ledger already carries at ~0.003 (page.chord-row.first-staff-refpoint).
%%
%% THE BOOKS:
%%   RPH — the rows-only book: system 2's staff line is R1 R1 and vanishes.
%%   RPC — THE CONTROL: the same book with system 2's staff carrying the same quarters
%%         as every other system, so nothing is removed. One variable between them.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0 step 2):
%%   (a) RPC page 1 holds 4 systems / 4 staves; RPH page 1 holds 4 systems / 3 staves
%%       (the hara-kiri asserted by a count, HANDOFF 5.0 trap 8).
%%   (b) Under the default spacing every system-system spring rests at
%%       max(basic-distance 12, skyline floor + padding); whether the RPH pair's floor
%%       beats 12 is exactly what the dump says, and BOTH outcomes are a reading: if
%%       the floor loses, the entry pins WHERE the 12 spans between (for a rows-only
%%       prev, which refpoint LilyPond hangs the spring from — the corner case
%%       page-layout-problem.cc:728-730 marks its first chord row spaceable, an anchor
%%       Lily#'s chain does not have: its no-spring node falls back to a nominal
%%       first-refpoint 2.0 below the system origin).
%%   (c) RPH's drawn reading (system 2's LOWER chord-row baseline down to system 3's
%%       first staff middle line) is not degenerate in either engine: LilyPond's
%%       system skyline sees the chord glyphs themselves, so the rows cannot be
%%       overprinted whatever wins.
%%   (d) THE FORK THIS PROBE DECIDES, for Lily#'s side (recorded in the ledger whys):
%%       Lily#'s chain prices the same pair from BAND arithmetic (two 3.1-ish bands
%%       spanning all X) where LilyPond prices per-X glyph ink plus its own anchor
%%       frame; a PLUS Lily# residual on the RPH reading means over-reservation, a
%%       MINUS one means the chain UNDER-answers — the session-240 shape alive on the
%%       multi-page path — and the repair direction is extending the single-page
%%       scalar floor to the chain, not trimming bands.

%% ============ MEASURED 2026-08-27 on LilyPond 2.26.0 (session 266) ============
%%
%% RPH page 1 holds 4 systems / 3 StaffSymbols, RPC 4 / 4 — prediction (a) HELD.
%%
%% RPH Y-offsets (page 1): 6.559068150221457 / 18.559068150221457 /
%% 30.559068150221457 / 42.55906815022146 — every pair EXACTLY 12.000000 apart:
%% the skyline floor loses to basic-distance 12 on the rows-only pair too, and the
%% corner-case anchor (:728-730) makes the rows-only line's spring span the same
%% 12 a staff's would (its sre reads (-1.938700 . -1.938700), the upper chord row
%% standing where a staff refpoint stands). RPC Y-offsets: 6.559068150221457 /
%% 17.075426780880907 / 27.623194586614176 / 38.13955321727362 — deltas
%% 10.516359 / 10.547768 / 10.516359 — LESS than the 12 the rows-only pairs rest
%% at, so a staffed pair's spring is NOT origin-to-origin at basic-distance (the
%% frame build_system_skyline's dy conversions put it in; not decomposed here —
%% this probe's question was the rows-only pair, and its answer is above).
%%
%% ★ THE FINDING THAT DECIDED AGAINST LEDGERING THIS PAIR. On the system AFTER the
%% rows-only one, LilyPond's page assembly redistributes the loose lines between
%% the bracketing spaceable anchors (page-layout-problem.cc:860-880) and its chord
%% rows land ON the staff: rel +2.127467 / -0.371417 against the normal
%% +3.588959 / +1.121485 (line 3 of the same page) — the lower row's baseline sits
%% 0.43 BELOW its own staff's top line, glyph ink overlapping the staff (verified
%% in the rendered page, both under the rejected tight regime and under the
%% DEFAULTS above). That redistribution is the subsystem Lily# does not have at
%% all (HANDOFF 2D: loose-line redistribution, unported items (2)(3)), so a
%% residual recorded here would price that absence, not the pair minimum the
%% divergence inventory names. When the redistribution is ported, this probe is
%% the referee waiting for it — committed and re-runnable, ledgered then.
%%
%% Lily#'s side of the same shape, measured the same day with a temporary
%% instrumentation of InterSystemPairMinimum (session 266, HANDOFF §1): the chain
%% and single-page arms answer the SAME number on every constructible rows-only
%% book (the rows-only silhouette degrades to exactly the scalar extents —
%% dist == inkBelow + upExt + originToLast on one- and two-row shapes), and the
%% empty-silhouette fallback (divergence (1)) fired on none of 612 books nor on
%% any adversarial construction. Both divergences are inert today; the remarks on
%% InterSystemPairMinimum carry the audit.

#(define (dump tag layout pages)
   (let ((top (ly:output-def-lookup layout 'top-margin)))
     (format #t "\nPROBERP BOOK ~a top-margin=~a\n" tag top)
     (let ploop ((ps pages) (p 1))
       (if (pair? ps)
           (begin
             (let loop ((ls (ly:prob-property (car ps) 'lines)) (i 0))
               (if (pair? ls)
                   (let* ((sys (car ls))
                          (yoff (ly:prob-property sys 'Y-offset 0.0))
                          (sre (ly:prob-property sys 'staff-refpoint-extent '(0 . 0)))
                          (sg (ly:prob-property sys 'system-grob)))
                     (format #t "PROBERP ~a PAGE ~a LINE ~a yoff=~a sre=(~a . ~a)\n"
                             tag p i yoff (car sre) (cdr sre))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(ChordName StaffSymbol))
                                        (format #t "PROBERP ~a GROB ~a ~a ~a rel=~a ext=(~a . ~a)\n"
                                                tag p i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))))))
                                (ly:grob-array->list all)))))
                     (loop (cdr ls) (1+ i)))))
             (ploop (cdr ps) (1+ p)))))))

rpPaper =
#(define-scheme-function (tag) (string?)
   #{ \paper {
        indent = 0
        ragged-right = ##t
        ragged-bottom = ##t
        max-systems-per-page = #4
        property-defaults.fonts.serif = "LilyPond Serif"
        property-defaults.fonts.sans = "LilyPond Sans Serif"
        page-post-process = #(lambda (layout pages) (dump tag layout pages)) } #})

staffCommon = { \clef treble \repeat unfold 2 { g'4 a' g' a' } \break }
chordsRowOne = \chordmode { c1 g f c g c g d c g g c }
chordsRowTwo = \chordmode { g1 c c f c g d g g c c g }

rpLayout =
\layout { \context { \Staff \RemoveEmptyStaves } }

%% RPH — system 2's staff is two multi-measure rests and hara-kiri takes it.
\book {
  \rpPaper "RPH"
  \score { <<
    \new ChordNames \chordsRowOne
    \new ChordNames \chordsRowTwo
    \new Staff {
      \staffCommon
      R1 R1 \break
      \repeat unfold 2 { g'4 a' g' a' } \break
      \repeat unfold 2 { g'4 a' g' a' } \break
      \repeat unfold 2 { g'4 a' g' a' } \break
      \repeat unfold 2 { g'4 a' g' a' }
    }
  >> \rpLayout }
}

%% RPC — THE CONTROL: the same book, system 2's staff plays like every other.
\book {
  \rpPaper "RPC"
  \score { <<
    \new ChordNames \chordsRowOne
    \new ChordNames \chordsRowTwo
    \new Staff {
      \staffCommon
      \repeat unfold 2 { g'4 a' g' a' } \break
      \repeat unfold 2 { g'4 a' g' a' } \break
      \repeat unfold 2 { g'4 a' g' a' } \break
      \repeat unfold 2 { g'4 a' g' a' } \break
      \repeat unfold 2 { g'4 a' g' a' }
    }
  >> \rpLayout }
}

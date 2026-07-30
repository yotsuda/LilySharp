\version "2.26.0"
%% LP FIDELITY PROBE — HOW MUCH ROOM A FIGURE ROW TAKES AT THE FOOT OF A PAGE.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe figured-bass-page.ly -Prefix PROBEP
%% (three books, about forty-five seconds).
%%
%% WHY THIS BOOK EXISTS, and why it is a page book rather than a fourth placement book.
%%
%% figured-bass-placement.ly measures WHERE the figures sit and HOW MUCH ROOM they get
%% BETWEEN two staves; the corpus has no reading of what a figure row does to the PAGE.
%% Lily# has a third spelling of the row's depth that only that missing reading can see:
%% LayoutEngine.EstimateLooseLineExtents adds `2.0 + n * 1.5` to the system's down extent
%% for a system carrying n figures per column — a LILYSHARP-OWN formula with no LilyPond
%% counterpart at all (LilyPond's pure height comes from the same grobs' pure extents,
%% which is exactly why the LYRIC branch of that same function was deleted).
%%
%% ⚠️ IT IS LOAD-BEARING, AND MEASURED RATHER THAN GUESSED (session 45): zeroing that branch
%% moves test/figbass-below-script's page height by -0.59 and
%% test/figbass-chordname-lower-staff's by -0.55. It is what floored those pages, and it
%% MASKED the BassFigureAlignment port, which moved them by only -0.01 and -0.05. The port
%% could not delete it, because deleting output-moving code with no observer is how a defect
%% gets shipped as a rebase (HANDOFF §5.0). THESE ARE THAT OBSERVER.
%%
%% THE QUANTITY: the last staff refpoint of page 1 down to the bottom paper edge — the span
%% of the spring Page_layout_problem appends after the last system (page-layout-problem.cc:
%% 538-545 last-bottom-spacing), the same term page.{stretched,compressed}.last-staff-to-foot
%% read on books JSS/JSK. It is the ONE page reading in which the ink hanging BELOW the last
%% staff appears by itself: `ensure_min_distance` raises the spring's floor to
%% `last_padding - bottom_skyline_.max_height ()`, i.e. padding 1 + whatever hangs under that
%% refpoint, and leaves its strength alone (spring.cc:156-159). A figure row is that ink.
%%
%% WHY THE FLOOR IS WHAT READS HERE, stated so the regime can be falsified from the dump
%% itself rather than believed: the three books differ ONLY in what hangs below the staff,
%% and their readings differ by EXACTLY those inks (2.050000 / 5.174795 / 9.624795, each
%% + bottom-margin 5.690551 + padding 1). A spring taking the page's force instead would
%% answer the same in all three, since all three pages hold twelve systems of the same music.
%% ⚠️ A page that stretched past f = (1 + ink - 1) / 30 would open this spring and measure
%% something else; the quiet book has the smallest ink and therefore the earliest block, at
%% f = 0.172493, and page 1 solves for f = 0.020 (the printed system gaps against ideal 12).
%%
%% THE PAPER: twelve systems to a page, justified (the default). Two reasons, both regime:
%%   * FULL, so the chain reaches the foot at a small force. Six systems of this one-staff
%%     music leave enough slack to stretch the foot spring past its block; twelve do not.
%%   * NOT THE LAST PAGE — 100 bars is fourteen systems, so page 1 is followed by page 2 and
%%     ragged-last-bottom (##t by default) cannot suppress the justification page 1 needs.
%%     ⚠️ Both are asserted by the ledger's three systems-on-first-page entries. A reading
%%     taken from "the last staff of page 1" means that staff only while the page holds the
%%     systems the probe assumes (HANDOFF §5.0 trap 8).
%%
%% THE THREE TEXTURES, which are the point:
%%   FBPQ — the QUIET one (middle-line d, stems forced UP, so the staff's own ink is the
%%          deepest thing the music has) with two figure rows. Here the row IS the ink below
%%          the refpoint, and it is the smallest such row there can be.
%%   FBPD — the DEEP one (two ledger lines below the bass staff, stems forced DOWN) with the
%%          same two rows, so the row is pushed far under the staff.
%%   FBPN — THE CONTROL: FBPQ's music with the figures removed and nothing else changed, so
%%          LilyPond's difference between it and FBPQ IS the row's contribution, with no
%%          appeal to any other reading. (The same music built twice, deliberately: a pair
%%          whose halves are hand-copied drifts apart, and has, twice — HANDOFF §5.0.)
%%
%% ⚠️ The figures are on EVERY bar, not just the last system's. The two engines need not
%% break lines identically, and a figure row parked only at the end would then land on
%% different systems on the two sides; uniform music makes the last system of page 1 carry
%% the row whatever the breaking is.
%%
%% ⚠️ REGIME S (figures in the Staff context, direction DOWN), because that is what `@fig()`
%% is and what the port mimics — see figured-bass-placement.ly's header, and the ledger's
%% figbass.upper-staff.staff-gap for the one quantity on which LilyPond's two devices differ.
%%
%% PREDICTIONS, written before the Lily# side was run (HANDOFF §5.0 step 2), with signs and
%% a fork. Lily#'s down extent is `max(its real skyline extent, 2.0 + n * 1.5)`, so with
%% n = 2 the invention offers 5.000000 below the bottom staff line, and the reading adds the
%% half staff (2.0) and the foot spring's padding 1 and bottom-margin 5.690551.
%%   * FBPQ: the invention BINDS — Lily#'s own row reaches only 3.172462 below the bottom
%%     line (the ledger's figbass.quiet placement 3.672462 below the centre, plus the ported
%%     1.5 step, less the 2.0). PREDICTED Lily# 13.690551, residual +1.825204, which is
%%     5.000000 - 3.174795 - 0.002333187 to the digit: over-reservation, PLUS sign.
%%   * FBPD: the invention is INACTIVE — the real row reaches 7.622462 below the bottom line,
%%     which beats 5.000000. PREDICTED residual -0.002333187, THE ISLAND'S ONE NUMBER
%%     (emmentaler-11 against emmentaler-20; see figbass.alone). ⚠️ MINUS sign: Lily#'s digit
%%     is the smaller one.
%%   * FBPN: 0 exact. No figures at all, so none of the five guards in the figured-bass code
%%     is even entered; if this moves, something with no figures on it changed.
%%   * ★ THE FORK, and it decides what the next commit IS rather than what it prints:
%%       - FBPD at -0.002333187 ⇒ the figure row IS in Lily#'s system silhouette already, the
%%         estimate is a pure over-reservation on top of it, and the port is a DELETION.
%%       - FBPD near -2.624795 (i.e. Lily# reading 13.690551 in BOTH books) ⇒ the row is NOT
%%         in that silhouette and the estimate is the only reservation there is. Then deleting
%%         it is a regression, and the port is instead to merge the row's ink into the down
%%         skyline — the same claim the between-staves half needed in session 43 (placement
%%         and reservation are one claim).
%%   * FALSIFIER for the port, whichever branch runs: afterwards ALL THREE read the island's
%%     one number and nothing else — FBPQ and FBPD at -0.002333187, FBPN unmoved at 0.
%%     Anywhere else and the depth below the staff was not the only term left in this reading.
%%
%% MEASURED (LilyPond 2.26.0, 2026-07-30). Page 1 holds twelve systems in all three books and
%% page 2 holds two, so the regime the predictions assume is the one that ran:
%%   FBPQ  last-staff-to-foot = 11.865346416707723   ink below the refpoint = 5.174795235605362
%%   FBPD                     = 16.315346416707683                          = 9.624795235605323
%%   FBPN                     =  8.740551181102347                          = 2.049999999999986
%% Each is bottom-margin 5.690551181102362 + last-bottom-spacing's padding 1 + that ink, and
%% the three inks are the three textures' own: the staff's 2.05, the quiet row's
%% 3.674795235605315 + one 1.5 step, and the deep row's 9.624795235605315 — the last of which
%% figured-bass-placement.ly's FBLB already decomposed for the between-staves reading. ⇒ THE
%% SPRING IS ON ITS FLOOR IN ALL THREE, which is the regime claim, and it is falsifiable right
%% here: the three books' pages are otherwise identical, so a spring taking the page's force
%% would answer alike. The forces themselves confirm it from the other side — the uniform
%% system gaps read 13.212025 / 12.811124 / 13.493538 against ideal 12 with stretchability 60,
%% i.e. f = 0.020200 / 0.013519 / 0.024892, each well under its own block (the quiet book's is
%% the earliest at f = 0.172493, the control's at 0.068333).

%% WHAT THE LILY# SIDE ANSWERED (2026-07-30, the same session; the full record is in the six
%% ledger `why`s). The distance predictions held to six digits — FBPQ +1.825204583 and FBPD
%% -0.002333368, where the last three digits of each are the harness's own bottom margin (the
%% F6 5.690551 against 5.690551181102362), which FBPN reads ALONE at -0.000000181 and is
%% therefore the trio's calibration rather than a geometry difference.
%%   ⇒ THE FORK RESOLVED TO ITS FIRST BRANCH: the row is already in Lily#'s system silhouette,
%%     so `2.0 + n * 1.5` is a pure over-reservation on top of it and the port is a DELETION.
%%   ★ AND THE PAIR FOUND A SECOND THING, which is what a pair is for: FBPD's COUNT came out
%%     10 against LilyPond's 12. It is the one book of the three whose count is not held at the
%%     cap, so it is the only one that reads the page BREAKER, and the two engines disagree
%%     about the room a deep figure row needs BETWEEN systems (about 2.85 per pair) — a
%%     different quantity from the room below the last one, and OPEN. ⚠️ Not this trio's
%%     branch: 5.000000 loses to that texture's real 7.622462048 and is inactive there, which
%%     FBPD's distance says independently by landing on the island's own number.

#(define (probe-dump-pages layout pages)
   (let ((top (ly:output-def-lookup layout 'top-margin))
         (bottom (ly:output-def-lookup layout 'bottom-margin))
         (height (ly:output-def-lookup layout 'paper-height)))
     (format #t "\nPROBEP PAPER top-margin=~a bottom-margin=~a paper-height=~a\n"
             top bottom height)
     (let loop ((ps pages) (n 1))
       (if (pair? ps)
           (let* ((page (car ps))
                  (lines (ly:prob-property page 'lines))
                  (last-sys (if (pair? lines) (car (last-pair lines)) #f)))
             ;; The reading, computed here rather than left to a reader with a rounded dump:
             ;; scm/page.scm:190 translates a system's stencil by -(Y-offset + top-margin), so
             ;; the system origin sits that far below the paper edge, and
             ;; staff-refpoint-extent holds its staves' refpoints ABOUT that origin (negative,
             ;; the staves being below it). The LAST spaceable staff is the interval's cdr,
             ;; and last-bottom-spacing attaches there.
             (if last-sys
                 (let* ((staff (ly:prob-property last-sys 'staff-refpoint-extent '(0 . 0)))
                        (refpoint (- (+ (ly:prob-property last-sys 'Y-offset 0.0) top)
                                     (cdr staff))))
                   (format #t "PROBEP PAGE ~a systems=~a last-staff-to-foot=~a ink-below-refpoint=~a\n"
                           n (length lines) (- height refpoint)
                           ;; What the floor is made of, so the entry's decomposition is in the
                           ;; dump: to-foot less bottom-margin less last-bottom-spacing's
                           ;; padding 1 (ly/paper-defaults-init.ly:84-87).
                           (- height refpoint bottom 1))))
             ;; The raw ingredients too — a computed line that goes wrong silently is worse
             ;; than an arithmetic step someone has to take.
             (let inner ((ls lines) (i 0))
               (if (pair? ls)
                   (let* ((sys (car ls))
                          (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                     (format #t "PROBEP SYS ~a ~a y=~a staff=(~a . ~a)\n"
                             n i (ly:prob-property sys 'Y-offset 0.0)
                             (car staff) (cdr staff))
                     (inner (cdr ls) (1+ i)))))
             (loop (cdr ps) (1+ n)))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEP BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% THE MUSIC. 100 bars = fourteen systems at this line width, twelve of them on page 1.
%% The two textures are figured-bass-placement.ly's own quietMusic and figuredMusic,
%% repeated: the same regimes those books measure the PLACEMENT of, so a difference here
%% cannot be a difference of texture.
quietMusic = { \clef bass \stemUp \repeat unfold 100 { d4 d d d } }
deepMusic  = { \clef bass \stemDown \repeat unfold 100 { c,4 c, c, c, } }
theFigures = \figuremode { \repeat unfold 100 { <5 3>1 } }

figuredLayout =
\layout {
  \context {
    \Staff
    \consists "Figured_bass_engraver"
    \override BassFigureAlignmentPositioning.direction = #DOWN
  }
}

%% FBPQ — the quiet texture: the row is the deepest ink there is, and the smallest row there
%%     can be. This is the book in which Lily#'s `2.0 + n * 1.5` is expected to bind.
\book {
  \probeTag "FBPQ"
  \paper { max-systems-per-page = #12 indent = 0 }
  \score { \new Staff << \quietMusic \theFigures >> \figuredLayout }
}

%% FBPD — the deep texture: the column pushes the row far below the staff, so the real ink
%%     is expected to beat the invention and this book says whether the row is in the
%%     silhouette at all (the fork above).
\book {
  \probeTag "FBPD"
  \paper { max-systems-per-page = #12 indent = 0 }
  \score { \new Staff << \deepMusic \theFigures >> \figuredLayout }
}

%% FBPN — the control: FBPQ without the figures. LilyPond's difference between the two IS
%%     the row's contribution to the foot, with nothing else changed.
\book {
  \probeTag "FBPN"
  \paper { max-systems-per-page = #12 indent = 0 }
  \score { \new Staff \quietMusic }
}

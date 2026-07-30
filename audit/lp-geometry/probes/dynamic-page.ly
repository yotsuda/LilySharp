\version "2.26.0"
%% LP FIDELITY PROBE — HOW MUCH ROOM A DYNAMIC (AND A HAIRPIN) TAKES AT THE FOOT OF A PAGE.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dynamic-page.ly -Prefix PROBEP
%% (three books, about a minute).
%%
%% WHY THIS BOOK EXISTS — it is figured-bass-page.ly's recipe pointed at the NEXT branch of
%% the same function. LayoutEngine.EstimateLooseLineExtents estimates a system's down extent
%% from the ITEMS, with a hand-picked constant per annotation class:
%%
%%     dynamics   2.0   ("staffPadding(0.2) + padding(0.6) + textAscent(1.2)")
%%     hairpins   1.5   ("Hairpins share Y level with dynamics; estimate ~1.5 ss")
%%
%% Both carry a LILYPOND-REF naming DynamicLineSpanner's outside-staff-priority 250 — which is
%% a PRIORITY, not either number. LilyPond has no such estimate at all: a system's pure height
%% comes from the same grobs' pure extents. The figured-bass branch of this function was
%% deleted on 2026-07-30 once these three readings existed for it (session 46); these are the
%% same three readings for the dynamic family, which is one family in LilyPond's model — a
%% hairpin and a dynamic text hang off the SAME DynamicLineSpanner (scm/define-grobs.scm,
%% direction DOWN, padding 0.6, staff-padding 0.1, minimum-space 1.2).
%%
%% THE QUANTITY (identical to figured-bass-page.ly, see that header for the full argument):
%% page 1's last staff refpoint down to the bottom paper edge — last-bottom-spacing's span
%% (page-layout-problem.cc:538-545), whose floor ensure_min_distance raises to padding 1 plus
%% whatever ink hangs below that refpoint. Twelve systems to a justified page, 100 bars so
%% page 1 is neither the last page nor short.
%%
%% THE CONTROL IS NOT REPEATED HERE: figbass.page.control.last-staff-to-foot is this exact
%% paper with this exact quiet music and NOTHING hanging below it (8.740551181102347 =
%% bottom-margin + padding 1 + the staff's own ink 2.05). Measuring it a second time would
%% add a duplicate entry rather than a fact.
%%
%% THE THREE BOOKS:
%%   DYPQ — the QUIET texture (middle-line d, stems forced UP) with \f on every bar. The
%%          dynamic is the only thing below the staff, so the reading IS its ink.
%%   DYPD — the DEEP texture (two ledger lines below, stems forced DOWN) with the same \f, so
%%          the column pushes the dynamic down and the estimate should be dominated.
%%   DYPH — the QUIET texture with a HAIRPIN and no dynamic text at all, which is the only
%%          state in which Lily#'s 1.5 branch runs (it is gated on there being no dynamics).
%%
%% ⚠️ ONE SPELLING DIFFERENCE, STATED RATHER THAN HIDDEN (DYPH only). LilyPond ends a hairpin
%% with \!, and Lily# has no terminator sigil: its grammar ends a hairpin at the next dynamic
%% (docs/GRAMMAR.md:544), so with no dynamic anywhere a hairpin runs to the next hairpin.
%% MEASURED on the Lily# side before writing this book: three per-bar @cresc marks draw TWO
%% hairpins, each spanning its bar into the next one's start, and the last is dropped. So the
%% two sides put a hairpin under every bar but END them one note apart. The reading is the
%% ink's DEPTH under the staff and this texture is uniform — every bar of every system is the
%% same quiet d — so the depth cannot depend on where a hairpin stops. ⚠️ It would matter for
%% an X reading or for a broken-spanner reading, and this book must not be reused for either.
%%
%% PREDICTIONS, written before running (HANDOFF §5.0 step 2), with a fork that decides the
%% next commit rather than a number that decorates it:
%%   * THE LILY# SIDE IS EXACTLY COMPUTABLE, so it is the fork. Its down extent is
%%     max(real placed ink, the estimate), and the estimate is 2.0 below the BOTTOM LINE =
%%     4.000000 below the refpoint (1.5 = 3.500000 for DYPH). Adding padding 1 and
%%     bottom-margin 5.690551:
%%       - DYPQ reading 10.690551 exactly ⇒ THE ESTIMATE BINDS, and its residual against
%%         LilyPond is what deleting it will move.
%%       - DYPQ reading anything else ⇒ the real placed ink already beats it, the estimate is
%%         INERT, and the deletion is free (an entry that opens exact, §5.2.1④ — the net
%%         under a deletion, not a hunt).
%%       - DYPH likewise against 10.190551.
%%   * DYPD: the estimate must be dominated — the deep column carries the dynamic far below
%%     4.000000 — so this book should read the dynamics island's own debt and nothing new.
%%     If it reads 10.690551 too, then the dynamic is NOT in the down silhouette at all and
%%     the port is a merge rather than a deletion (the figured-bass fork's second branch).
%%   * LILYPOND, by mechanism rather than by arithmetic (the glyph's own ink is the term this
%%     probe exists to print): aligned_side puts the spanner's ink top at the staff's 2.05 +
%%     padding 0.6 in the quiet books, so the reading is bottom-margin + 1 + 2.65 + the ink
%%     the \f (or the hairpin's half height) adds below that. ⇒ Somewhere near 10.3 for DYPQ
%%     and near 9.8 for DYPH, i.e. THE SAME ORDER as the estimate — unlike the figured-bass
%%     case, where the invention was 1.8 out. Whatever comes back, the fork above is what
%%     decides the work.

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
             ;; The same reduction figured-bass-page.ly prints, and for the same reason:
             ;; scm/page.scm:190 translates a system by -(Y-offset + top-margin), and
             ;; staff-refpoint-extent holds its staves' refpoints about that origin.
             (if last-sys
                 (let* ((staff (ly:prob-property last-sys 'staff-refpoint-extent '(0 . 0)))
                        (refpoint (- (+ (ly:prob-property last-sys 'Y-offset 0.0) top)
                                     (cdr staff))))
                   (format #t "PROBEP PAGE ~a systems=~a last-staff-to-foot=~a ink-below-refpoint=~a\n"
                           n (length lines) (- height refpoint)
                           (- height refpoint bottom 1))))
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

%% The two textures are figured-bass-page.ly's own, unchanged, so a difference between the
%% two probes is the ANNOTATION and not the music.
quietDyn = { \clef bass \stemUp \repeat unfold 100 { d4\f d d d } }
deepDyn  = { \clef bass \stemDown \repeat unfold 100 { c,4\f c, c, c, } }
quietHair = { \clef bass \stemUp \repeat unfold 100 { d4\< d d d\! } }

\book {
  \probeTag "DYPQ"
  \paper { max-systems-per-page = #12 indent = 0 }
  \score { \new Staff \quietDyn }
}

\book {
  \probeTag "DYPD"
  \paper { max-systems-per-page = #12 indent = 0 }
  \score { \new Staff \deepDyn }
}

\book {
  \probeTag "DYPH"
  \paper { max-systems-per-page = #12 indent = 0 }
  \score { \new Staff \quietHair }
}

%% ---------------------------------------------------------------------------------------
%% THE ARRANGEMENT BOOKS, added after the first three ran — and added because the DELETION
%% the first three licensed turned out to move five committed fixtures, every one of them
%% MULTI-STAFF (test/dynamics-lower-staff, test/multi-staff-hairpins,
%% test/voice-dynamics-multistaff, showcase/03-piano, test/ossia-beams; page height alone,
%% by -0.33 to -0.67, no drawn coordinate moved).
%%
%% ⚠️ THE THREE BOOKS ABOVE CANNOT SEE THAT, and the reason is exactly the defect: Lily#'s
%% estimate is taken PER SYSTEM from the ITEMS, with no staff in the sentence, so a dynamic
%% belonging to the UPPER staff of two adds its 2.0 below the WHOLE system — where its real
%% ink lives BETWEEN the staves and contributes nothing to a system's down extent. On a lone
%% staff the two spellings agree about which staff is meant, which is why a single-staff pair
%% measures the constant and not the frame. Same shape as the figured-bass drop that had no
%% staff in it either (figured-bass-placement.ly, session 43).
%%
%% ⇒ DYPU / DYPHU are that arrangement: the annotation belongs to the UPPER staff of two, and
%% the LOWER staff is quiet and carries nothing at all. So NOTHING hangs below the last staff
%% and LilyPond's foot reading must be the plain staff one — the same 8.740551181102347 the
%% control reads. The whole of Lily#'s number above that is the estimate charging a
%% between-staves annotation to the foot of the page.
%%
%% PREDICTIONS, written before running:
%%   * LilyPond reads 8.740551181102347 in BOTH books, identical to
%%     figbass.page.control.last-staff-to-foot: a two-staff system whose lower staff is quiet
%%     hangs the same 2.05 below its last refpoint as a one-staff system does. ⚠️ FALSIFIER
%%     for the arrangement itself: if it reads MORE, something in these books does hang below
%%     the lower staff and the pair measures a different thing.
%%   * Lily# reads 10.690551181 (DYPU) and 10.190551181 (DYPHU) — bottom-margin + padding 1 +
%%     the half staff 2.0 + the estimate's 2.0 / 1.5 — i.e. residuals of +1.950000 and
%%     +1.450000, the ENTIRE constant, because the real ink it should be reading is between
%%     the staves where the foot cannot see it.
%%   * After the deletion both must read 8.740551 exactly, i.e. 0.
%%
%% ★★★ MEASURED — AND THE FALSIFIER FIRED, SO NO LEDGER ENTRY WAS OPENED ON THESE TWO.
%% DYPU reads 18.025409944386325 and DYPHU 17.563843269591302, both far above the predicted
%% 8.740551181102347, and the reason is the REGIME rather than the geometry: a two-staff
%% system is tall, so LilyPond puts only SEVEN (eight) of them on the page and the page is
%% left with slack. The foot spring is then STRETCHED rather than sitting on its floor —
%% f ≈ 0.378 against a block of (1 + 2.05 − 1) / 30 = 0.068333 — and a stretched spring's
%% length is the page's force, not the ink under the last staff. That is precisely the caveat
%% the three books above are written under ("a page that stretches past its block is measuring
%% a different quantity"), and it is why these two are kept here MEASURED but unentered: a
%% number that does not mean what the entry would claim is worse than no entry.
%%
%% ⇒ WHAT WOULD BE NEEDED, for whoever wants this arrangement in the ledger: a two-staff page
%% that COMPRESSES (JSK's regime — more systems than fit, so every floor binds), which needs
%% either the exact `systems-per-page` form (whose Lily#-side hazard is documented on
%% LpGeometryProbes.SixSystemsPerPage: a count the breaker cannot satisfy makes it fall back
%% to one content-sized page and the probe measures its own fallback) or a shrunk paper. Both
%% are a book's worth of work, not a line's.
%%
%% ⚠️ THE ARRANGEMENT IS NOT LEFT UNGUARDED, though. What the estimate did to it is a claim
%% about Lily#'s OWN model — "a dynamic belonging to the upper staff of two must not extend
%% the SYSTEM's down extent, because its ink lives between the staves" — and that is pinned
%% by a machine instead: LilySharp.Tests' LooseLineExtentScopeTests. A claim with no ledger
%% reading gets a test in the same commit (HANDOFF §5.0, the session-42 lesson that a
%% re-based snapshot is not an observer).
upperDyn = { \clef bass \stemUp \repeat unfold 100 { d4\f d d d } }
upperHair = { \clef bass \stemUp \repeat unfold 100 { d4\< d d d\! } }
lowerQuiet = { \clef bass \stemUp \repeat unfold 100 { d4 d d d } }

\book {
  \probeTag "DYPU"
  \paper { max-systems-per-page = #12 indent = 0 }
  \score { << \new Staff \upperDyn \new Staff \lowerQuiet >> }
}

\book {
  \probeTag "DYPHU"
  \paper { max-systems-per-page = #12 indent = 0 }
  \score { << \new Staff \upperHair \new Staff \lowerQuiet >> }
}

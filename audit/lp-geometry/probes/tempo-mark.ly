\version "2.26.0"
%% LP FIDELITY PROBE — where the METRONOME MARK sits: its baseline over the staff, its
%% LEFT edge against the time signature, and its height over a trill it must clear.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tempo-mark.ly (two tiny books).
%%
%% WHY THIS ISLAND IS OPEN (USER DIRECTIVE 2026-07-29): Lily#'s tempo mark does not
%% mimic LilyPond — it draws "= N" BOLD serif at 1.8, prices its extent as a CENTERED
%% estimate, and places it by its own arithmetic; the trill port exposed the mismatch
%% when the mark's "120" crossed the lowered tr glyph ink (test/trill-spanner, named in
%% HANDOFF session 32 note 6). Fix tempo first — points first.
%%
%% WHAT LILYPOND DECLARES (verified in source, 2026-07-29):
%%   * MetronomeMark (define-grobs.scm:2335-2365): direction UP, side-axis Y,
%%     Y-offset side-position-interface::y-aligned-side, padding 0.8, NO staff-padding,
%%     outside-staff-priority 1300, outside-staff-horizontal-padding 0.2,
%%     vertical-skylines from the STENCIL, break-align-symbols (time-signature),
%%     self-alignment-X LEFT, X-offset self-alignment-interface::self-aligned-on-breakable.
%%   * The SUPPORTS are the STAVES: metronome-engraver.cc:136-139 sets
%%     side-support-elements = stavesFound, so aligned_side pays padding 0.8 against the
%%     staff's own extent (no include_staff needed — the staff IS the support).
%%   * The markup (translation-functions.scm:100-150 metronome-markup): the note is
%%     \smaller \note-by-number, general-aligned Y DOWN (its BOTTOM on the baseline),
%%     then literal " = " and the count, all in the mark's plain upright font (only an
%%     \markup TEXT like "Allegro" is \bold); MetronomeMark declares no font-size, so
%%     the text em is the text-font-size family (2.2, like TextScript).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0, with signs):
%%   * TMQ Y: baseline = staff ink 2.05 + padding 0.8 + (baseline − stencil bottom).
%%     The digits and the DOWN-aligned note both sit ON the baseline, so the facing
%%     term is only the digits' overshoot (~0.01 at em 2.2): ≈ 2.86, NOT round.
%%     If it reads ≈ 2.51 + something, the 0.46 pass won instead and the support
%%     claim is wrong.
%%   * TMQ X: mark ink-left − time-signature ink-left ≈ 0 (self-aligned LEFT on the
%%     break-aligned time signature). If it is the mark's PEN that aligns, the two
%%     books' readings differ by the first glyph's side bearing — both books carry the
%%     same first glyph (the note), so this probe cannot split pen-vs-ink; it pins the
%%     zero and the next probe splits it if Lily# needs the distinction.
%%   * TMT Y: strictly HIGHER than TMQ (sign certain): the trill (priority 50) is
%%     placed first — quiet trill line at 3.550000 (ledger trill.quiet), glyph ink to
%%     line + 1.1 — and the mark (priority 1300) clears it by outside-staff 0.46 with
%%     horizontal padding 0.2. Candidate: 3.55 + 1.1 + 0.46 + (facing ≈ 0) ≈ 5.11,
%%     pointwise details left to the measurement. TMT's trill itself must read
%%     3.550000 (the ledger value) — if the trill moved, the mark changed the trill
%%     and the books are not measuring what they claim.
%%   * FALSIFIER: TMQ == TMT means the trill did not push the mark and the pair
%%     measured nothing about the stacking — treat as unmeasured, do not record.
%%
%% ⚠️ The mark's "= 120" is serif TEXT; the serif pin is load-bearing (svg backend
%% resolves fonts.serif via this machine's fontconfig otherwise; page-vertical.ly's
%% header has the history).
%%
%% MEASURED (2026-07-29, first run; ledger entries are NOT opened yet — the Lily#
%% mirrors and accessors are the next session's first move):
%%   * TMQ Y: baseline − staffRefpoint = 2.883010 = staff ink 2.05 + padding 0.8
%%     + 0.033010, and 0.033010 IS the mark's own ext bottom (dump (-0.03300964 …)) —
%%     aligned_side lands the stencil's bottom at 2.85 and the baseline rides its own
%%     overshoot above that. The support-is-the-staff claim held to the digit.
%%   * TMQ X: mark ink-left = TimeSignature ink-left = 4.885000 exactly — LEFT on the
%%     break-aligned time signature, difference 0.
%%   * TMT Y: baseline − staffRefpoint = 5.110000 SIX-DIGIT ROUND = quiet trill line
%%     3.55 + tr glyph top 1.1 + outside-staff 0.46 — the digits under the trill's
%%     x-range sit flat ON the baseline, so the cleared edge is the baseline itself
%%     (the -0.033 overshoot lives under the note glyph, left of the trill).
%%   * TMT trill: 3.550000, UNMOVED (the ledger trill.quiet value) — the mark cleared
%%     the trill, not the other way round; both falsifiers held (TMQ != TMT).
%%   * ext rode along: the mark's ink is (-0.033010 . 3.161441) about its baseline —
%%     the 3.161441 is the \smaller up-stem quarter (3.53 x ~0.891), the "= 120" text
%%     is em 2.2 upright serif, NOT bold and NOT 1.8.

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
                   ;; MetronomeMark / TrillSpanner / TimeSignature ride along:
                   ;; rel = Y refpoint about the SYSTEM refpoint, ext = own Y ink,
                   ;; x = own X ink about the system (for the break-align reading).
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(MetronomeMark TrillSpanner TimeSignature))
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

%% TMQ — the QUIET regime: \tempo over drawn third-space c'' heads, nothing else
%%     above the staff; aligned_side against the staff support decides.
\book {
  \probeTag "TMQ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \tempo 4 = 120 c''4 c'' c'' c'' | c'4 c' c' c' \bar "|." }
  }
}

%% TMT — THE CONTROL, the STACKING regime: the same music with a trill under the
%%     mark; the mark (priority 1300) must clear the trill (priority 50).
\book {
  \probeTag "TMT"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \tempo 4 = 120 c''4\startTrillSpan c'' c'' c''\stopTrillSpan | c'4 c' c' c' \bar "|." }
  }
}

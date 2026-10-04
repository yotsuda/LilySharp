\version "2.26.0"
%% LP FIDELITY PROBE — A FLAG'S OWN ROOM IN THE STAFF GAP.
%%
%% Run with ../Measure-LilyPondPageGeometry.ps1 -Probe flag-staff-gap.ly
%% (the dump machinery below is copied verbatim from rest-staff-gap.ly, so it reports in the
%% same PROBEV format; the staff gap is the difference of the two VAG rel lines).
%%
%% THE QUESTION (HANDOFF §2 C, "the same half remains on Flag / Accidental / Rest"): LilyPond's
%% Flag declares grob::always-vertical-skylines-from-stencil (scm/define-grobs.scm Flag), so a
%% staff's profile carries the flag glyph's traced OUTLINE (lily/stencil-integral.cc:535-563
%% add_named_glyph_segments). Lily#'s SkylineBuilder seeded an ordinary flag as a nominal
%% 1.2 x 2.5 box flat at the stem's tip (EngravingDefaults.FlagWidth, LILYSHARP-OWN). Nothing
%% measured the difference: a flag's top at the stem IS the stem's tip, so a reader that sees
%% the stem too cannot tell the two apart.
%%
%% THE SHAPE: the upper staff's C4 with its stem forced DOWN (the stem stands at the head's
%% LEFT and the flag rises off its lower end, running right) over the lower staff's C4 with
%% its stem forced UP (the stem stands at the head's RIGHT, under the flag's far end). The two
%% stems never meet in X, so the flag is the only ink that can reach the lower stem: an outline
%% that rises as it runs right does not, a box flat at the tip does.
%%
%% FDC is the control and differs by one token: a quarter, so no flag (and the quarter's stem
%% is 0.5 shorter — stem-shorten (1.0 0.5 0.25) by flag count for an unnatural direction —
%% which only matters where the stem itself binds, and here it does not). FDL16 is FDL with a
%% sixteenth's flag. ragged-bottom, so the number is Align_interface's and not a force the
%% page solved.
%%
%% READ (2.26.0, session 812): FDL 10.626974, FDC 10.045000, FDL16 10.795000.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEV PAPER top-margin=~a bottom-margin=~a paper-height=~a paper-width=~a output-scale=~a line-width=~a\n"
           (ly:output-def-lookup layout 'top-margin)
           (ly:output-def-lookup layout 'bottom-margin)
           (ly:output-def-lookup layout 'paper-height)
           (ly:output-def-lookup layout 'paper-width)
           (ly:output-def-lookup layout 'output-scale)
           (ly:output-def-lookup layout 'line-width))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEV PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        ;; Raw prob properties, NOT the paper-system-* helpers: those live
                        ;; in the separate module (lily paper-system) which a .ly file's
                        ;; own module does not import, so calling them here fails with
                        ;; "Unbound variable" only once page breaking is already done.
                        ;; paper-system-extent is ly:stencil-extent of exactly this
                        ;; stencil (scm/lily/paper-system.scm:56), and the stencil is what
                        ;; scm/page.scm:195 places, so its extent is relative to the same
                        ;; refpoint the Y-offset is measured to.
                        (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                        ;; The extent of the STAVES about that refpoint. LilyPond spaces
                        ;; systems staff-to-staff, not ink-to-ink, so this is the extent
                        ;; system-system-spacing actually works against.
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   ;; Y-offset is already set: Page::page_stencil runs before
                   ;; page-post-process (lily/paper-book.cc:775-788) and
                   ;; page-translate-systems fills it in from 'configuration.
                   (format #t "PROBEV SYS ~a ~a y=~a ext=(~a . ~a) staff=(~a . ~a) title=~a\n"
                           n i
                           (ly:prob-property sys 'Y-offset 0.0)
                           (car ext) (cdr ext)
                           (car staff) (cdr staff)
                           (if (equal? #t (ly:prob-property sys 'is-title)) 1 0))
                   ;; EVERY vertical axis group of this system, spaceable or not. The SYS
                   ;; line above cannot show a loose line at all: staff-refpoint-extent is
                   ;; built from the SPACEABLE staves only (lily/system.cc:706-717), which
                   ;; is exactly the set the page's spring chain contains. A Lyrics line is
                   ;; not in it, and it is placed by a SECOND spacer afterwards
                   ;; (page-layout-problem.cc:1025-1054), so its distance from its staff is
                   ;; a quantity nothing here was reading.
                   ;;
                   ;; The groups hang off the System's 'vertical-alignment, NOT its own
                   ;; 'elements -- looking in 'elements finds no VerticalAxisGroup at all and
                   ;; prints nothing, silently.
                   ;; `aff` is staff-affinity: () on a spaceable staff, 1 (UP) or -1 (DOWN)
                   ;; on a loose line, which is what says WHICH staff the line belongs to.
                   (let* ((sg (ly:prob-property sys 'system-grob))
                          (align (if (ly:grob? sg)
                                     (ly:grob-object sg 'vertical-alignment)
                                     #f)))
                     (if (ly:grob? align)
                         (for-each
                          (lambda (g)
                            (format #t "PROBEV VAG ~a ~a rel=~a aff=~a ext=(~a . ~a)\n"
                                    n i
                                    (ly:grob-relative-coordinate g sg Y)
                                    (ly:grob-property g 'staff-affinity)
                                    (car (ly:grob-extent g g Y))
                                    (cdr (ly:grob-extent g g Y))))
                          (ly:grob-array->list (ly:grob-object align 'elements))))
                     ;; ...and the outside-staff grobs that ride ABOVE a staff, which the
                     ;; VAG line cannot show either: they are inside the group's skyline,
                     ;; so they set `min_offsets[0]` — the ink a system reserves above its
                     ;; own reference point — without appearing as a group of their own.
                     ;; `rel` is the grob's own reference point (a text grob's BASELINE);
                     ;; subtract the VAG rel above to get its height over the staff.
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    ;; Clef rides along because the QUESTION about a bar
                                    ;; number is whether the two overlap in X: the number is
                                    ;; break-aligned to `left-edge` with the comment "want the
                                    ;; bar number before the clef at line start"
                                    ;; (define-grobs.scm:322-323) AND declares
                                    ;; extra-spacing-width (+inf.0 . -inf.0), i.e. it takes no
                                    ;; horizontal room, so "before the clef" does not by itself
                                    ;; say they are disjoint. X is printed as the grob's own
                                    ;; span about the SYSTEM, ready to intersect.
                                    (if (or (eq? nm 'BarNumber) (eq? nm 'Clef))
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

%% Tag each book so the parser can keep the regimes apart. Mixing them is exactly the
%% mistake HANDOFF 5.3 warns about: a stretched page and an unstretched one do not measure
%% the same spring.
%% ⚠️ THE SERIF FONT IS PINNED, and it is not cosmetic. ly/paper-defaults-init.ly:170-173
%% sets `fonts.serif` to "LilyPond Serif" for every backend EXCEPT svg, where it falls back
%% to the bare name "serif" — i.e. to whatever fontconfig happens to resolve on this
%% machine. The measuring script runs -dbackend=svg (it needs realized pages), so any
%% quantity with TEXT in its binding ink was being measured against the wrong font and
%% against a machine-dependent one.
%%
%% Measured, not assumed: with the pin removed, the eight books whose binding ink is glyphs
%% or staff lines (N J S L T P D Q) print IDENTICAL numbers on both backends, and the four
%% tuplet books differ by exactly 0.027492 — the TupletNumber is the only text in any
%% binding pair in this file. Pinning here rather than per-book so that the next book with
%% text in it cannot inherit the bug.
%%
%% ⚠️ THE SANS FONT IS PINNED TOO (2026-07-29): fonts.sans falls back the same way under
%% svg (ly/paper-defaults-init.ly:174-177), and CHORD ROWS are sans text — every chord-row
%% book here (LYRC / LYRCH / LYRMC / LYROS family) had its ChordName ink measured against
%% this machine's fontconfig pick for generic "sans" (Verdana metrics — found and measured
%% in chord-symbol-width.ly's header, ext("Am") 4.336200 against the canonical 3.926480).
%% Quantities the chord ink binds were re-measured after the pin.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

\book {
  \probeTag "FDL"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble \stemDown c'8 r8 r4 r2 }
      \new Staff { \clef bass \stemUp c'4 r4 r2 }
    >>
  }
}
\book {
  \probeTag "FDC"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble \stemDown c'4 r4 r2 }
      \new Staff { \clef bass \stemUp c'4 r4 r2 }
    >>
  }
}
\book {
  \probeTag "FDL16"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble \stemDown c'16 r16 r8 r4 r2 }
      \new Staff { \clef bass \stemUp c'4 r4 r2 }
    >>
  }
}

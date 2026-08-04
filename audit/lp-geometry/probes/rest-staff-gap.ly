\version "2.26.0"
%% LP FIDELITY PROBE — A REST'S OWN ROOM IN THE STAFF GAP.
%%
%% Split out of page-vertical.ly rather than added to it: that file carries books of 120
%% measures and takes a quarter of an hour to sweep, which is the wrong iteration cost for
%% a quantity nobody has measured before. The dumping machinery below is COPIED from it
%% verbatim (probe-dump-pages and probeTag) so the two files report in one format and
%% Measure-LilyPondPageGeometry.ps1 parses this one with -Probe rest-staff-gap.ly.
%%
%% Everything printed is in STAFF SPACES; see page-vertical.ly's header for why, and for
%% the `;` versus `%%` hazard inside #(...) blocks.


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

%% RSTD / RSTC — THE PAIR IS THE POINT. A rest is ordinary ink inside its staff's axis
%%     group (lily/axis-group-interface.cc:914-935 puts every inside-staff grob's
%%     vertical-skylines in), so a rest that leaves the staff must widen the gap exactly as
%%     a low note does. Nothing in the corpus reached one: Lily# seeds a rest as a fixed
%%     1.0 x 1.0 box CENTRED ON THE MIDDLE LINE, which the staff symbol's own 2.05 swallows
%%     whole, so that seed has never been able to bind anything at all.
%%
%%     WHY TWO VOICES AND WHY QUARTER RESTS. A rest at its ordinary position cannot bind on
%%     either engine: measured on 2.26.0, a plain staff of quarter rests has a
%%     VerticalAxisGroup Y-extent of (-3.55 . 3.80) -- the clef, not the rest. \voiceTwo
%%     pushes the rest down, and the DURATION decides how far: the same book with whole
%%     rests stays at -3.55 (a whole rest hangs from a line and is only 0.625 deep) while
%%     quarter rests take it to -4.25. The quarter rest is the one that leaves the staff.
%%
%%     WHY THE LOWER STAFF REACHES UP. 4.25 + 2.05 + 1 = 7.30 loses to StaffGrouper's
%%     basic-distance of 9 and would measure nothing, so the bass staff carries the same
%%     written b' book Q uses -- six spaces above ITS middle line -- and the two protrusions
%%     meet at the same x. That is probe P's construction applied on both sides at once.
%%
%%     RSTC IS THE CONTROL AND DIFFERS BY ONE TOKEN: voice two holds spacer rests instead of
%%     printed ones. Same voices, same forced stem directions, same columns, no rest ink. So
%%     RSTD - RSTC is the rest's whole contribution and nothing else, on either engine.
%%
%%     ragged-bottom, so the number is Align_interface's and not a force the page solved.
\book {
  \probeTag "RSTD"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff << { \voiceOne b'4 b' b' b' } \\ { \voiceTwo r4 r r r } >>
      \new Staff { \clef bass b'4 b' b' b' }
    >>
  }
}

\book {
  \probeTag "RSTC"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff << { \voiceOne b'4 b' b' b' } \\ { \voiceTwo s4 s s s } >>
      \new Staff { \clef bass b'4 b' b' b' }
    >>
  }
}


%% RSTU / RSTUC — THE SAME QUANTITY WITH THE REST GOING THE OTHER WAY, and it is not
%%     redundant. RSTD binds the LOWER staff's top line against a rest pushed DOWN out of
%%     the upper staff; RSTU binds the UPPER staff's bottom line against a rest pushed UP
%%     out of the lower one. Those are two directions of one formula
%%     (rest-collision.cc:270-272 is signed by `dir`) and two edges of the staff symbol, so
%%     a sign error or a rounding that only fires one way lives exactly here. It is book Q's
%%     relationship to book P, applied to the rest.
%%
%%     THE MIRROR IS EXACT: the rest is now in voice ONE (which points UP), its partner
%%     voice holds the notes, and the staff that has to be cleared is the one ABOVE. The
%%     upper staff carries book P's `d` -- six spaces below ITS middle line -- so the two
%%     protrusions meet at the same x, as they do in RSTD.
%%
%%     RSTUC is the control on the same one-token rule: voice one holds spacer rests.
%%
%%     ragged-bottom, so the number is Align_interface's and not a force the page solved.
\book {
  \probeTag "RSTU"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble d4 d d d }
      \new Staff { \clef bass << { \voiceOne r4 r r r } \\ { \voiceTwo d4 d d d } >> }
    >>
  }
}

\book {
  \probeTag "RSTUC"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble d4 d d d }
      \new Staff { \clef bass << { \voiceOne s4 s s s } \\ { \voiceTwo d4 d d d } >> }
    >>
  }
}


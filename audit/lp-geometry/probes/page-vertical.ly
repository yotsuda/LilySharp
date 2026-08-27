\version "2.26.0"
%% LP FIDELITY PROBE — page VERTICAL geometry (Page_layout_problem / the page breaker).
%%
%% The X probe (barline-spacing.ly) measures inside one system. This one measures the page:
%% how far below the paper edge the first system's ink starts, how far apart consecutive
%% systems sit, and how many of them the breaker puts on a page. Those are the quantities
%% HANDOFF.md 2-8 is about.
%%
%% Books P and Q at the bottom measure a DIFFERENT owner with the same dump: the distance
%% between two staves INSIDE one system, which Align_interface decides, not the page's
%% springs. They ride here rather than in a file of their own because the quantity is
%% already in the dump — `staff-refpoint-extent` is the interval over every spaceable
%% staff's refpoint (lily/system.cc:705-717), so on a two-staff system its WIDTH is the
%% staff-to-staff distance, exactly as the distance between two systems is the difference
%% of two such refpoints.
%%
%% Run it with ../Measure-LilyPondPageGeometry.ps1.
%%
%% WHY A DEDICATED PROBE, AND WHY NO MARKUP
%%
%% The measurement this replaces lived in a scratchpad and is gone; worse, it carried a
%% `section` mark on the Lily# side with no counterpart here, which put roughly 3.2 ss of
%% header into a difference that was being read as margin. There is deliberately NO
%% \header, NO title and NO markup in this file: the first system must be a system, not a
%% title, so that `top-system-spacing` governs the top of the page rather than
%% `top-markup-spacing` (scm/page.scm:67-87 picks between them on paper-system-title?).
%%
%% Everything printed is in STAFF SPACES. The paper module's dimension variables are
%% divided by output-scale when the paper is normalized (scm/paper.scm:427-432), and
%% stencil coordinates are multiplied by output-scale only at output time, so the page
%% coordinate system these numbers live in is staff spaces throughout.
%%
%% The Y-offset printed for each system is what scm/page.scm:184-192 subtracts: the system
%% stencil is placed at `-(Y-offset + top-margin)` from the TOP paper edge. So
%%
%%     distance from paper top edge down to the system's refpoint = Y-offset + top-margin
%%     distance from paper top edge down to its topmost ink       = that - (cdr Y-extent)
%%
%% and the parser script does exactly that arithmetic, nothing else.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`. A `%%` line in a Scheme
%% block is read as part of the expression and LilyPond reports it as a syntax error at
%% the top of the whole definition, which points nowhere near the offending line.

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
                                    ;;
                                    ;; RehearsalMark and ChordName ride along for the SAME
                                    ;; reason and it is not decoration: books ROWM / ROWMX /
                                    ;; ROWMY turn on whether a mark and a chord overlap in X,
                                    ;; and until these two names were here NO run of this file
                                    ;; printed either of them -- the mark is not a
                                    ;; VerticalAxisGroup, so the VAG lines cannot show it, and
                                    ;; the SYS line only carries the system's total ink. A
                                    ;; session read the difference off the system ORIGIN
                                    ;; instead and got two terms that are not quantities (see
                                    ;; ROWMX's header). Print the grobs.
                                    (if (memq nm '(BarNumber Clef RehearsalMark ChordName))
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

%% N — NATURAL. ragged-bottom AND few enough systems to fit without compression, so every
%%     gap is the spring's own length. This is the regime that yields
%%     `system.natural-distance`.
%%
%%     The music must be SHORT here. With a page's worth of systems LilyPond warns
%%     "ragged-bottom was specified, but page must be compressed" and compresses anyway —
%%     the flag suppresses stretching, not compression — and the gaps stop being natural
%%     while still looking like a ragged-bottom measurement.
\book {
  \probeTag "N"
  \paper { ragged-bottom = ##t }
  \score { \new Staff { \repeat unfold 24 { c'4 d' e' f' } } }
}

%% J — JUSTIFIED, i.e. LilyPond's shipping default (ragged-bottom = ##f,
%%     ragged-last-bottom = ##t). Every page but the last is filled, so the gaps here are
%%     the breaker's chosen force rather than the natural length. This is the regime that
%%     yields `system.compressed-distance` and the page-1 system COUNT — the two numbers
%%     HANDOFF 2-8 is chasing.
\book {
  \probeTag "J"
  \score { \new Staff { \repeat unfold 150 { c'4 d' e' f' } } }
}

%% S — the same JUSTIFIED shape as J, but with music chosen so the deepest ink on every
%%     system is the CLEF and nothing else. a' sits one step BELOW the middle line, which
%%     is what makes its stem point UP: the head reaches only 1.045 below the middle, the
%%     stem goes the other way, and the staff's own bottom line at 2.0 is the only thing
%%     left under it. The clef reaches 3.540, so it decides the extent by a wide margin.
%%
%%     Do NOT write this on the middle line. b' looks like the natural choice and is a
%%     trap: a note ON the middle line takes a DOWN stem, which reaches 3.5 below it and
%%     shadows the clef's 3.540 to within 0.04. Measured that way first, and the probe
%%     nearly failed to show anything.
%%
%%     Why the book is worth having: LilyPond's clef is an ordinary inside-staff grob and
%%     joins the staff's vertical skyline, so it — not the notes — sets last-bottom-spacing's
%%     floor and through it the page's force. Book J cannot catch a port that leaves the
%%     clef out: there a c' notehead reaches 3.545, five thousandths PAST the clef, and the
%%     number comes out right for the wrong reason.
\book {
  \probeTag "S"
  \score { \new Staff { \repeat unfold 150 { a'4 a' a' a' } } }
}

%% L — the SAME short music as N but on the shipping default paper, so the one page it
%%     produces is also the LAST page and `ragged-last-bottom = ##t` governs it. N and L
%%     differ only in which flag is doing the work, which is what makes the pair able to
%%     answer "does LilyPond leave a last page at its natural spacing?" — the question
%%     behind the reported symptom that the last page's systems sat closer together than
%%     every other page's.
\book {
  \probeTag "L"
  \score { \new Staff { \repeat unfold 24 { c'4 d' e' f' } } }
}

%% T — TIGHT PAPER, and the quantity is the PAGE BREAKER's own: how many systems it decides
%%     to put on a page, and how many pages that takes. Everything above reads a page that
%%     ALREADY holds N systems and would stay green if N were wrong.
%%
%%     Why the paper is shrunk rather than the music lengthened: measured 2026-07-22, book
%%     J's page-1 count of 13 is NOT set by the page's capacity. Raising the first system by
%%     up to four octaves (eight ledger steps) leaves it at 13 every time, because on A4 the
%%     count is chosen by the breaker's DEMERITS — the force each candidate page solves to —
%%     and not by a rod hitting the ceiling. A probe on default paper therefore cannot see
%%     the breaker's arithmetic at all. Shrinking the paper until a page holds a handful of
%%     systems puts the force where a small error in it changes the answer.
%%
%%     ⚠️ CORRECTED 2026-07-25 — THIS BOOK USED TO HAVE NO `indent = 0`, and it was the only
%%     page book here without one (the four inter-system books below all say it). It
%%     therefore engraved at LilyPond's DEFAULT 15mm indent while its Lily# twin renders at
%%     LayoutOptions.Default, whose indent is 0: the two sides of the pair were not setting
%%     the same page. Measured (jn-line-forces.ly, scores TPT and TPD): the same forty bars
%%     on this same tight paper give SIX systems cut 6,7,7,7,7,6 at the default indent and
%%     FIVE systems of eight bars at indent 0 — the six WAS the indent, not the page breaker
%%     and not paging. The paragraph below is kept because its reasoning about WHY the paper
%%     is shrunk still holds, but its counts were read on the indented engraving.
%%
%%     40 bars is six systems at this line width. On 2.26.0 LilyPond splits them 5 + 1 across
%%     two pages for every paper height up to 75 staff spaces; Lily# does so up to 76. So 70
%%     sits five or six staff spaces inside BOTH plateaus — deliberately not on either
%%     side's boundary, so the entry reads the model rather than a rounding.
%%
%%     ⚠️ Do NOT raise this book's paper looking for a sharper reading. Above 75 the two
%%     sides stop measuring the same thing: at 76 and 77 LilyPond does not fit six systems
%%     onto one page, it RE-BREAKS the music into FIVE systems and puts those on one page.
%%     LILYPOND-REF: lily/optimal-page-breaking.cc:139-173 — Optimal_page_breaking::solve
%%     sweeps sys_count downward from the line breaker's ideal and keeps the global argmin
%%     of demerits, so in LilyPond the PAGE breaker chooses the LINE breaking. Lily# breaks
%%     lines once and pages afterwards and cannot produce that answer at all.
%%
%%     ⚠️ This dump prints one line per PAGE and was observed to lose lines (a book showing
%%     only "PAGE 1 systems=5" for a two-page result). If a book's pages do not add up to
%%     the score's systems, re-measure with a one-line-per-BOOK dump before believing it —
%%     that mistake is what produced the since-corrected claim that LilyPond held two pages
%%     through 77 and flipped at 79.
%%
%%     paper-height is written in mm because that is what \paper takes; 123.0109mm is 70
%%     staff spaces at the default 20pt staff (output-scale 1.757299 mm/ss). The dump prints
%%     it back as 69.99998, and the 1.7e-5 is the mm rounding, not a disagreement — these two
%%     entries are integer counts and cannot be moved by it.
%%
%%     The Lily# twin passes the same height through the harness (RenderedGeometry.Render's
%%     LayoutOptions parameter) rather than in its source: paper-height is a \paper variable
%%     in LilyPond, not a grob property, so .lys has no faithful spelling for it and one was
%%     deliberately not invented.
\book {
  \probeTag "T"
  \paper { paper-height = 123.0109\mm indent = 0 }
  \score { \new Staff { \repeat unfold 40 { c'4 d' e' f' } } }
}

%% P — TWO STAVES, and the quantity is INSIDE the system. Align_interface puts adjacent
%%     staves at
%%
%%         max (skyline-distance + padding, minimum-distance, basic-distance)
%%
%%     (lily/align-interface.cc:228-238) with StaffGrouper's 9 / 7 / 1
%%     (scm/define-grobs.scm:3352-3355). The staff LINES are ordinary ink in that skyline,
%%     and making them the binding side is the whole purpose of this book:
%%
%%       * `d` in the TREBLE staff hangs 6 staff spaces below the middle line (position
%%         -12) and its head reaches 0.545 further, so the upper staff's down-skyline is
%%         6.545 there;
%%       * the SAME written pitch in the bass staff is that staff's MIDDLE LINE, so at
%%         that x nothing on the lower staff rises above its own top line.
%%
%%     6.545 + 2.05 + 1 = 9.595, which beats basic-distance 9 — and the 2.05 is a staff
%%     line's INK (half of its 0.1 thickness past the line's centre at 2.0). That 0.05 is
%%     what this book exists to see.
%%
%%     A plain two-staff score cannot see it. With nothing protruding, both sides are
%%     staff lines: 2.05 + 2.05 + 1 = 5.1, basic-distance 9 wins, and the staff symbol's
%%     extent leaves no trace in the output at all.
%%
%%     ragged-bottom, so the page's own springs stay at their natural length and the
%%     number read here is Align_interface's, not a force the page breaker solved for.
\book {
  \probeTag "P"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble d1 }
      \new Staff { \clef bass d1 }
    >>
  }
}

%% D — a WHOLE NOTE with a FORCED-DOWN stem, carrying a dynamic, and the quantity is again
%%     the staff-to-staff distance. This exists to measure one specific suspicion:
%%     Lily#'s DynamicEngraver.GetLowestExtent subtracts a full stem length from any
%%     down-stemmed note WITHOUT checking the duration, so a whole note — which has no stem
%%     at all — reserves one. Same defect 89aaa29f removed from SkylineBuilder, at a site no
%%     ledger entry reached.
%%
%%     Why the two voices. The defect only fires on a DOWN stem, which by the default
%%     direction rule means a notehead at or above the middle line — too shallow for the
%%     dynamic below it to beat StaffGrouper's basic-distance of 9, so the gap would sit on
%%     that floor and measure nothing. \voiceTwo forces the stem down on a note placed as
%%     LOW as we like, which is what makes the two requirements (down stem, and deep enough
%%     to bind) satisfiable at once. Voice one holds the middle line so the staff has an
%%     ordinary upper voice and the pair is a normal two-voice texture.
%%
%%     LilyPond draws no stem here either way (lily/stem.cc Stem::is_normal_stem — duration
%%     log >= 1), so `a` reaches only its notehead's 0.545 below its centre at -4.0, i.e.
%%     4.545 below the staff refpoint. Lily# reserves 3.5, reaching 7.5. The dynamic hangs
%%     from whichever it is, so the difference should arrive at the staff gap nearly intact.
%%
%%     ragged-bottom, so the page's own springs stay natural and the number read here is
%%     Align_interface's.
\book {
  \probeTag "D"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff << { \voiceOne b'1 } \\ { \voiceTwo a1\f } >>
      \new Staff { \clef bass d1 }
    >>
  }
}

%% Q — P with the protrusion on the OTHER side, and it is not redundant. P binds the LOWER
%%     staff's TOP line against ink coming down; Q binds the UPPER staff's BOTTOM line
%%     against ink going up. Those are two different edges of the staff symbol reached
%%     through two different skylines, which is precisely where a sign or a frame goes
%%     wrong without anything else noticing.
%%
%%     `b'` is the treble staff's middle line and, in the bass staff, sits 6 spaces ABOVE
%%     the middle line (position +12). So the arithmetic mirrors P exactly —
%%     2.05 + 6.545 + 1 = 9.595 — and the two books must print the SAME number. A
%%     difference between them is a defect on its own, independent of the value.
\book {
  \probeTag "Q"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble b'1 }
      \new Staff { \clef bass b'1 }
    >>
  }
}

%% TU / TD — a TUPLET BRACKET over STEMLESS whole notes, reaching into the staff gap from
%%     below (TU) and from above (TD). Same quantity as P/Q/D: Align_interface's
%%     staff-to-staff distance. Two suspicions are measured at once here, and the pair is
%%     built so that they can be told apart by SIGN.
%%
%%     (1) Lily#'s SkylineBuilder does not know the word "tuplet". Neither
%%         BuildAllStaffSkylines (the staff gap) nor AugmentSkylinesForPaging (the page)
%%         seeds a TupletBracket, so the bracket is reserved NOWHERE. LilyPond's is an
%%         ordinary inside-staff grob of the VerticalAxisGroup — scm/define-grobs.scm
%%         TupletBracket carries `vertical-skylines` from its stencil and, although it
%%         lists outside-staff-interface, it sets NO outside-staff-priority, so it is not
%%         pushed out and joins the staff's own skyline like the clef does.
%%
%%     (2) TupletBracketEngraver.cs:573,585 adds DefaultStemLength 3.5 to the extreme note
%%         WITHOUT testing the duration, so a whole note gets a stem it has not got. Third
%%         instance of the defect 89aaa29f removed from SkylineBuilder and 26afa9fe from
%%         DynamicEngraver. LILYPOND-REF: lily/stem.cc Stem::is_normal_stem (duration-log
%%         >= 1); the bracket's own encompass points are the note columns' extents,
%%         lily/tuplet-bracket.cc calc_position_and_height.
%%
%%     MEASURED on 2.26.0, unperturbed: LilyPond puts the bracket at the notehead's INK
%%     plus TupletBracket's padding 1.1 and nothing else — a whole-note tuplet on `d'` in
%%     the bass staff (3.5 spaces above the middle line) reports positions (5.145 . 5.145)
%%     = 3.5 + 0.545 + 1.1, and the drawn line sits exactly 5.145 above the refpoint. The
%%     same music in Lily# draws it 8.100 above, i.e. 3.5 - 0.545 = 2.955 too far.
%%
%%     ⚠️ THE OUTERMOST INK IS THE NUMBER, NOT THE BRACKET. lily/tuplet-number.cc:342
%%     returns `to_bracket` — the midpoint of the bracket's own positions — as the
%%     TupletNumber's Y-offset for every tuplet that is not a knee against a beam, and
%%     :227-228 aligns its stencil to CENTER on both axes. So the digit straddles the
%%     bracket line and reaches num_height/2 = 0.627717 past it. Both books' gaps close to
%%     six digits on `notehead ink + 1.1 + 0.627717 + 2.05 + 1`. Do not attribute that
%%     0.627717 to the bracket's own half-thickness: the bracket is 0.16 thick and its
%%     0.08 never reaches the outside.  So the
%%     two defects push the GAP in OPPOSITE directions: (1) makes Lily# too small, (2)
%%     makes it too large once (1) is fixed. Seeding the bracket without guarding the stem
%%     lands the entry on roughly +2.94 rather than 0, and because it would cross zero it
%%     must not be read as "nearly there".
%%
%%     ⚠️ THE PITCH IS NOT FREE. On `d'` the bracket reaches 5.225 above the lower
%%     refpoint and 5.225 + 2.05 + 1 = 8.275 LOSES to StaffGrouper's basic-distance 9 — so
%%     that book prints 9.000000 on BOTH sides and measures nothing at all. Measured, not
%%     assumed: it does print 9.000000. The notes are raised until the bracket beats the
%%     floor with room to spare, which is the same requirement P and Q are built around.
%%
%%     ⚠️ TWO VOICES, as in book D, and for the same reason: the bracket sits on its
%%     voice's stem side, so \voiceOne / \voiceTwo is what makes "bracket on the side
%%     facing the other staff" and "notes deep enough to beat the floor" satisfiable at
%%     once. Left to the default direction rule the bracket always ends up on the side
%%     AWAY from the gap. The .lys twins are polyphonic for the same reason — Lily# takes
%%     the bracket's side from VoiceDefaults only when the staff has more than one voice.
%%
%%     ⚠️ DO NOT interrogate these grobs with an after-line-breaking callback that reads a
%%     SYSTEM-relative coordinate. Doing so forces the vertical alignment early and MOVES
%%     the answer: measured that way this same music reported a staff gap of 18.000000
%%     against the 9.000000 it actually has. The numbers below come from the drawn output.
\book {
  \probeTag "TU"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \time 8/4 b'1 b'1 }
      \new Staff { \clef bass \time 8/4
        << { \voiceOne \tuplet 3/2 { a'1 a'1 a'1 } } \\ { \voiceTwo d1 d1 } >> }
    >>
  }
}

%% TSD / TSU — the SAME tuplet bracket, measured BETWEEN SYSTEMS instead of between staves.
%%     TU and TD reach MultiStaffLayouter.BuildAllStaffSkylines; nothing in the corpus
%%     reaches LayoutEngine.AugmentSkylinesForPaging, which is the other place Lily# builds
%%     a vertical skyline and which still does not seed a TupletBracket. (Lily#'s
%%     EnrichExtentsWithAnnotationProtrusions DOES see tuplets, but it feeds the scalar
%%     fallback that the skyline path beats whenever a skyline exists — so it never
%%     decides anything.) One staff, several systems, so the same StaffGap() that reads
%%     Align_interface in TU/TD reads system-system-spacing here.
%%
%%     ragged-bottom and short enough for one page, so the gap is the spring's own natural
%%     length — the regime of books N and L, NOT the solved force of J. Mixing them is what
%%     HANDOFF 5.3 exists to prevent.
%%
%%     ⚠️ THE PITCH IS NOT FREE, AND THE FLOOR IS MUCH HIGHER HERE. Between staves the
%%     bracket only has to beat StaffGrouper's 9; between systems it has to beat
%%     system-system-spacing's basic-distance of TWELVE. The notes are put 8 staff spaces
%%     outside the middle line so the bracket clears that by more than a staff space:
%%     8 + 0.545 (notehead ink) + 1.1 (padding) + 0.627717 (half the tuplet NUMBER, which
%%     straddles the line — see TU) + 2.05 (the other system's staff line ink) + 1
%%     (padding) = 13.322717. On book P's `d` it would come to 11.322717 and LOSE to 12,
%%     printing a number that measures nothing.
%%
%%     ⚠️ AND THE NOTES ALONE MUST NOT BIND, or the entry stops being about the bracket:
%%     8.545 + 2.05 + 1 = 11.595 is under 12, so a Lily# that reserves the notes and not
%%     the bracket sits exactly on the floor. That is what makes the seeded residual read
%%     the whole bracket stack rather than part of it.
%%
%%     Two voices for the reason book D needs them: the bracket sits on its voice's stem
%%     side, and under the default rule that is always the side AWAY from the gap.
%%
%%     ⚠️ EACH BAR OPENS WITH A PLAIN WHOLE NOTE, and that is not decoration. Written as a
%%     bar-filling tuplet the bracket starts right after the clef, and measured that way
%%     the UP book read 14.785225 instead of 13.322717: at that x the OTHER system's
%%     deepest ink is not its staff line at 2.05 but its CLEF at 3.540, and the entry was
%%     silently measuring clef-against-bracket. Confirmed by hiding the clef at line
%%     starts, which moved the number and nothing else did (ledgers and the time signature
%%     were ruled out the same way). That would have folded the clef's own LILC-vs-skyline
%%     sliver — the residual system.clef-bounded-distance carries — into a tuplet entry.
%%     The leading whole note pushes the bracket clear of both system edges, so the ink it
%%     meets is the plain staff line. HANDOFF 5.3: do not mix regimes.
%%
%%     TSD and TSU must print the SAME number — they are the two edges of one gap, and the
%%     notes are the same distance out on each side. A difference between them is a defect
%%     on its own, exactly as for P/Q and TU/TD.
\book {
  \probeTag "TSD"
  %% ⚠️ NO `indent = 0` HERE, and that is deliberate as of 2026-07-25 — the default 15mm is
  %% what puts these six bars on TWO systems at all. Measured: with indent = 0 LilyPond fits
  %% them on ONE system and the inter-system gap this book exists to measure does not exist.
  %% The pair is made comparable from the LILY# side instead, which passes the same indent
  %% through LayoutOptions (LpGeometryProbes.IndentedPaper). Book T went the other way (it
  %% got `indent = 0`) because there the indent was not holding a regime open, it was only
  %% making the two sides break differently.
  \paper { ragged-bottom = ##t }
  \score {
    \new Staff { \time 12/4
      \repeat unfold 6 {
        << { \voiceOne b'1 b'1 b'1 } \\ { \voiceTwo a'1 \tuplet 3/2 { g,1 g,1 g,1 } } >> } }
  }
}

\book {
  \probeTag "TSU"
  %% No `indent = 0` — see TSD: the default indent is what holds the two systems open here.
  \paper { ragged-bottom = ##t }
  \score {
    \new Staff { \time 12/4
      \repeat unfold 6 {
        << { \voiceOne a'1 \tuplet 3/2 { d''''1 d''''1 d''''1 } } \\ { \voiceTwo b'1 b'1 b'1 } >> } }
  }
}

%% TD — the mirror. TU binds the UPPER staff's bottom line against a bracket coming up;
%%     TD binds the LOWER staff's top line against one going down. P/Q are the same pair
%%     for the staff symbol, and the reason is unchanged: an up bracket and a down bracket
%%     are two different edges reached through two different skylines, and a sign error
%%     shows up in exactly one of them. `d` in the treble staff is 6 spaces below the
%%     middle line — the pitch book P already uses for the same purpose.
\book {
  \probeTag "TD"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \time 8/4
        << { \voiceOne b'1 b'1 } \\ { \voiceTwo \tuplet 3/2 { d1 d1 d1 } } >> }
      \new Staff { \clef bass \time 8/4 d1 d1 }
    >>
  }
}

%% SD / SU — a SLUR over stemless whole notes, drooping into the staff gap from ABOVE (SD,
%%     on the upper staff) and reaching up into it from BELOW (SU, on the lower staff). Same
%%     quantity as P/Q and TU/TD: Align_interface's staff-to-staff distance, ragged-bottom so
%%     the springs stay at their natural length.
%%
%%     The suspicion, and the first ledger point ever to reach a SLUR. LilyPond's Slur is an
%%     ordinary inside-staff grob: measured on 2.26.0 it carries `outside-staff-priority` #f
%%     (NONE), so lily/axis-group-interface.cc never pushes it out and it joins the staff's
%%     own vertical skyline exactly as the clef and the tuplet bracket do. Lily#'s
%%     SkylineBuilder does not contain the word "slur"; slurs reach only
%%     EnrichExtentsWithAnnotationProtrusions, which feeds the SCALAR fallback the skyline
%%     path beats wherever a skyline exists (the same architecture that hid the tuplet
%%     bracket until eb8315f8). So between two staves — where note skylines always exist —
%%     the slur should be reserved NOWHERE, and the gap should rest on the notes alone.
%%
%%     MEASURED unperturbed on 2.26.0: this down-slur's own vertical skyline reaches 6.462596
%%     below the upper refpoint (interrogated with probe-glyph on the Slur grob: ext bottom
%%     -6.46..., skyline-down -6.462596, the same LILC-vs-skyline sliver the clef shows). So
%%     LilyPond's gap is 6.462596 + 2.05 (the lower staff's line INK) + 1 (StaffGrouper
%%     padding) = 9.512596. A Lily# that reserves the notes but not the slur reads the g
%%     noteheads instead: bottom 5.045 below the refpoint, and 5.045 + 2.05 + 1 = 8.095 LOSES
%%     to StaffGrouper's basic-distance 9, so it sits on that floor at 9.000000 and this
%%     residual reads floor-minus-LilyPond, -0.512596, the WHOLE slur protrusion past the
%%     floor rather than part of it.
%%
%%     ⚠️ THE PITCH IS NOT FREE, for the reason TU/TD's is not: on a higher note the slur
%%     would not beat 9 and the book would print 9.000000 on both sides and measure nothing;
%%     on a much lower one the NOTES alone would beat 9 and the residual would read only the
%%     slur's protrusion past the noteheads, not the whole thing. `g` (G3, six spaces below
%%     the treble middle line) puts the noteheads under the floor and the slur's droop over
%%     it — measured, not assumed.
%%
%%     ⚠️ DEFAULT DIRECTION, NO \slurDown. Both books rely on Slur::calc_direction: a slur
%%     over notes whose columns point stem-UP (a low note takes an up stem) curves DOWN, and
%%     vice versa. So the treble `g` slurs down and the bass `f'` slurs up with no override,
%%     which is what the .lys twins do too (Lily# has no forced-direction token here) — LP
%%     and Lily# must decide the side by the same rule or the pair is not comparable.
%%
%%     SD and SU must print the SAME number: two edges of one gap reached through two
%%     different skylines (`f'` sits +9 above the bass middle line, the mirror of `g`'s -9
%%     below the treble one), so a difference between them is a defect on its own — the
%%     relationship P/Q and TU/TD are built on.
\book {
  \probeTag "SD"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \time 8/4 g1( g1) }
      \new Staff { \clef bass \time 8/4 d1 d1 }
    >>
  }
}

\book {
  \probeTag "SU"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \time 8/4 b'1 b'1 }
      \new Staff { \clef bass \time 8/4 f'1( f'1) }
    >>
  }
}

%% TID / TIU — the SLUR pair (SD/SU) again with a TIE, the adjacent inside-staff grob.
%%     LilyPond's Tie carries vertical-skylines from its stencil and sets NO
%%     outside-staff-priority (define-grobs.scm Tie), so like the slur and the clef it
%%     joins the staff's own vertical skyline and a staff below must clear its bow. A tie
%%     is FLATTER than a slur (details height-limit 1.0 / ratio 0.333 vs the slur's 2.0 /
%%     0.25), so the tied notes are pushed further out than SD/SU's g/f' to keep the bow
%%     off the basic-distance-9 floor -- e (E3) is -11 below the treble middle line, a'
%%     +11 above the bass one. Measured 2026-07-24: both bind at 9.655901 (margin 0.66
%%     above the floor), and they print the IDENTICAL number, the pair's cross-check.
\book {
  \probeTag "TID"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \time 8/4 e1~ e1 }
      \new Staff { \clef bass \time 8/4 d1 d1 }
    >>
  }
}

\book {
  \probeTag "TIU"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \time 8/4 b'1 b'1 }
      \new Staff { \clef bass \time 8/4 a'1~ a'1 }
    >>
  }
}

%% BMD / BMU — a BEAM over same-pitch eighth notes, reaching into the staff gap from ABOVE
%%     (BMD, a down-stemmed beam on the upper staff) and from BELOW (BMU, an up-stemmed beam
%%     on the lower staff). Same quantity as P/Q, TU/TD and SD/SU: Align_interface's
%%     staff-to-staff distance, ragged-bottom so the springs stay at their natural length.
%%
%%     The suspicion, and the first ledger point ever to reach a BEAM. A beam is drawn by the
%%     quanter at whatever stem length its beat needs, but Lily#'s SkylineBuilder reserves a
%%     per-note box with a FIXED stem of DefaultStemLength 3.5 (AddNoteBoxToSkylines) and
%%     never consults the quanter — so a beam group of low, forced-down eighths reserves a
%%     3.5 stem where LilyPond's quanter draws a SHORTER one. This is the "draws right,
%%     reserves stale" double model, the same shape as the tuplet bracket that was reserved
%%     nowhere: the drawing and the reservation come from two different models.
%%
%%     MEASURED unperturbed on 2.26.0. The beam quantises to positions -6.81 (stem 2.31 from
%%     g's centre at -4.5), so its outer edge is 6.81 + 0.24 (half of Beam.thickness 0.48)
%%     = 7.05 below the upper refpoint, and the gap is 7.05 + 2.05 (the lower staff line's
%%     INK) + 1 (StaffGrouper padding) = 10.100000. A Lily# that reserves the FIXED 3.5 stem
%%     reads g's stem tip at 4.5 + 3.5 = 8.0 instead, for a gap of 8.0 + 2.05 + 1 = 11.05 --
%%     so the predicted residual is +0.95, the whole of the stem it over-reserves.
%%
%%     ⚠️ THE PITCH IS NOT FREE, for the reason SD/SU's is not: g (G3, six spaces below the
%%     treble middle line) puts the beam deep enough that 10.100000 beats StaffGrouper's
%%     basic-distance 9 with room (0.66 above the floor, like the tie), while the noteheads
%%     alone (5.045 below the refpoint, + 2.05 + 1 = 8.095) LOSE to it -- so a Lily# that
%%     reserved the notes but not the stem would sit on the floor and this residual would
%%     read floor-minus-LilyPond rather than the whole stem. On a higher note neither side
%%     would beat 9 and the pair would print 9.000000 and measure nothing.
%%
%%     ⚠️ TWO VOICES, and it is load bearing. A single-voice \stemDown does force a beam
%%     down in LilyPond, but Lily# cannot force a beam group's direction from a single-note
%%     token -- measured, the beam came out UP -- so its twins use a second voice, whose
%%     stems (and beam) LilyPond's \voiceTwo forces down and \voiceOne up. Measured on
%%     2.26.0, the quant is IDENTICAL either way: single-voice \stemDown, \voiceTwo and
%%     \voiceOne all report positions -/+6.81 to fourteen digits, so the two-voice twin is a
%%     faithful mirror of the single-voice defect HANDOFF first measured. Voice one/two also
%%     holds the middle line so each staff is an ordinary two-voice texture.
%%
%%     BMD and BMU must print the SAME number: two edges of one gap reached through two
%%     different skylines (f', +9 above the bass middle line, is the mirror of g's -9 below
%%     the treble one), so a difference between them is a defect on its own -- the
%%     relationship P/Q, TU/TD and SD/SU are built on.
\book {
  \probeTag "BMD"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \time 4/4
        << { \voiceOne b'1 } \\ { \voiceTwo g8 g g g g g g g } >> }
      \new Staff { \clef bass \time 4/4 d1 }
    >>
  }
}

\book {
  \probeTag "BMU"
  \paper { ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \time 4/4 b'1 }
      \new Staff { \clef bass \time 4/4
        << { \voiceOne f'8 f' f' f' f' f' f' f' } \\ { \voiceTwo d1 } >> }
    >>
  }
}

%% SSD / SSU — the SLUR pair (SD/SU) measured BETWEEN SYSTEMS instead of between staves, the
%%     slur's version of TSD/TSU. SD and SU reach MultiStaffLayouter.BuildAllStaffSkylines;
%%     nothing in the corpus reaches LayoutEngine.AugmentSkylinesForPaging with a slur, and
%%     that pass — the one whose skyline the PAGE spaces systems by — seeds tuplet brackets,
%%     figured basses and scripts but NOT slurs (nor ties). So between systems a slur is
%%     reserved NOWHERE, exactly as the tuplet bracket was before it was seeded there. One
%%     staff over several systems, so the same StaffGap() that reads Align_interface in SD/SU
%%     reads system-system-spacing here.
%%
%%     ragged-bottom and short enough for one page, so the gap is the spring's own natural
%%     length — the regime of books N, L and TSD/TSU, NOT the solved force of J. Mixing them
%%     is what HANDOFF 5.3 exists to prevent.
%%
%%     ⚠️ THE PITCH IS NOT FREE, AND THE FLOOR IS TWELVE HERE, not StaffGrouper's nine. The
%%     slur must clear system-system-spacing's basic-distance of TWELVE, and the noteheads
%%     alone must NOT — the same two requirements TSD/TSU are built around. The notes sit 8
%%     staff spaces outside the middle line (the tuplet's proven depth): a whole note at that
%%     pitch reaches 8.545 below the refpoint, so notehead-alone is 8.545 + 2.05 + 1 = 11.595,
%%     UNDER 12 — a Lily# that reserves the notes and not the slur sits exactly on the floor
%%     and the residual reads the WHOLE slur protrusion past it. LilyPond droops the slur to
%%     roughly 9.96 below the refpoint (centre 8.0 + head edge 0.545 + lift 0.5 + arc + ink),
%%     for a gap around 13.0 that clears 12 with more than a staff space to spare. The exact
%%     number is measured, not computed — the arc height follows from the slur span.
%%
%%     ⚠️ SINGLE VOICE, NO \slurDown, unlike TSD/TSU. A tuplet bracket sits on its voice's
%%     stem side and needs a forced voice to face the gap; a slur curves OPPOSITE the stems
%%     (Slur::calc_direction), so a low note (up stem) slurs DOWN and a high note (down stem)
%%     slurs UP with no override — the same default SD/SU rely on. LP and Lily# must decide
%%     the side by the same rule or the pair is not comparable.
%%
%%     ⚠️ EACH BAR OPENS WITH A PLAIN WHOLE NOTE on the middle line, and that is not
%%     decoration — it is the correction TSD/TSU document. Written as a bar-filling slur the
%%     bow would start right after the clef, and at that x the OTHER system's binding ink is
%%     not its staff line at 2.05 but its CLEF (down-skyline 3.540 for the up book, up-skyline
%%     4.776 for the down one), so the entry would silently measure clef-against-slur and fold
%%     the clef's own LILC-vs-skyline sliver (system.clef-bounded-distance) into a slur entry.
%%     The leading whole note pushes the bow clear of both system edges. HANDOFF 5.3.
%%
%%     SSD and SSU must print the SAME number: the notes are the exact mirror (g,, at -16 is
%%     g -9's continuation, d''' at +16 its reflection) and the slur is symmetric about the
%%     attachment, so a difference between them is a defect on its own — the property P/Q,
%%     TU/TD, SD/SU and TSD/TSU are all built on.
\book {
  \probeTag "SSD"
  \paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }
  \score {
    \new Staff \with { \omit TimeSignature } { \time 12/4
      \repeat unfold 4 { b'1 g,1( g,1) } \break
      \repeat unfold 4 { b'1 g,1( g,1) } }
  }
}

\book {
  \probeTag "SSU"
  \paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }
  \score {
    \new Staff \with { \omit TimeSignature } { \time 12/4
      \repeat unfold 4 { b'1 d''''1( d''''1) } \break
      \repeat unfold 4 { b'1 d''''1( d''''1) } }
  }
}

%% TSID / TSIU — the TIE pair (TID/TIU) measured BETWEEN SYSTEMS instead of between staves, the
%%     tie's version of SSD/SSU, and one grob over from them. AddTiesToSkyline reaches
%%     SkylineBuilder.BuildStaffSkylines (the per-staff skyline TID/TIU read); nothing in the
%%     corpus reaches LayoutEngine.AugmentSkylinesForPaging with a tie. That pass — the one the
%%     PAGE spaces systems by — now seeds tuplet brackets AND slurs (SSD/SSU) but still NOT
%%     ties, so between systems a tie is reserved NOWHERE, exactly the hole the slur had before
%%     it was seeded there. One staff over several systems, so the same StaffGap() that reads
%%     Align_interface in TID/TIU reads system-system-spacing here.
%%
%%     ragged-bottom and short enough for one page, so the gap is the spring's natural length
%%     — the regime of books N, L, TSD/TSU and SSD/SSU, NOT the solved force of J. HANDOFF 5.3.
%%
%%     ⚠️ THE PITCH RUNS DEEPER THAN SSD/SSU, and it is the TID design, not the SSD one. A tie
%%     is FLATTER than a slur (details height-limit 1.0 / ratio 0.333 vs the slur's 2.0 / 0.25),
%%     so its bow protrudes far less — a slur clears the floor of 12 by more than a staff space
%%     from 8 ss out (SSD), a tie from that depth would barely reach it. So the tie takes TID's
%%     route instead: put the NOTEHEADS themselves past the floor and let the residual read the
%%     WHOLE tie protrusion on top of them, the way TID reads -0.560901 rather than a
%%     floor-clipped fraction. e, (E2) sits 9 staff spaces below the middle line, so
%%     notehead-alone is 9.0 + 0.545 + 2.05 + 1 = 12.595, already ABOVE 12 — a Lily# that
%%     reserves the notes and not the tie sits on the NOTES, and the residual is exactly the
%%     tie's own droop, the same shape as staff.staff.tie-under-notes' -0.560901.
%%
%%     ⚠️ SINGLE VOICE, NO \tieDown, like SSD/SSU and unlike TSD/TSU. A tie curves by position
%%     (Tie::calc_direction), so a low note ties DOWN and a high one ties UP with no override —
%%     the default TID/TIU rely on. Whole notes carry no stems, so nothing competes with it.
%%
%%     ⚠️ EACH BAR OPENS WITH A PLAIN WHOLE NOTE on the middle line — the SSD/SSU correction.
%%     A bar-filling tie would start right after the clef, and at that x the other system's
%%     binding ink is its CLEF, not its staff line; the leading whole note pushes the bow clear
%%     of both system edges so the entry measures tie-against-staff-line. HANDOFF 5.3.
%%
%%     TSID and TSIU must print the SAME number: e, at -18 and f'''' at +18 are exact mirrors
%%     about the middle line and the tie is symmetric about its attachment, so a difference
%%     between them is a defect on its own — the property P/Q, SD/SU, TSD/TSU and SSD/SSU share.
\book {
  \probeTag "TSID"
  \paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }
  \score {
    \new Staff \with { \omit TimeSignature } { \time 12/4
      \repeat unfold 4 { b'1 e,1~ e,1 } \break
      \repeat unfold 4 { b'1 e,1~ e,1 } }
  }
}

\book {
  \probeTag "TSIU"
  \paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }
  \score {
    \new Staff \with { \omit TimeSignature } { \time 12/4
      \repeat unfold 4 { b'1 f''''1~ f''''1 } \break
      \repeat unfold 4 { b'1 f''''1~ f''''1 } }
  }
}

%% BSD / BSU — the BEAM pair (BMD/BMU) measured BETWEEN SYSTEMS instead of between staves,
%%     the beam's version of TSD/TSU. BMD/BMU reach SkylineBuilder.BuildStaffSkylines, where
%%     the drawn beam is seeded and the members' fixed stems suppressed; nothing in the
%%     corpus reaches LayoutEngine.AugmentSkylinesForPaging with a beam. That pass — the one
%%     the PAGE spaces systems by — now seeds tuplet brackets, slurs and ties but still NOT
%%     beams, and its base skylines come from BuildSkylines, where AddNoteBoxToSkylines
%%     still reserves each beamed member's FIXED DefaultStemLength 3.5 stem. So between
%%     systems the last "draws right, reserves stale" double model survives: the quanter
%%     draws a shortened stem where the page reserves 3.5. One staff over several systems,
%%     so the same StaffGap() that reads Align_interface in BMD/BMU reads
%%     system-system-spacing here.
%%
%%     ragged-bottom and short enough for one page, so the gap is the spring's natural
%%     length — the regime of books N, L, TSD/TSU, SSD/SSU and TSID/TSIU. HANDOFF 5.3.
%%
%%     ⚠️ THE PITCH IS NOT FREE, AND THE FLOOR IS TWELVE HERE, not StaffGrouper's nine.
%%     The notes sit 8 staff spaces outside the middle line, the depth TSD/TSU proved: the
%%     beam's outer edge must clear 12 and the noteheads alone must NOT (8.545 + 2.05 + 1 =
%%     11.595, under 12). On BMD's g (4.5 out) neither side would beat 12 and the pair
%%     would print 12.000000 and measure nothing. All stems are FORCED (head off the middle
%%     line, direction against the default), so beamed-stem-shorten 1.0 applies exactly as
%%     in BMD/BMU; if the quanter picks BMD's stem of 2.31, the outer edge is 8 + 2.31 +
%%     0.24 (half of Beam.thickness 0.48) = 10.55 and the gap 10.55 + 2.05 + 1 = 13.60. A
%%     Lily# that reserves the fixed 3.5 stem reads 8 + 3.5 + 2.05 + 1 = 14.55 instead —
%%     predicted residual +0.95, the whole of the stem it over-reserves, the same number
%%     BMD/BMU carried. The quant at this depth is measured, not assumed.
%%
%%     ⚠️ TWO VOICES, load bearing as in BMD/BMU: Lily# cannot force a beam group's
%%     direction from a single-note token, so the beam lives in the voice whose stems are
%%     forced toward the gap (\voiceTwo down for BSD, \voiceOne up for BSU). Measured on
%%     2.26.0 (BMD), the quant is identical under \stemDown, \voiceTwo and \voiceOne.
%%
%%     ⚠️ EACH BAR OPENS AND CLOSES WITH A PLAIN WHOLE NOTE — the TSD/TSU correction, both
%%     ends. A beam starting at the bar line would sit over the NEXT system's clef
%%     (down-skyline 3.540 / up-skyline 4.776) and fold the clef's LILC-vs-skyline sliver
%%     (system.clef-bounded-distance) into a beam entry; the trailing whole keeps it clear
%%     of the line's final bar line too. The beam spans the middle third of the bar, where
%%     the other system's binding ink is its plain staff line at 2.05. HANDOFF 5.3.
%%
%%     BSD and BSU must print the SAME number: g, at -16 and d'''' at +16 are exact mirrors
%%     about the middle line, and quant, shorten and thickness are direction-symmetric — so
%%     a difference between them is a defect on its own, the property P/Q, TU/TD, SD/SU,
%%     BMD/BMU and TSD/TSU are all built on.
\book {
  \probeTag "BSD"
  \paper { ragged-bottom = ##t }
  \score {
    \new Staff { \time 12/4
      \repeat unfold 6 {
        << { \voiceOne b'1 b'1 b'1 } \\ { \voiceTwo a'1 g,8 g, g, g, g, g, g, g, a'1 } >> } }
  }
}

\book {
  \probeTag "BSU"
  \paper { ragged-bottom = ##t }
  \score {
    \new Staff { \time 12/4
      \repeat unfold 6 {
        << { \voiceOne a'1 d''''8 d'''' d'''' d'''' d'''' d'''' d'''' d'''' a'1 } \\ { \voiceTwo b'1 b'1 b'1 } >> } }
  }
}

%% KNE / KNEC — the SAME-STAFF KNEE, measured between systems, and the question is whether
%%     Lily#'s omission of it is observable at all.
%%
%%     LilyPond leaves only CROSS-staff grobs out of the skylines
%%     (axis-group-interface.cc:850-858, its own comment), so a same-staff kneed Beam and its
%%     Stems ARE in them. Lily# seeds neither: SkylineBuilder.AddBeamsToSkyline skips
%%     `IsKnee`, and BeamedItemsToSuppress skips it too, so the members keep the per-note
%%     FIXED 3.5 stem instead — each in its OWN direction (AddNoteBoxToSkylines takes
%%     note.StemUp, not the group's).
%%
%%     PREDICTION, written before running (section 5.0-2): KNE - KNEC = 0.000000, i.e. the
%%     knee cannot be seen from outside. A knee's stems point INWARD — the low note stems up
%%     to the beam, the high note stems down to it — so the beam band and both stems live
%%     BETWEEN the two heads, and the heads are what the skyline sees either way. LilyPond
%%     only knees when the gap exceeds auto-knee-gap (5.5 ss), which is wider than the 3.5
%%     fixed stem Lily# substitutes, so even that substitute stays inside the envelope.
%%     (falsifier: any difference, which would mean the knee's ink DOES break the heads'
%%      envelope — and then the number to port is the beam band, measured here.)
%%
%%     ⚠️ THE CONTROL IS THE SAME MUSIC WITH THE KNEE SWITCHED OFF, not music without the
%%     leap. The first attempt paired the kneed bar against plain low notes and read a
%%     difference of 6.090000 that was almost entirely d''''`s OWN ink — the trap section 5.0
%%     names ("both sides of a pair must be the same music"), walked into once more. Both books
%%     below carry the IDENTICAL notes and differ only in Beam.auto-knee-gap: 0 forces the
%%     knee, 100 forbids it. What the difference then contains is the knee and nothing else.
%%     KNE takes the paper BSD/BSU take (ragged-bottom alone) so its Lily# twin is the one
%%     those points already validate, and it lets the knee happen NATURALLY: the leap is about
%%     four octaves, far past any auto-knee-gap either engraver could be using, so both knee it
%%     without an override and the twin is just the music. KNEC keeps the override and is a
%%     LilyPond-SIDE reference only — Lily# has no auto-knee-gap to switch.
%%     SIX BARS AND NO \break, the BSD/BSU shape exactly: each engraver breaks where it likes
%%     and the gap is uniform, so the twin does not have to reproduce a break decision. An
%%     earlier draft wrote 4 bars with an explicit \break here and the Lily# side fitted all
%%     four on ONE system — the pair could not even be measured. Same music on both sides
%%     means the same BAR COUNT too (section 5.0).
\book {
  \probeTag "KNE"
  \paper { ragged-bottom = ##t }
  \score {
    \new Staff \with { \omit TimeSignature } { \time 12/4
      \repeat unfold 6 { b'1 g,8 d'''' g, d'''' b'1 g,8 d'''' g, d'''' } }
  }
}

\book {
  \probeTag "KNEC"
  \paper { ragged-bottom = ##t }
  \score {
    \new Staff \with { \omit TimeSignature \override Beam.auto-knee-gap = #100 } { \time 12/4
      \repeat unfold 6 { b'1 g,8 d'''' g, d'''' b'1 g,8 d'''' g, d'''' } }
  }
}

%% IS3 / IS3C — WHICH STAVES the SYSTEM skyline is built from, measured between systems.
%%
%%     Page_layout_problem::build_system_skyline (page-layout-problem.cc:1080-1127) merges
%%     EVERY staff of the system, and its own comment says why: "for the upper skyline, we
%%     pretend that all of the staves in the system are packed together close to the top
%%     system" (:1070-1074) — each staff's vertical-skylines merged at dy taken from
%%     MINIMUM_translations, i.e. the staves packed, not where they finally sit.
%%     Lily#'s SkylineBuilder.BuildSystemSkylines adds the FIRST staff and the LAST staff and
%%     nothing else, so on three staves the middle one contributes nothing at all.
%%
%%     THIS PAIR ASKS WHETHER THAT IS REACHABLE, and the honest expectation is that it is not
%%     by pitch. The middle staff is packed 9 (StaffGrouper basic-distance) below the top, so
%%     its ink must clear 9 staff spaces just to reach the top staff's REFPOINT, and one staff
%%     space is TWO staff positions — 9 ss is 18 diatonic steps, about two and a half octaves
%%     above the middle line, before it competes with anything the top staff draws. IS3 puts
%%     the highest ink a real score would carry on the middle staff (d'''' , the same pitch
%%     BSU/TSU use, 8 ss above its middle line); IS3C is the CONTROL with that staff plain.
%%
%%     PREDICTIONS, written before running (section 5.0-2):
%%       IS3 - IS3C = 0.000000. The middle staff's 8 ss of reach is 1 ss SHORT of its own
%%       9 ss packing offset, so even packed to the top it stays below the top staff's
%%       refpoint and cannot touch the system's up-skyline.
%%       (falsifier: a non-zero difference, which would mean the packing offset is smaller
%%        than StaffGrouper's basic-distance — the number to read then is what LilyPond used,
%%        not what this comment assumed.)
%%
%%     ⚠️ IF THE PREDICTION HOLDS, the finding is that Lily#'s omission of inner staves is
%%     UNREACHABLE BY PITCH, and the reachable half of the same divergence is the OTHER thing
%%     the LilyPond comment says: the offsets are the MINIMUM translations, where Lily# uses
%%     the staves' FINAL positions. That wants its own pair, on a page whose staff springs are
%%     stretched past their minimum — do not fold it into this one.
%%
%%     ★ THE PREDICTION HELD — measured 2026-07-25:
%%
%%       book   top-to-top gap   INSIDE a system   inter-system = gap - inside
%%       IS3      32.595000        20.595000         12.000000
%%       IS3C     30.000000        18.000000         12.000000
%%
%%     The inter-system distance is system-system-spacing's basic-distance 12.000000 in BOTH,
%%     so IS3 - IS3C = 0.000000: eight staff spaces of reach on the middle staff, packed nine
%%     below the top, never touches the system's up-skyline. ALL of the 2.595000 that moved is
%%     INSIDE the system (18.000000 = two StaffGrouper gaps of 9; 20.595000 = 9 + 11.595000,
%%     the same 8.545 + 2.05 + 1 the SSD/SSU header derives), i.e. Align_interface, which is a
%%     different Lily# path (MultiStaffLayouter.BuildAllStaffSkylines) and DOES see every staff.
%%
%%     ⚠️ SO THERE IS NO LEDGER POINT HERE, deliberately: both engravers sit on the 12.000000
%%     floor, so an entry would be exact on both sides while measuring nothing (HANDOFF 5.0,
%%     "do not sit on the floor"). What these books are for is the RECORD that the regime was
%%     measured and does not reach — so the next reader does not re-open it — and they stay
%%     re-runnable if the packing offset ever changes.
%%
%%     ⚠️ A LEAD, NOT A FINDING, from the same run: Lily# renders this shape with an INSIDE
%%     distance of 21.000000 plain and 25.595000 with the high note, i.e. the note pushes the
%%     staves apart by 4.595000 where LilyPond pushes 2.595000. That is the staff-to-staff
%%     path, not this one. ⚠️ DO NOT read it as a 2.0 defect yet: these books are ragged-bottom
%%     on this probe's paper while the Lily# side was rendered on its own default (content-sized)
%%     page, so the two are not the same regime and their floors differ (LilyPond's plain gap is
%%     9.000000, Lily#'s 10.500000). It needs a paper-matched pair before anyone believes it.
\book {
  \probeTag "IS3"
  \paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }
  \score {
    \new StaffGroup <<
      \new Staff \with { \omit TimeSignature } { \time 12/4
        \repeat unfold 4 { b'1 b'1 b'1 } \break \repeat unfold 4 { b'1 b'1 b'1 } }
      \new Staff \with { \omit TimeSignature } { \time 12/4
        \repeat unfold 4 { b'1 d''''1 b'1 } \break \repeat unfold 4 { b'1 d''''1 b'1 } }
      \new Staff \with { \omit TimeSignature } { \time 12/4
        \repeat unfold 4 { b'1 b'1 b'1 } \break \repeat unfold 4 { b'1 b'1 b'1 } }
    >>
  }
}

\book {
  \probeTag "IS3C"
  \paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }
  \score {
    \new StaffGroup <<
      \new Staff \with { \omit TimeSignature } { \time 12/4
        \repeat unfold 4 { b'1 b'1 b'1 } \break \repeat unfold 4 { b'1 b'1 b'1 } }
      \new Staff \with { \omit TimeSignature } { \time 12/4
        \repeat unfold 4 { b'1 b'1 b'1 } \break \repeat unfold 4 { b'1 b'1 b'1 } }
      \new Staff \with { \omit TimeSignature } { \time 12/4
        \repeat unfold 4 { b'1 b'1 b'1 } \break \repeat unfold 4 { b'1 b'1 b'1 } }
    >>
  }
}

%% JSS / JSSC — THE STAFF SPRING INSIDE A SYSTEM, ON A STRETCHED PAGE. This is the OTHER
%%     half of the IS3 divergence, the half that header says wants its own pair.
%%
%%     Page_layout_problem::append_system pushes one spring per spaceable staff of the system
%%     into the SAME chain as the system-to-system springs (page-layout-problem.cc:651-720):
%%     ideal = staff-staff-spacing's basic-distance 9, inverse stretch strength = its
%%     stretchability 5 (define-grobs.scm:3352-3355), then ensure_min_distance floors it at
%%     what Align_interface's minimum translations asked for. One solve sets them all, so on
%%     a page stretched to force f the staves INSIDE a system move apart by 5f while the
%%     systems move apart by 60f (system-system-spacing's stretchability).
%%
%%     Lily# has no such spring: PageLayouter.PositionSystemsOnPage builds one spring per
%%     SYSTEM boundary and nothing else, and every system on every page is drawn at the
%%     score-wide MultiStaffLayouter.CalculateSystemHeight — the Align_interface minimum, at
%%     any force. Which is also the answer to "is BuildSystemSkylines' offset the minimum or
%%     the final position": today it is BOTH, because nothing ever stretches them apart. The
%%     skyline convention (build_system_skyline takes minimum_translations, :1080-1095)
%%     cannot be measured until the spring exists, so this pair measures the spring.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       JSSC (ragged-bottom, natural): staff-to-staff INSIDE a system = 9.000000, the plain
%%       basic-distance, because this music's own skyline asks for less than that: the
%%       treble's c' hangs 3.545 below its middle line, the bass staff's deepest up-stem
%%       reaches 3.0 above its own, and 3.545 + 3.0 + 1 = 7.545 < 9.
%%       JSS (justified, read on page 1 which is FULL): the same distance reads 9 + 5f and
%%       the system-to-system gap on that page reads 12 + 60f, for ONE f — so the falsifier
%%       is inside the same dump: (inside - 9) / 5 must equal (gap - 12) / 60 to six digits.
%%       (falsifier: an inside distance that does not move at all, which would mean the staff
%%        spring is not in the page's chain and Lily#'s fixed system height is right.)
%%
%%     ⚠️ BOTH BOOKS CARRY THE IDENTICAL MUSIC AND THE IDENTICAL BAR COUNT (HANDOFF 5.0), and
%%     differ in ragged-bottom alone. ⚠️ Read page 1, never the last page: ragged-last-bottom
%%     governs that one and it is a different measurement (book L).
%%     ⚠️ The music must NOT sit on the staff spring's floor — nothing here may protrude far
%%     enough for ensure_min_distance to beat 9, or the spring blocks (blocking_force =
%%     (min - ideal) / 5, spring.cc:64-72) and reads its floor at any force this page solves
%%     to. Book P's own shape is exactly that trap: its 9.595 floor blocks until f > 0.119,
%%     and the force book J solves to is 0.0042.
%%
%%     ⚠️ max-systems-per-page IS LOAD BEARING, and the first draft had no cap at all. Left
%%     to choose, the breaker put EIGHT of these two-staff systems on the page and COMPRESSED
%%     them: measured 2026-07-26, the inside distance read 8.651797 — under the 9 it is
%%     supposed to stretch from — and ragged-bottom made not one digit of difference, because
%%     that flag suppresses stretching and not compression (the same trap book N's header
%%     names). A compressed page cannot answer this question: it is the OTHER regime, the one
%%     HANDOFF 2D says is unimplemented in Lily# altogether, so a pair measured there would
%%     read a defect that is not the one being asked about. Capping at six leaves the page
%%     slack to distribute and puts both books on the stretching side of force 0.
%%
%%     ⚠️ THE CAP, NOT systems-per-page = #6, and the difference is the Lily# twin's. Written
%%     as an exact count first: LilyPond breaks this music into 18 systems and pages it 6/6/6,
%%     but Lily# breaks it into 17, so its last page would hold 5 — and its page breaker
%%     rejects every candidate that is not exactly 6 (PageBreaker.cs:523), finds no paging at
%%     all, and falls back to ONE content-sized page carrying all 17 systems (34 staves,
%%     caught by page.stretched.two-staff.staves-on-first-page). A cap admits a short last
%%     page on both sides, and page 1 — the only page these entries read — holds six systems
%%     either way. ⚠️ That 18-vs-17 is a LINE-breaking difference on two-staff music and is
%%     NOT what this pair measures; it is recorded here because it decides how the pair has
%%     to be spelled.
%%
%%     THE FOOT OF THE CHAIN, added 2026-07-26 and read on JSS and JSK alike. Everything
%%     above measures a GAP, which is a spring's length at the page's force; a force is the
%%     page's slack over the chain's total strength, so a fixed term that is wrong at either
%%     END shows up in every gap at once, each scaled by its own spring, and the dump has
%%     nothing to attribute it to. That is how four residuals here turned out to be TWO
%%     forces (HANDOFF 5.3). The script now prints "last STAFF refpoint below edge ... to
%%     foot", which is the span of the spring page-layout-problem.cc:538-545 appends after
%%     the last system — refpoint to the bottom of the band, not the system origin and not
%%     the ink.
%%
%%     MEASURED: 10.023885 on JSS page 1 AND on JSK page 1 — the same number in both
%%     regimes, which is the mechanism and not a coincidence. The spring is ideal 1 with
%%     stretchability 30 (ly/paper-defaults-init.ly:84-87) and ensure_min_distance raises
%%     only its FLOOR, to padding 1 + the last staff's own ink 3.333333, leaving that
%%     strength alone (spring.cc:156-159) — so it blocks at f = (4.333333 - 1) / 30 =
%%     0.111111 and neither page gets there (JSS f = 0.099092, JSK f = -0.174101).
%%     ⚠️ A book that stretches past f = 0.111111 opens this spring and is measuring a
%%     DIFFERENT quantity; it needs its own entry rather than a comparison with these.
%%     ⚠️ The 3.333333 is a middle-line DOWN STEM, shortened from 7 to 20/3 half-spaces by
%%     stem.cc:519-555. Lily# reserved 3.5 until 96641db7, which is exactly the 0.166664
%%     that both regimes' forces independently pointed at.
\book {
  \probeTag "JSS"
  \paper { max-systems-per-page = #6 }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble \repeat unfold 120 { c'4 d' e' f' } }
      \new Staff { \clef bass \repeat unfold 120 { c4 d e f } }
    >>
  }
}

\book {
  \probeTag "JSSC"
  \paper { max-systems-per-page = #6 ragged-bottom = ##t }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble \repeat unfold 120 { c'4 d' e' f' } }
      \new Staff { \clef bass \repeat unfold 120 { c4 d e f } }
    >>
  }
}

%% JSK — THE SAME STAFF SPRING, COMPRESSED. The third regime of one spring: JSSC reads it
%%     at rest (9.000000), JSS stretched, this one squeezed.
%%
%%     It is the SAME MUSIC as JSS/JSSC and differs in one paper number: eight systems to a
%%     page instead of six, which is what the breaker picks for this score when nothing caps
%%     it (measured while writing these books — and it COMPRESSES them, reading 8.651797
%%     inside a system). The cap is written down anyway so both engravers are pinned to the
%%     same page and the entry cannot silently become a page-breaker measurement.
%%
%%     WHICH STRENGTH COMPRESSION USES, and it is not the one stretching uses:
%%     alter_spring_from_spacing_spec sets min-distance 7 and then set_default_strength
%%     (page-layout-problem.cc:1345-1358), so inverse_compress = ideal - min = 9 - 7 = 2
%%     (spring.cc:205-211). ensure_min_distance afterwards raises the FLOOR to the alignment
%%     minimum but does NOT recompute the strength (the setters do not recalculate — the
%%     same fact the note-spacing side of the corpus already leans on). The system spring is
%%     12 / 8, so its compress strength is 4 where its stretch strength was 60.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       inside < 9.000000 on page 1, and the falsifier is again inside the same dump:
%%       (9 - inside) / 2 must equal (12 - system-to-system) / 4 to six digits, one negative
%%       force for the page. ⚠️ It must ALSO stay above the alignment minimum 7.545 (3.545
%%       under the treble's middle line + 3.0 of bass up-stem + 1), or the spring is sitting
%%       on its floor and measuring nothing (HANDOFF 5.0).
%%       (falsifier: an inside distance of exactly 9.000000, which would mean compression
%%        never reaches the staff spring.)
%%
%%     ⚠️ ragged-bottom would NOT undo this and the control is NOT a ragged twin: that flag
%%     suppresses stretching only, so a ragged page that is full compresses just the same
%%     (book N's header says it, and the first draft of JSS walked into it). The control for
%%     THIS entry is page.natural.staff-staff-inside — same music, same bar count, a page
%%     with slack — which is exact on both sides at 9.000000.
\book {
  \probeTag "JSK"
  \paper { max-systems-per-page = #8 }
  \score {
    \new PianoStaff <<
      \new Staff { \clef treble \repeat unfold 120 { c'4 d' e' f' } }
      \new Staff { \clef bass \repeat unfold 120 { c4 d e f } }
    >>
  }
}

%% LYRS / LYRC — WHERE A LOOSE LINE SITS, and the answer is "with its staff, in BOTH
%%     regimes". A Lyrics line is not spaceable, so it is left out of the page's spring
%%     chain and placed afterwards by a SECOND Simple_spacer over the lines between two
%%     already-placed spaceable ones (page-layout-problem.cc:919-942 collects, :1025-1054
%%     solves). The obvious reading of that is "the row shares the page's slack", and it is
%%     WRONG — which is the whole reason this pair is two books and not one.
%%
%%     ★ WHY IT DOES NOT MOVE, from the source: get_spacing_spec hands the spring from a
%%     loose line to a NULL neighbour (the page edge, or the null that breaks affinity at a
%%     system boundary) add_stretchability(SCM_EOL, HUGE_STRETCH = 10e7), and the spring to
%%     a staff on its NON-affinity side LARGE_STRETCH = 10e5 (:1257-1338, with LilyPond's
%%     own comment saying this is deliberate: "a spacing-affinity UP line at the bottom of
%%     the page will still be placed close to its staff"). Against those, the spring to its
%%     OWN staff is an ordinary nonstaff-relatedstaff-spacing with stretchability 1. The
%%     second solve therefore pours essentially all the slack into the huge springs and
%%     gives this one a part in 10e7.
%%     ⚠️ CORRECTED 2026-07-26: this used to say "whose default strength is its ideal, 5.5".
%%     WRONG — ly/engraver-init.ly:648-652 DECLARES (stretchability . 1) for the Lyrics
%%     context, so set_default_strength (spring.cc:213-216) never runs on this spring at all.
%%     The finding is unchanged and slightly stronger (1 gets an even smaller share than 5.5
%%     would), and Lily# was already right; the error was in this header. ⚠️ Do not confuse
%%     it with the COMPRESS strength, which IS ideal - minimum-distance = 5.5 and is what
%%     every LYRV reading below depends on. Two different quantities, one old sentence.
%%
%%     MEASURED, both books, first system of page 1: staff refpoint to Lyrics refpoint =
%%     5.500000 — 5.500000001945665 on the ragged page against 5.500000181705927 on a page
%%     stretched to gaps of ~43. The excess IS the sliver the 5.5 spring takes (about 2e-8
%%     of the slack), and its being two orders of magnitude larger on the stretched page is
%%     the falsifier that the mechanism above is the right one rather than a coincidence.
%%
%%     ⚠️ THE MELODY IS HIGH AND THE SYLLABLE HAS NO ASCENDER, and BOTH are the trap this
%%     pair is built around (HANDOFF 5.3): each side's spring is floored by an alignment
%%     minimum made of the staff's ink, the LYRIC'S OWN INK and a padding, and a reading
%%     that lands on that floor is a measurement of the FONT — the two engravers' lyric
%%     faces differ by about 27%. Both sides have to be off their floors, and they are off
%%     DIFFERENT floors:
%%       - melody at g''/a'' rather than c': with a head 3.55 under the middle line the
%%         LilyPond floor is 5.865115 and beats basic-distance 5.5. Measured on the first
%%         draft, which read exactly that.
%%       - the syllable is "no" rather than "la": Lily#'s own floor is its glyph height, and
%%         `l` is an ascender. Measured with "la", Lily# read 4.662000 — its ink floor, not
%%         its basic distance — so the residual carried BOTH engravers' models at once and
%%         could not name one defect. With "no" it sits on its own basic distance instead.
%%     ⚠️ So this pair says nothing about either floor. A book with a low melody or a tall
%%     syllable measures a different quantity and needs its own entry — and a rule about
%%     whose ink it is allowed to contain.
%%
%%     ⚠️ Four systems to a page, so page 1 keeps real slack. ragged-bottom ALONE would not
%%     make the control (trap 7 in HANDOFF 5.0: a full ragged page compresses anyway), and
%%     the music must be long enough that page 1 is not also the LAST page — the first draft
%%     had 40 bars, which LilyPond re-broke onto a single page, and ragged-last-bottom then
%%     left it unstretched. Both books are 120 bars for that reason.
\book {
  \probeTag "LYRS"
  \paper { max-systems-per-page = #4 }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g''4 a'' g'' a'' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
    >>
  }
}

\book {
  \probeTag "LYRC"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g''4 a'' g'' a'' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
    >>
  }
}

%% LYRV — A SECOND VERSE, which LilyPond spaces from a DIFFERENT SPEC. Everything above
%%     measures the staff-to-lyrics spring (nonstaff-relatedstaff-spacing). Stack a second
%%     Lyrics line under the first and get_spacing_spec takes its loose-to-loose branch
%%     instead (:1315-1332): neither neighbour is spaceable, the upper one's affinity is UP
%%     so the first test `before_affinity != UP` fails, and the second — `after_affinity !=
%%     DOWN`, true for a second UP line — returns the UPPER line's
%%     nonstaff-nonstaff-spacing, which ly/engraver-init.ly:653-656 sets to
%%     ((basic-distance . 0) (minimum-distance . 2.8) (padding . 0.2)).
%%
%%     ⚠️ A ZERO BASIC-DISTANCE WITH A MINIMUM, which is the opposite shape from the spring
%%     above it: the ideal is 0, so the realized step is whatever ensure_min_distance leaves
%%     — max(minimum-distance 2.8, the alignment minimum, which is the two lines' own ink
%%     plus padding 0.2). LilyPond's verse spacing therefore RESPONDS TO THE TEXT and only
%%     falls back to 2.8 when the text is short.
%%
%%     PREDICTION, written before running (HANDOFF 5.0-2): 2.800000. The syllable is "no",
%%     with neither ascender nor descender, so the ink term is about 0.037 (the first line's
%%     overshoot below its baseline) + an x-height + 0.2, well under 2.8 — the minimum
%%     binds and the reading has no font in it.
%%     (falsifier: a number above 2.8, which would mean the ink term binds after all and
%%      this book is measuring the two faces rather than the spec.)
%%
%%     THE PREDICTION HELD — 2.800000 — AND THE CONTROL IT CAME WITH DID NOT, which is the
%%     more interesting half. Same music, same paper and the same syllable as LYRC, so the
%%     first line's own distance was supposed to read 5.500000 again. It reads
%%     "3.737890, 2.800000, 5.500001": the verse step at 2.8 on every system, but
%%     staff-to-verse-1 at 3.737890 on the inner systems and only 5.500001 on the last.
%%     ★ THE LOOSE CHAIN IS COMPRESSED. Two lyric lines plus their paddings no longer fit in
%%     the 12.000000 the system spring keeps (it does NOT widen for them — the staff-to-staff
%%     gap is still 12.000000 all the way down), so distribute_loose_lines solves at a
%%     NEGATIVE force and the first spring gives way to its floor. 3.737890 is that floor:
%%     the staff's own ink 2.05 + the syllable's x-height + padding 0.5 — i.e. a FONT
%%     quantity, which is why it is deliberately not a ledger point.
%%     ⚠️ So LYRV is NOT a control for the staff-to-lyrics distance. LYRC is.
%%     ★ AND THIS IS THE REGIME THE ONE-LINE PAIR SAID TO LOOK FOR: with a single loose line
%%     LilyPond's redistribution is invisible (the springs on the far side carry
%%     LARGE_STRETCH/HUGE_STRETCH), but with TWO the pass really does move them.
%%
%%     ⚠️ WHY THE VERSE STEP IS STILL READABLE IN A COMPRESSED CHAIN, from the source rather
%%     than from luck: basic-distance 0 means set_default_strength gives that spring an
%%     inverse STRETCH strength of 0 (spring.cc:213-216) — it cannot stretch — and
%%     minimum-distance 2.8 means it cannot compress past 2.8 either. The step is RIGID at
%%     max(2.8, ink + 0.2) at every force, which is what makes 2.800000 a spec reading and
%%     not a regime artefact.
%%
%%     PER-SYSTEM READINGS, page 1 (2026-07-26), because the uniqued line above hides WHICH
%%     system is which and a port was nearly built on the wrong reading of it:
%%
%%       sys | staff rel  | v1 rel     | v2 rel      | staff->v1 | v1->v2
%%        0  | -3.776000  | -7.513890  | -10.313890  | 3.737890  | 2.800000
%%        1  | -4.303666  | -8.041556  | -10.841556  | 3.737890  | 2.800000
%%        2  | -4.279226  | -8.017116  | -10.817116  | 3.737890  | 2.800000
%%        3  | -4.279226  | -9.779227  | -12.579227  | 5.500001  | 2.800000
%%
%%     So the INNER systems sit on the alignment minimum and only the LAST one on the page
%%     reaches the basic-distance — which fits: the last system's loose chain runs to
%%     -page_height_, a span with room to spare, while an inner one runs to the next
%%     system's staff, 12.000000 away.
%%     3.737890 IS that alignment minimum, and it checks out: 2.050000 (the staff's bottom
%%     LINE plus half its thickness — the notes are up at g''/a'' and never enter it) +
%%     1.187880 (the x-height of "no", the VAG's own printed up-extent) + 0.500000 (the
%%     spec's padding) = 3.737880.
%%
%%     THE REGIME IS CONFIRMED BY PERTURBATION, since the offsets alone could not say
%%     whether the inner chain was compressed onto its floors or stretched onto its ideals
%%     (2026-07-26, two throwaway books):
%%       - widen system-system-spacing 12 -> 20: the inner systems' staff->v1 goes
%%         3.737890 -> 5.500000. More room releases it, so it WAS compressed.
%%       - raise nonstaff-relatedstaff-spacing's basic-distance 5.5 -> 8 at the same room:
%%         the inner systems DO NOT MOVE (3.737890) while the last system on the page
%%         follows to 8.000001. Pinned on the minimum, and the override demonstrably took.
%%
%%     ★ AND THE SUM OF THE CHAIN'S MINIMUMS IS MEASURED, not inferred, by bisecting the
%%     room R (system-system-spacing's basic-distance) and watching where the first spring
%%     lifts off its floor — a fully compressed chain is exactly R <= sum(minimums):
%%
%%       R     | inner staff->v1
%%       12.0  | 3.737890   (on the floor)
%%       13.0  | 4.163732
%%       14.0  | 5.009886
%%       15.0  | 5.500000   (at the ideal)
%%       16.0  | 5.500000
%%
%%     Extrapolating the first segment back (slope 0.425842 per unit of R) hits 3.737890 at
%%     R = 12.000000. ⇒ SUM OF MINIMUMS = 12.000000 on this book, exactly the system gap:
%%     the chain is critically compressed, which is why the reading looks like a hard floor.
%%     ⚠️ The slope CHANGES between segments (0.425842, then 0.846154) — other springs are
%%     unblocking one by one as the force rises, which is Simple_spacer behaving normally
%%     and a reminder that this chain is not two springs.
%%
%%     ⚠️ WHAT IS STILL UNNAMED: adding the minimums by hand from the source gives
%%     3.737890 + 2.800000 + 0.0 (the null that breaks affinity, :928-933) +
%%     (padding 1 - min_offsets[0]) = 11.841556 when min_offsets[0] is read as the next
%%     system's staff rel, -4.303666. The measurement says 12.000000, so THAT SUBSTITUTION IS
%%     WRONG BY 0.158444 — the post-placement rel is not the minimum translation. The term to
%%     pin is min_offsets[0] itself.
%%     ⇒ Porting distribute_loose_lines needs that one number; everything else in the chain
%%     is now measured.
%%
%%     ★ THE "IT IS A CONSTANT" HYPOTHESIS WAS TESTED AND FAILED (2026-07-26). Verse 1's
%%     syllable was changed from "no" (x-height 1.187880) to "hi" (ascender 1.820098), which
%%     raises the first spring's floor by exactly 0.632218 and leaves everything else alone —
%%     verse 2 stays "no", so the second spring stays on its 2.8 minimum, measured:
%%
%%       book      gap     staff->v1   v1 up-ink   v1->v2
%%       no-12.0   12.0    3.737890    1.187880    2.800000   (floor)
%%       no-12.5   12.5    3.740655    1.187880    2.800000
%%       no-13.0   13.0    4.163732    1.187880    2.800000
%%       hi-12.0   12.0    4.370123    1.820098    2.800000   (floor)
%%       hi-12.5   12.5    4.370123    1.820098    2.800000   (floor)
%%       hi-13.0   13.0    4.370123    1.820098    2.800000   (floor)
%%       hi-13.5   13.5    4.586809    1.820098    2.800000
%%       hi-14.0   14.0    5.009886    1.820098    2.800000
%%
%%     If the unnamed term were constant, the release point would move with the floor:
%%     R*(no) = 12.000000 and a floor 0.632218 taller would give R*(hi) = 12.632218. But
%%     "hi" is still ON its floor at 13.000000 and only moves at 13.500000, so R* moved by
%%     MORE THAN 1.0 while the floor moved 0.632218. ⇒ NOT a constant, and not proportional
%%     to the ink either. The model "the first spring releases exactly at R = sum(minimums)"
%%     is what has to be wrong, since every other term was checked.
%%
%%     ★ ONE CLEAN INVARIANT CAME OUT OF IT AND IS WORTH KEEPING: OFF the floor, the reading
%%     does not depend on the ink at all — at R = 14.000000 both books read 5.009886, to six
%%     digits. So the syllable's ink sets the floor and nothing else in this chain; once the
%%     chain has room, the position is decided by the ideals and strengths alone.
%%     ★★ SOLVED, by reading Simple_spacer and Spring instead of measuring again
%%     (2026-07-26). THE RELEASE IS NOT AT FORCE -1. A spring sits at its minimum while
%%     f <= blocking_force, and blocking_force is
%%       (min_distance - ideal_distance) / inverse_compress_strength   (spring.cc:78-82)
%%     — where inverse_compress_strength came from set_default_strength as
%%     ideal - min AT THE TIME THE SPEC WAS READ (spring.cc:205-210), i.e. with the SPEC's
%%     minimum-distance, which nonstaff-relatedstaff-spacing does not have at all, so it is
%%     5.5 - 0 = 5.5. ensure_min_distance then raises min_distance to the ink floor and
%%     update_blocking_force runs, but THE STRENGTH IS NOT RECOMPUTED — the setters never
%%     recalculate it. So
%%       blocking_force = (floor - 5.5) / 5.5
%%     and f = -1 would be the release only if the strength were ideal - FLOOR, which is
%%     what I had assumed. That assumption is the whole of the 0.158444: it never existed as
%%     a term, it was the error in a release condition.
%%
%%     EVERY NUMBER IN THE TABLE FALLS OUT OF length(f) = max(floor, 5.5 + f * 5.5):
%%       blocking_force("no") = (3.737890 - 5.5)/5.5 = -0.320384
%%       blocking_force("hi") = (4.370123 - 5.5)/5.5 = -0.205432
%%       no-12.5 reads 3.740655 -> f = -0.319881, a hair ABOVE its blocking force: released.
%%       no-13.0 reads 4.163732 -> f = -0.242958.
%%       hi-13.0 is at the same R and therefore near the same f = -0.242958, which is BELOW
%%         hi's blocking force -0.205432 — so it is still blocked, and reads its floor. That
%%         is the refutation explained: the release point moves with (floor - ideal)/ideal,
%%         not with the floor.
%%       R=14.0 reads 5.009886 in BOTH books -> f = -0.089112. Off the floor the length is
%%         5.5 + f*5.5 with no floor term in it, which is why the ink drops out entirely.
%%
%%     ⇒ WHAT A PORT NEEDS: the compress strength of a loose-line spring is the SPEC's
%%     ideal - minimum-distance (0 when the spec has no minimum-distance), NOT ideal minus
%%     the alignment floor. ⚠️ This is the same fact the note on staff springs already
%%     records — the strengths are set once and ensure_min_distance does not touch them —
%%     applied to a different spring, and it was in the project's own memory before this
%%     probe was built.
\book {
  \probeTag "LYRV"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g''4 a'' g'' a'' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
    >>
  }
}

%% LYRRV — LYRV UNASSOCIATED, and it is LYRR's question asked in the regime where the answer
%%     can be seen. LYRR already put the two spellings side by side and LilyPond read them
%%     identically, but it did so on the ONE-LINE book, where the loose chain never binds:
%%     a single loose line's far-side springs carry LARGE_STRETCH/HUGE_STRETCH
%%     (:1257-1338), so the row sits at its ideal whatever the page does. LYRV's regime is
%%     the opposite one and it is measured — the chain there is CRITICALLY compressed
%%     (sum of minimums = the 12.000000 gap, bisected in LYRV's header above), so every
%%     term of it is load bearing. This book is LYRV with `\lyricsto "mel"` struck from both
%%     Lyrics contexts and nothing else touched.
%%
%%     ⚠️ WHY IT EXISTS, and it is not for LilyPond's sake. Lily# has TWO MODELS for a lyric
%%     line and LilyPond has one. `staff mel with lyrics words` is note-bound and reaches the
%%     loose chain; a bare `lyrics words` ROW is laid out as a staff-like BAND
%%     (MultiStaffLayouter's text row, LyricEngraver.LyricRowBaseline) and reaches nothing —
%%     it has no ink in any skyline, so no spring is floored by it and no chain contains it.
%%     HANDOFF 1 names that as the island; this pair is the instrument for it.
%%
%%     ⚠️ THE TWO SIDES SPELL "TWO VERSES" DIFFERENTLY AND THAT IS THE MEASUREMENT, not a
%%     flaw in the pair (HANDOFF 5.0, trap 5/6: check that both sides are the same MUSIC).
%%     They are: 480 syllables `no` over the same 120 bars of g''/a'', on the same paper,
%%     standing on the same columns. What differs is the CONTAINER — LilyPond has no
%%     one-context-two-verses spelling, so its second verse is a second Lyrics context, while
%%     Lily#'s row auto-wraps a long block into stacked verses inside ONE band. Comparing the
%%     containers is the point: the note-bound side is a chain and the row side is a band.
%%
%%     PREDICTION, written before running (HANDOFF 5.0-2): every figure this probe prints for
%%     LYRRV equals LYRV's, digit for digit —
%%       (a) staff/loose -> next loose = the set {3.737890, 2.800000, 5.500001}, INCLUDING
%%           the 5.500001 that only the last system on the page reaches;
%%       (b) system-to-system and staff-to-staff gap = 12.000000, unwidened by the lyrics;
%%       (c) 4 systems on page 1.
%%     Because a Lyrics context is a Lyrics context: engraver-init.ly:648-658 gives it
%%     staff-affinity UP and its nonstaff-* specs without ever asking whether a Voice was
%%     named, and `\lyricsto` decides which COLUMN a syllable stands on, not which spring
%%     holds the line.
%%
%%     ★ WRITTEN AS A FORK (HANDOFF 5.0), so the reading selects the next piece of work
%%     rather than merely scoring:
%%       - IDENTITY  => LilyPond's side of the comparison is a constant, so every difference
%%         Lily# shows between its two spellings is Lily#'s own, and the port has a target
%%         that carries no font quantity and no LilyPond uncertainty: give the ROW its ink.
%%       - DIFFERENT => association reaches the vertical spacing after all, LYRR's identity
%%         was a regime artefact rather than a fact about Lyrics contexts, and the row model
%%         cannot be measured against the note-bound one at all — the island then needs a
%%         different instrument before any porting, and LYRR's own conclusion has to be
%%         re-opened.
%%     ⚠️ The falsifier is real rather than decorative: LYROS is the book in this very file
%%     where an "obviously identical" addition turned out to change WHAT WAS BEING MEASURED
%%     (staff-refpoint-extent spans spaceable lines only, so adding a spaceable one moved the
%%     quantity's meaning). "It is a Lyrics context either way" is the same kind of claim.
%%
%%     ★ MEASURED 2026-07-27 — THE IDENTITY HOLDS IN THE STRONGEST FORM AVAILABLE: this
%%     probe's LYRV and LYRRV dumps are LINE FOR LINE IDENTICAL, all 59 of them, compared
%%     mechanically rather than by eye. Same 5 pages with the same 4/4/4/4/2 systems, the same
%%     first and last staff refpoints per page, the same {3.737890, 2.800000, 5.500001}, the
%%     same 12.000000 gap. ⚠️ Compared as WHOLE DUMPS on purpose: predictions (a)(b)(c) name
%%     three figures, and checking only those three would not have noticed the association
%%     moving a fourth.
%%
%%     ⇒ THE FORK TOOK THE FIRST BRANCH. LilyPond's side is a constant here, so the whole of
%%     any difference Lily# shows between `with lyrics words` and `lyrics words` is Lily#'s
%%     own, in a regime where every term of the chain is load bearing. What Lily# shows is in
%%     the ledger as lyrics.row.two-verse.*; the short version is that the ROW's verse step is
%%     3.200000 against this 2.800000 and its system gap is 12.000000 — exact — while the
%%     second verse is drawn 0.800000 BELOW the next system's staff refpoint. The gap being
%%     right is not the layout being right.
\book {
  \probeTag "LYRRV"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g''4 a'' g'' a'' } } }
      \new Lyrics \lyricmode { \repeat unfold 480 { no4 } }
      \new Lyrics \lyricmode { \repeat unfold 480 { no4 } }
    >>
  }
}

%% LYRR — THE SAME LINE, UNASSOCIATED. Identical to LYRC except that the Lyrics context is
%%     not \lyricsto anything: its syllables carry their own durations instead of being
%%     handed to a Voice's note columns. In LilyPond that changes WHICH COLUMN a syllable
%%     stands on and nothing else — a Lyrics context is a Lyrics context, so
%%     engraver-init.ly gives it the same staff-affinity UP and the same
%%     nonstaff-relatedstaff-spacing either way.
%%
%%     ⚠️ WHICH IS WHY THIS BOOK EXISTS, and it is not for LilyPond's sake: Lily# has TWO
%%     models here. `staff mel with lyrics words` is note-bound, and a bare `lyrics words`
%%     row is laid out as a staff-like BAND with its own baseline constant
%%     (LyricEngraver.LyricRowBaseline, MultiStaffLayouter's text-row band). LilyPond has one
%%     model for both, so this pair is the strongest kind (HANDOFF 5.0): the LilyPond side is
%%     an IDENTITY, and whatever Lily# reads differently between its two spellings is,
%%     by construction, entirely Lily#'s.
%%
%%     PREDICTION, written before running (HANDOFF 5.0-2): 5.500000, the same as LYRC, to
%%     six digits.
%%     (falsifier: a different number, which would mean the association DOES reach the
%%      vertical spacing and the two Lily# models might both be defensible.)
\book {
  \probeTag "LYRR"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g''4 a'' g'' a'' } } }
      \new Lyrics \lyricmode { \repeat unfold 480 { no4 } }
    >>
  }
}

%% BNL / BNH — WHERE A BAR NUMBER SITS, and the pair is built so that LilyPond's side is an
%%     IDENTITY (HANDOFF 5.0: the strongest shape). A BarNumber rides above the staff at the
%%     LINE START, left of the clef, and LilyPond places outside-staff grobs against an
%%     X-AWARE skyline (axis-group-interface.cc:359-474) — so the notes, which start after
%%     the clef, cannot reach it. Raising the melody by two octaves must therefore leave the
%%     number exactly where it was.
%%
%%     ⚠️ THE QUANTITY IS NOT DECORATIVE. This number is inside its staff's VerticalAxisGroup
%%     skyline, so it IS `min_offsets[0]` — the ink a system reserves ABOVE its own reference
%%     point (align-interface.cc:215-220, the j = 0 branch). That term closes the loose-line
%%     chain of the system before it (page-layout-problem.cc:931-932,
%%     `elements_[i].padding - min_offsets[0]`) and floors the system-to-system spring
%%     (:625-629), so a bar number placed too high moves BOTH — which is how this pair came
%%     to be written: Lily#'s two-verse lyric block would not compress into the 12.000000
%%     LilyPond keeps, and the excess was not the lyrics.
%%
%%     The dumped GROB rel is the number's BASELINE about the system's reference point, and
%%     the VAG rel on the same system is the staff's, so the measured quantity is their
%%     difference: staff refpoint UP to bar-number baseline.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       BNL and BNH agree to six digits, at about 3.05 — the staff's own half plus its line
%%       thickness (2.050000) plus BarNumber's padding 1.0, with the number's own descent the
%%       only other term.
%%     (falsifier: BNH reading higher than BNL, which would mean the placement is NOT
%%      X-aware and the whole diagnosis of the lyric island's residual is wrong.)
%%
%%     MEASURED: both books print the IDENTICAL set 3.074440 / 3.050000 / 3.076208 — the
%%     variation is which digits the number contains and nothing else (LilyPond puts the
%%     number's INK BOTTOM 1.0 above the staff's 2.050000, so the baseline sits at 3.050000
%%     plus that digit's own bottom overshoot). BNH's first ink starts 1.200000 higher up
%%     the page and the number did not move by a bit. Prediction held.
%%
%%     ★ AND THE LILY# SIDE REFUTED THE MECHANISM THE PAIR WAS BUILT TO CATCH: Lily# reads
%%     4.260000 on BOTH books, so its bar number is not riding above the notes either. The
%%     divergence is a flat OFFSET of 1.185560, not a missing X-awareness — which makes the
%%     fix a formula rather than a rework of the above-staff stacker. The pair still did its
%%     job: without BNH the constant would have been indistinguishable from "clears whatever
%%     is tallest", and the two hypotheses call for very different repairs.
%%
%%     ⚠️ Bar 1 carries no number (barNumberVisibility's default), so the reading must be
%%     taken from a CONTINUATION system, not from system 0.
\book {
  \probeTag "BNL"
  \paper { ragged-bottom = ##t }
  \score { \new Staff { \repeat unfold 48 { a'4 b' a' b' } } }
}

\book {
  \probeTag "BNH"
  \paper { ragged-bottom = ##t }
  \score { \new Staff { \repeat unfold 48 { a'''4 b''' a''' b''' } } }
}


%% LYRM — THE SAME LYRIC LINE UNDER A TWO-STAFF SYSTEM, and the pair is again built so that
%%     LilyPond's side is an IDENTITY (HANDOFF 5.0). A Lyrics line has staff-affinity UP, so
%%     nonstaff-relatedstaff-spacing runs from the staff DIRECTLY ABOVE it — the system's
%%     LAST spaceable staff (page-layout-problem.cc:943-944 records exactly that as
%%     last_spaceable_line). Adding a staff ABOVE that one must therefore leave the
%%     staff-to-lyric distance untouched: it is the same spring, between the same two
%%     VerticalAxisGroups, on the same music.
%%
%%     ⚠️ WHY THIS IS WORTH A BOOK. Lily# does not anchor a note-bound block on the last
%%     staff at all — it puts it `staffBottom` below the SYSTEM ORIGIN (the TOP staff's top
%%     line) and lets the skyline drop push it clear of whatever is under it. On a one-staff
%%     system those are the same place, which is why lyrics.natural.staff-to-lyric is exact
%%     and has been for sessions. On a two-staff system they are a whole staff apart, and the
%%     basic-distance 5.5 stops binding entirely — only the ink minimum is left. That is the
%%     reason distribute_loose_lines was ported for SINGLE-staff systems only (HANDOFF 1):
%%     solving a chain whose anchor is a staff away from LilyPond's would move the lines by
%%     that error rather than fix them.
%%
%%     The bottom staff's melody stays INSIDE the staff so its own ink cannot bind: the
%%     alignment minimum is 2.050000 + the syllable's x-height 1.187880 + padding 0.500000 =
%%     3.737890, comfortably under 5.5 (HANDOFF 5.0: do not sit the measurement on a floor).
%%     The top staff carries LYRC's high melody so the SYSTEM's up-ink is unchanged from it.
%%
%%     PREDICTION, written before running (HANDOFF 5.0-2): 5.500001 — the same reading as
%%     LYRC to six digits, sliver and all, because it is the same spring in the same chain
%%     and the extra staff joins the PAGE's chain, not this one.
%%     (falsifier: anything else, and in particular a number near 17.5 — that would mean
%%      LilyPond measures the line from the system's TOP staff, which would make Lily#'s
%%      origin anchor defensible and this whole island wrong.)
\book {
  \probeTag "LYRM"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \repeat unfold 120 { g''4 a'' g'' a'' } }
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
    >>
  }
}

%% LYRMV — LYRV'S LOOSE CHAIN WITH A STAFF ADDED ABOVE IT, which is the book that says
%%     whether the room a two-verse block is squeezed into depends on how many staves the
%%     system has. LYRM answered the ANCHOR question (which staff the block hangs from) and
%%     is now exact; this one asks the question LYRM could not, because one loose line has
%%     nothing to be squeezed against — LYRC and LYRV differ by exactly that.
%%
%%     ★ LILYPOND'S SIDE IS AN IDENTITY WITH LYRV, and it is an identity in the source
%%     rather than by observation. distribute_loose_lines is handed
%%     `last_spaceable_line_translation` and `-solution_[spring_idx]`
%%     (page-layout-problem.cc:936-939): the PREVIOUS spaceable staff's position on the page
%%     and THIS one's. Both are refpoints in `solution_`, the page's own spring chain, and
%%     neither end knows or cares which system it belongs to — the same call site serves a
%%     block between two systems and a block between two staves of one system, and the only
%%     thing that changes is which minimum closes it (:923-933). So adding a staff ABOVE the
%%     lyric-bearing one adds a spring to the PAGE's chain, between two positions the loose
%%     chain never reads.
%%
%%     ⚠️ WHY IT IS WORTH A BOOK ANYWAY, and it is not LilyPond's behaviour that is in doubt.
%%     Lily#'s `LayoutEngine.BuildLooseChainEnds` computes the room as
%%     `onPage[i].Y - onPage[i+1].Y` — the gap between two system ORIGINS — and RETURNS NULL
%%     FOR THE WHOLE SCORE as soon as any system holds more than one staff. On this book
%%     every chain therefore runs at force 0, i.e. every spring at max(min, ideal), which is
%%     where LilyPond's chain lands only when it has room to spare. LYRV is the proof that it
%%     does not: the same two verses solve at f = -0.841556 and the first spring gives way
%%     from its ideal 5.500000 to its ink floor 3.737890.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2). All three are identities, so all
%%     three are quoted from books already measured rather than derived afresh:
%%       (a) system-to-system (last staff -> next first) = 12.000000, LYRV's and LYRM's
%%           reading. LilyPond does not widen a system gap for loose lines, and the added
%%           staff is not in the loose chain.
%%       (b) staff/loose -> next loose = the set {3.737890, 2.800000, 5.500001}, LYRV's
%%           readings digit for digit: the inner systems' chains compressed onto the first
%%           spring's ink floor, the last one on the page running to -page_height_ with room
%%           to spare and reaching the basic-distance.
%%       (c) staff-to-staff INSIDE a system = 9.000000, LYRM's reading — nothing was added
%%           between those two staves, and Align_interface is the owner of that distance.
%%     (falsifier for (a): a gap near 13.762110 — 12.000000 plus the 1.762110 by which a
%%      force-0 chain is longer than this one's compressed length — which would mean the
%%      system spring DOES take the block into account once the block hangs from a system's
%%      LAST staff rather than from its only staff. That would make Lily#'s force-0 placement
%%      right for the wrong reason and the port below wrong to reuse LYRV's room.)
%%     (falsifier for (c): a number above 9.000000, which would mean the block's ink is
%%      inside the two staves' own spring and the chain is not what places it at all.)
%%
%%     ALL THREE HELD, to six digits, on pages 1, 2 and 3 alike: 12.000000, the set
%%     {3.737890, 2.800000, 5.500001}, and 9.000000, with four systems (eight staves) to a
%%     page. The identity is exact — this book's dump differs from LYRV's only by the second
%%     staff's own lines.
%%
%%     ★ AND THE HOLE HAD SOMETHING ELSE IN IT (HANDOFF 5.0). The Lily# side reads 10.500000
%%     for (c) — staffgroup-staff-spacing's basic-distance — because it models each bare
%%     `staff` declaration as its own group, where LilyPond, finding no staff-grouper at all,
%%     falls through to the staff's own default-staff-staff-spacing
%%     (axis-group-interface.cc:1008-1027, basic-distance 9). It reads the SAME 10.500000 on
%%     LYRM, so the lyrics are not in it; the two books together are what say so. Font-free
%%     on both sides, and nothing in the corpus had read the inside gap of an UNGROUPED pair
%%     before — page.natural.staff-staff-inside measures a PianoStaff. Carried as
%%     lyrics.two-staff{,.two-verse}.staff-staff-inside.
%%
%%     ⚠️ AND THE LILY#-SIDE PREDICTION FOR (a) WAS WRONG BY THE WHOLE AMOUNT, which is the
%%     more useful half: the gap was expected near 13.762110 because a force-0 chain is
%%     longer, and it reads 12.157200 — lyrics.two-verse.system-gap's residual exactly. The
%%     system gap is set by the RESERVATION, not by the solve, and those were decoupled one
%%     commit earlier. The reading that does see the missing solve is the staff-to-verse-1
%%     distance, opened afterwards as lyrics.two-staff.two-verse.staff-to-lyric.
%%
%%     ⚠️ The bottom staff carries LYRM's melody (g'/a', inside the staff) so its own ink
%%     cannot bind the first spring, and the top staff carries LYRC's high melody so the
%%     system's up-ink is unchanged from every book above. Both syllables are "no" for the
%%     reason LYRV's header gives at length: an ascender or a descender would put the reading
%%     on a font metric and it would stop being a spec measurement.
\book {
  \probeTag "LYRMV"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \repeat unfold 120 { g''4 a'' g'' a'' } }
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
    >>
  }
}

%% LYRHK — LYRMV WITH THE UPPER STAFF TAKEN AWAY UNDER ONE SYSTEM, which is the book that
%%     asks whether the room a loose chain is solved into belongs to the SYSTEM it hangs
%%     under or to the SCORE. Every book above holds its staff count constant down the
%%     page, so none of them can tell the two apart: read the span off system 0 or off the
%%     system the block actually belongs to and you get the same number either way.
%%
%%     ★ LILYPOND'S SIDE IS AN IDENTITY WITH LYRMV, and it is an identity in the SOURCE.
%%     distribute_loose_lines is handed `last_spaceable_line_translation` and
%%     `-solution_[spring_idx]` (page-layout-problem.cc:936-939) — the previous spaceable
%%     staff's position on the page and this one's, both members of the PAGE's spring
%%     chain. A staff that hara-kiri removed is not in that chain, and a staff that
%%     survives is in it whether or not its NEIGHBOUR system kept one; so how many staves
%%     any system carries cannot reach either end of the block's room.
%%
%%     ⚠️ WHY IT IS WORTH A BOOK, and it is Lily# that is in question rather than LilyPond.
%%     `LayoutEngine.BuildLooseChainEnds` takes the origin-to-last-spaceable-staff span
%%     PER SYSTEM (commit c64ee958) rather than off `systemsArray[0]` the way the sibling
%%     term was written, and the commit reported that NOTHING IN THE CORPUS OR THE FIXTURES
%%     REACHES THE DIFFERENCE: hara-kiri.lys, ossia.lys and dashed-barline.lys carry no
%%     lyrics, and every lyric book here is uniform down the page. A per-system read and a
%%     score-wide one agree on all of them. This is the book where they cannot agree.
%%
%%     THE SHAPE, and it is forced with explicit \break rather than left to the line
%%     breaker (HANDOFF 5.0: the two sides of a pair must be the same music, and "the
%%     upper staff rests through exactly system 0" is otherwise a bet on both engines
%%     choosing the same bar). Three bars to a system, twenty systems, four to a page:
%%       system 0 — upper staff silent, REMOVED, one staff
%%       systems 1..3 — upper staff playing, two staves
%%     so page 1 carries 7 staff refpoints, and the block under system 1 is the reading
%%     that separates the two implementations. Its anchor is that system's LAST staff,
%%     nine staff spaces below its origin; a score-wide span would take system 0's zero
%%     instead and hand the chain nine staff spaces of room it does not have.
%%
%%     ⚠️ THREE BARS AND NOT SIX, AND THE REASON IS A MEASUREMENT, not a preference: THE
%%     TWO ENGINES DO NOT FIT THE SAME NUMBER OF BARS ON A LYRIC LINE. Written first with
%%     six, the \break was honoured by LilyPond (whose lines hold about 6.7 bars of this
%%     music) but SUBDIVIDED by Lily#, which fits about three and split every group in
%%     two; its page 1 came out 1+1+2+2 = 6 staves against LilyPond's 1+2+2+2 = 7, and the
%%     two sides stopped being the same music. At three bars both engines take the break
%%     as given and both pages read 1+2+2+2. ⚠️ The underlying width difference is NOT a
%%     finding of this book and is not carried as an entry — it is the ~27% wider lyric
%%     face (HANDOFF 5.3) widening every column that a syllable binds — but it is worth
%%     knowing that no ledger point sees it, because every lyric book here measures the
%%     page and none measures how much music reached a line.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2). All of them are identities, so
%%     all of them are quoted from books already measured rather than derived afresh:
%%       (a) staff/loose -> next loose = the set {3.737890, 2.800000, 5.500001}, LYRMV's
%%           and LYRV's readings digit for digit — INCLUDING under system 0, whose staff
%%           count differs from its neighbours'. 3.737890 is the alignment floor the first
%%           spring gives way to when the chain is critically compressed, 2.800000 the
%%           verse step's minimum-distance, 5.500001 the last chain on the page running to
%%           -page_height_ with room to spare and reaching its basic-distance.
%%       (b) staff-to-staff INSIDE a system = 9.000000, from the three two-staff systems;
%%           default-staff-staff-spacing, since neither staff is in a staff-grouper
%%           (axis-group-interface.cc:1008-1027).
%%       (c) system-to-system (last staff -> next first) = 12.000000, LYRMV's reading. The
%%           gap out of a one-staff system and the gap out of a two-staff one are the same
%%           spring on the same spec.
%%       (d) page 1: 4 systems, 7 staff refpoints — the count that says the removal
%%           happened at all, and happened once (HANDOFF 5.0 trap 8).
%%     (falsifier for (a): any reading other than 3.737890 under system 0 or under system
%%      1, and in particular 5.500000 — the basic-distance, i.e. a chain with room to
%%      spare. That would mean the room a block is solved into DOES move with the staff
%%      count of the system it hangs under or of the one below it, which would make a
%%      score-wide span defensible and c64ee958 wrong to have gone per-system.)
%%     (falsifier for (d): 8 staves, which would mean the upper staff was not removed at
%%      all and this book is a slower spelling of LYRMV.)
%%
%%     ALL FOUR HELD, to six digits, on pages 1, 2 and 3 alike: {3.737890, 2.800000,
%%     5.500001}, 9.000000, 12.000000, and a page 1 of 12.000000 + 21.000000 + 21.000000
%%     ending on a last refpoint of 74.690551 — one staff, then three pairs. Note that the
%%     loose set has no FOURTH member: if the one-staff system's chain had been solved into
%%     different room than its two-staff neighbours', a second first-spring value would
%%     stand next to the 3.737890, and none does. The identity is exact.
%%
%%     ★ AND THE HOLE HAD SOMETHING ELSE IN IT (HANDOFF 5.0), which is neither of the two
%%     things this book was built to look at. Lily# reads 4.009200 under system 0 and
%%     5.509200 under systems 1..3 — so the per-system span c64ee958 introduced is fine and
%%     the room really is 12.157200 on both — but the DIFFERENCE, exactly +1.500000, comes
%%     from `removeEmpty` being DECLARED at all. Rendering this music with the declaration
%%     present and no staff ever empty reproduces 5.509200 on every system; dropping the
%%     declaration restores 4.009200. LayoutEngine.cs:198-202 takes a separate per-system
%%     height branch under hara-kiri and spells the inter-group distance as the literal
%%     `StaffSpacing.StaffGroupStaff.BasicDistance` (10.5) where MultiStaffLayouter's
%%     SelectInterGroupSpec has answered DefaultStaffStaff (9) since a666b476 — the same
%%     1.500000 that port closed, surviving in a THIRD copy of the selection that the port
%%     did not reach (HANDOFF 5.2.1 (2); MultiStaffLayouter's own remark says the choice has
%%     "one home ... because it is now read twice"). The height feeds the system's DOWN
%%     skyline, which is what the chain's first spring measures its ink floor against, so
%%     the block moves while the staves — placed elsewhere — do not: this book reads
%%     9.000000 inside a system on the very page whose lyrics are 1.5 low.
%%     ⚠️ Carried as lyrics.hara-kiri.{hidden,shown}-system.staff-to-lyric, and THE
%%     MEASUREMENT IS THE DIFFERENCE OF THE TWO: both are the same spring on the same face
%%     in one book, so the ~27% lyric-face difference cancels and 1.771310 - 0.271310 =
%%     1.500000 is font-free mechanism. Neither entry may be driven to zero on its own.
%%
%%     ⚠️ The melodies are LYRMV's unchanged — the bottom staff inside the staff (g'/a') so
%%     its own ink cannot bind the first spring, the top staff high (g''/a'') so the
%%     system's up-ink is what every book above reserves — and both syllables are "no" for
%%     the reason LYRV's header gives at length: an ascender or a descender would put the
%%     reading on a font metric and it would stop being a spec measurement.
%%
%%     ⚠️ THE SILENT BARS ARE `r1` AND NOT `R1`, which is not a spelling preference. An R1
%%     is a MULTI-MEASURE REST, and how many bars one of them swallows is a question the
%%     two engines answer differently — LilyPond prints six separate ones without
%%     \compressMMRests, and Lily#'s MMR runs are a model of their own. Neither answer
%%     changes what this book measures, because the staff is removed either way; but the
%%     pair would then differ in something other than the quantity under test, which is
%%     how a probe stops measuring what its header says (HANDOFF 5.0). A plain whole rest
%%     is one bar in both, and it keeps no staff alive in either: keepAliveInterfaces asks
%%     for note-head-interface and a Rest does not implement it.
\book {
  \probeTag "LYRHK"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff \with { \RemoveAllEmptyStaves } {
        \repeat unfold 3 { r1 } \break
        \repeat unfold 18 { \repeat unfold 3 { g''4 a'' g'' a'' } \break }
        \repeat unfold 3 { g''4 a'' g'' a'' }
      }
      \new Staff { \new Voice = "mel" { \repeat unfold 60 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 240 { no } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 240 { no } }
    >>
  }
}

%% LYRHKG — HARA-KIRI INSIDE A GROUPER, which is the book that stops the defect LYRHK found
%%     from being closed with a literal. LYRHK's staves are both bare, so a fix that writes
%%     9 where the code writes 10.5 passes it; here BOTH numbers must appear at once, and
%%     which one appears where depends on WHICH STAVES ARE STILL ALIVE.
%%
%%     A GrandStaff holding A (always playing) and B (\RemoveAllEmptyStaves, silent through
%%     system 0), with a bare staff C carrying the melody and two verses beneath them.
%%     get_spacing_spec reads the property off the staff ABOVE the gap
%%     (page-layout-problem.cc:1280-1281) and calc_maybe_pure_staff_staff_spacing then asks
%%     whether that staff still has a LIVE spaceable member below it inside its grouper
%%     (axis-group-interface.cc:1008-1027, Staff_grouper_interface::maybe_pure_within_group).
%%     So killing B does not merely delete a gap — it PROMOTES A to last live member of the
%%     grouper and changes the spec of the gap that remains.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       (a) system 0, where B is gone: A -> C = 10.500000, staffgroup-staff-spacing. A is
%%           the grouper's only live member, hence its last.
%%       (b) systems 1..3, where B is alive: A -> B = 9.000000 (staff-staff-spacing, a live
%%           member still below inside the group) and B -> C = 10.500000
%%           (staffgroup-staff-spacing, B is now the last live member).
%%       (c) system-to-system = 12.000000 and the loose set {3.737890, 2.800000, 5.500001},
%%           both unchanged from LYRHK — the block still hangs from the system's last
%%           spaceable staff and is still solved into the same refpoint-to-refpoint room.
%%       (d) page 1: 4 systems, 2 + 3 + 3 + 3 = 11 staff refpoints.
%%     (falsifier for (a), and it is the one that matters: 9.000000 there would mean
%%      maybe_pure_within_group counts DECLARED members rather than LIVE ones. The whole
%%      reading this probe rests on — that LilyPond has no hara-kiri branch, only a filter
%%      that removes dead groups before the ordinary spacing runs — would then be wrong,
%%      and so would the port planned on top of it.)
%%
%%     ALL FOUR HELD. Inside-system spread 10.500000 on system 0 and 19.500000 =
%%     9.000000 + 10.500000 on the rest; system-to-system 12.000000; loose
%%     {3.737890, 2.800000, 5.500000}; page-1 first-staff gaps 22.500000 and 31.500000,
%%     i.e. 2 + 3 + 3 + 3 = 11 staves. ★ SO LIVENESS IS WHAT DECIDES THE SPEC, and that is
%%     the direct evidence for the porting principle this island rests on.
%%     ⚠️ Lily# is EXACT on all three shape readings today (10.500000 / 9.000000 / 11) and
%%     its lyric sits on the font floor — the hara-kiri height branch happens to be RIGHT
%%     here, because the gap it hardcodes at 10.5 really is 10.5 between a grand staff and
%%     a bare staff. **That is why the book is worth keeping**: patch that literal to 9 to
%%     close LYRHK and these entries go 1.500000 wrong the other way.
\book {
  \probeTag "LYRHKG"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new GrandStaff <<
        \new Staff {
          \repeat unfold 3 { g''4 a'' g'' a'' } \break
          \repeat unfold 18 { \repeat unfold 3 { g''4 a'' g'' a'' } \break }
          \repeat unfold 3 { g''4 a'' g'' a'' }
        }
        \new Staff \with { \RemoveAllEmptyStaves } {
          \repeat unfold 3 { r1 }
          \repeat unfold 57 { g'4 a' g' a' }
        }
      >>
      \new Staff { \new Voice = "mel" { \repeat unfold 60 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 240 { no } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 240 { no } }
    >>
  }
}

%% LYRHKD / LYRHKN — THE DECLARATION ON ITS OWN, and LILYPOND'S SIDE IS AN IDENTITY BY
%%     CONSTRUCTION rather than by measurement. The two books are the same music on the same
%%     paper; the only difference is that LYRHKD's upper staff carries
%%     \with { \RemoveAllEmptyStaves } and LYRHKN's does not. NO STAFF IS EVER EMPTY in
%%     either, so the declaration cannot fire, and LilyPond's hara-kiri is a suicide followed
%%     by a live-filter (page-layout-problem.cc:1366-1370, align-interface.cc:90) — a grob
%%     that never dies leaves no trace of the interface at all. Every reading must match to
%%     the last digit. ⇒ Whatever Lily# reads differently between them is ENTIRELY its own,
%%     and needs no force arithmetic to interpret.
%%
%%     ⚠️ WHY THE PAIR IS NEEDED AND LYRHK IS NOT ENOUGH. Lily# branches on the DECLARATION
%%     (LayoutEngine `hasHaraKiri`, six sites), not on anything having been hidden, and two
%%     of those sites select a different formula: the per-system height (:198-202) and the
%%     page's staff springs, which under hara-kiri are emptied and rebuilt per system
%%     WITHOUT SKYLINES (:128-131). LYRHK sees the first because a skyline feeds the lyric
%%     chain. It cannot see the second: a spring's MINIMUM comes from those skylines, and a
%%     minimum only binds when the page is compressed — every hara-kiri book to date is
%%     ragged. Hence the justified paper below.
%%
%%     THE REGIME IS THE POINT (HANDOFF 5.0 trap 7): ragged-bottom would stop the stretch but
%%     not the compression, so the paper is LilyPond's justified default and the systems are
%%     packed 8 to a page the way book JSK packs them. ⚠️ Confirm from the dump which regime
%%     it landed in before reading anything else — the inside distance must come out BELOW
%%     the ideal 9.000000 for the minima to be binding at all. If it does not, raise
%%     max-systems-per-page rather than trusting the numbers.
%%
%%     PREDICTIONS: every reading of LYRHKD equals the same reading of LYRHKN, digit for
%%     digit, on every page.
%%     (falsifier: any difference whatever, which would mean LilyPond does react to the bare
%%      declaration and the invariant Lily# is about to be held to is not LilyPond's.)
%%
%%     HELD, TO THE LAST DIGIT: both books print 8 systems, inside 8.429724, system-to-system
%%     11.119934, first-staff gap 19.549658, loose {3.737890, 2.800000}. The regime is
%%     CONFIRMED COMPRESSED — 8.429724 is below the ideal 9.000000 (trap 7).
%%     ⇒ THE INVARIANT IS LILYPOND'S: declaring removeEmpty where nothing is empty changes
%%     nothing. Lily# breaks it — inside 9.647977 declared against 9.166134 undeclared,
%%     staff-to-lyric 5.500000 against 4.194860 — because it branches on the DECLARATION.
%%
%%     ⚠️ ONLY THE COUNTS ARE CARRIED AS ENTRIES, and that is itself a finding: LilyPond
%%     fits 8 systems here and Lily# fits 6 declared / 7 undeclared, so it never reaches the
%%     compressed regime at all and STRETCHES instead. A gap on a 6-system stretched page is
%%     not the same quantity as the same-named gap on an 8-system compressed one, so the
%%     distance entries would have been measuring the page count. They wait for a paper both
%%     engines fill alike. ★ Note the two Lily# counts DIFFER (12 against 14): the
%%     declaration costs a whole system off page 1, so the invariant is broken in the page
%%     BREAKER as well as in the spacing — the fix cannot be local to the lyric chain.
%%     ⚠️ The invariant itself belongs in a TEST, not here (HANDOFF 4). These two entries
%%     record only that LilyPond's side of it is real.
\book {
  \probeTag "LYRHKD"
  \paper { max-systems-per-page = #8 }
  \score {
    <<
      \new Staff \with { \RemoveAllEmptyStaves } {
        \repeat unfold 3 { g''4 a'' g'' a'' } \break
        \repeat unfold 18 { \repeat unfold 3 { g''4 a'' g'' a'' } \break }
        \repeat unfold 3 { g''4 a'' g'' a'' }
      }
      \new Staff { \new Voice = "mel" { \repeat unfold 60 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 240 { no } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 240 { no } }
    >>
  }
}

\book {
  \probeTag "LYRHKN"
  \paper { max-systems-per-page = #8 }
  \score {
    <<
      \new Staff {
        \repeat unfold 3 { g''4 a'' g'' a'' } \break
        \repeat unfold 18 { \repeat unfold 3 { g''4 a'' g'' a'' } \break }
        \repeat unfold 3 { g''4 a'' g'' a'' }
      }
      \new Staff { \new Voice = "mel" { \repeat unfold 60 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 240 { no } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 240 { no } }
    >>
  }
}

%% HKW / HKWN — HARA-KIRI WHERE THE INK BETWEEN TWO SURVIVING STAVES BEATS THE SPEC. This is
%%     the regime commit 41f9749d moved a snapshot in and could NOT name an entry for:
%%     test/hara-kiri's system 1 went 9.000000 -> 9.500000 and the message justified it with
%%     the mechanism and a test instead of a ledger key (HANDOFF 5.2.1 ③ — when the key
%%     cannot be named, the point is opened FIRST). Nothing in the corpus reached it. Every
%%     hara-kiri book above (LYRHK, LYRHKG, LYRHKD/LYRHKN) keeps its staves' ink inside their
%%     own lines, so all of them sit on StaffGrouper's basic-distance and would still read
%%     9.000000 with the skylines unplugged altogether — which is exactly what the walk this
%%     corpus was blind to did.
%%
%%     THE SHAPE IS BOOK P's ARITHMETIC UNDER A HARA-KIRI DECLARATION. `d` in the treble
%%     staff hangs 6 staff spaces below its middle line and its head reaches 0.545 further,
%%     while the same written pitch is the bass staff's own middle line, so nothing there
%%     rises above its top line: 6.545 + 2.05 + 1 = 9.595 beats basic-distance 9
%%     (align-interface.cc:228-238, define-grobs.scm:3352-3355). WHOLE NOTES, so no stem
%%     enters the gap (lily/stem.cc, Stem::is_normal_stem — duration-log >= 1) and the ink
%%     that binds is the notehead and the staff line, both of which the corpus already agrees
%%     on to six digits: staff.staff.{upper-note-to-lower-lines,lower-note-to-upper-lines}
%%     are 9.595000 exact. ⇒ the new points inherit a KNOWN value, so a divergence here can
%%     only be about hara-kiri and not about the ink.
%%
%%     ★ LILYPOND'S SIDE IS AN IDENTITY BETWEEN THE TWO BOOKS on the tall-ink systems, and it
%%     is an identity in the SOURCE rather than a measurement. Hara-kiri is a suicide
%%     followed by a live-filter (page-layout-problem.cc:1366-1370, align-interface.cc:90);
%%     the surviving system's Align_interface then runs the ordinary max() over the staves it
%%     still holds, and what some OTHER system did with its own staves reaches neither term.
%%     ⇒ whatever Lily# reads differently between HKW and HKWN is entirely its own, with no
%%     force arithmetic to interpret — ragged-bottom keeps a page force out of the number too.
%%
%%     ⚠️ AND THE CONTROL CARRIES ITS OWN REGIME ASSERTION (HANDOFF 5.0 trap 7 — "do not sit
%%     on the floor"). HKWN's system 0 keeps the silent staff, whose whole rest hangs from the
%%     fourth line and protrudes nowhere, so that ONE gap is spec-bound at 9.000000 while its
%%     neighbours are ink-bound at 9.595000. Both readings come out of one book, one paper and
%%     one solve, so a probe that has quietly stopped consulting the skyline cannot stay green
%%     — it would have to print 9.000000 twice.
%%
%%     THE SILENT BARS ARE `r1` AND NOT `R1`, for the reason LYRHK's header gives at length: a
%%     multi-measure rest is a quantity the two engines model differently, and the pair would
%%     then differ in something besides what is under test. Three bars to a system, twenty
%%     systems, four to a page, breaks explicit — the same shape as every hara-kiri book
%%     above, so nothing here rests on the two line breakers agreeing about how much music
%%     reaches a line.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       (a) HKW page 1 = 1 + 2 + 2 + 2 = 7 staff refpoints. Its system 0 has no inside gap
%%           at all; systems 1..3 read 9.595000.
%%       (b) HKWN page 1 = 8. Its system 0 reads 9.000000 and systems 1..3 read 9.595000 —
%%           the same 9.595000 as (a), digit for digit.
%%       (c) system-to-system is 12.000000 in both, unchanged from every ragged book above.
%%     (falsifier, and it is the one that matters: HKW's tall-ink gap differing from HKWN's in
%%      either direction. That would mean removing a staff from ONE system changes how the
%%      others are spaced, and the live-filter reading this whole island was ported on top of
%%      would be wrong.)
%%
%%     ALL THREE HELD once the grouper was right: HKW page 1 reads 12.000000 then 21.595000
%%     between its systems' first staves — a lone staff, then 9.595000 + 12.000000 twice —
%%     and HKWN reads 21.000000 then 21.595000 twice, i.e. 9.000000 and 9.595000 inside. The
%%     tall-ink gap is 9.595000 in BOTH books and matches P and Q to the last digit.
%%
%%     ★ AND THE POINT WAS MEASURED BACKWARDS THROUGH THE ISLAND before being recorded, which
%%     is what says it is a NET and not a reading that never moves (HANDOFF 5.0: exact can
%%     mean "does not move in this regime"). Lily#'s two tall-ink gaps, by commit:
%%
%%                                       b415dd16   41f9749d   29bde26d   HEAD
%%       HKW   (declared)                9.000000   9.595000   9.595000   9.595000
%%       HKWN  (control)                 9.000000   9.000000   9.595000   9.595000
%%       HKWN system 0 (spec-bound)      9.000000   9.000000   9.000000   9.000000
%%
%%     So the ink was ignored on BOTH paths for two different reasons, and the pair separates
%%     them: 41f9749d removed the fixed-gap walk the DECLARATION selected — that is the entry
%%     its snapshot rebase could not name — and 29bde26d stopped the surviving walk from
%%     building ONE skyline off system 0's music and handing it to every system, which is why
%%     the CONTROL was still 9.000000 in between (system 0 is the one whose upper staff rests,
%%     so the score-wide skyline was the 9.000000 one). ⚠️ That second commit had no ledger key
%%     either; its net was the test EachSystemIsSpacedByItsOwnInk, and this pair is now the
%%     corpus half of it. The spec-bound reading never moves across any of it, which is what
%%     rules out "both gaps sat on the floor together".
%%
%%     ★ ⚠️ AND (a) FAILED ON THE FIRST RUN FOR A REASON THAT IS ITSELF THE FINDING: written
%%     with \new PianoStaff (which is what books P/Q/D/TU/TD use), NOTHING WAS REMOVED —
%%     HKW and HKWN printed identical pages, 8 staves and a 9.000000 system-0 gap in both.
%%     LilyPond says why in one line of its own source: PianoStaff is "just like GrandStaff,
%%     but the staves are only removed together, never separately", because it
%%     \consists Keep_alive_together_engraver (ly/engraver-init.ly:535-544). The declaration
%%     was live and the sibling staff kept the resting one alive. ⇒ A HARA-KIRI BOOK MUST BE
%%     A GrandStaff, and that is also what makes this pair the same music as its Lily# twin:
%%     Lily#'s `grandStaff` removes members separately, which fixture test/hara-kiri relies
%%     on. The existing PianoStaff books are unaffected — no staff can die in any of them, and
%%     the two contexts differ in nothing else — but a probe that had been copied from them
%%     would have measured a hara-kiri regime it was never in, and both sides would have
%%     agreed (HANDOFF 5.0: exact can mean "does not move here").
\book {
  \probeTag "HKW"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    \new GrandStaff <<
      \new Staff \with { \RemoveAllEmptyStaves } {
        \clef treble
        \repeat unfold 3 { r1 } \break
        \repeat unfold 18 { \repeat unfold 3 { d1 } \break }
        \repeat unfold 3 { d1 }
      }
      \new Staff { \clef bass \repeat unfold 60 { d1 } }
    >>
  }
}

\book {
  \probeTag "HKWN"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    \new GrandStaff <<
      \new Staff {
        \clef treble
        \repeat unfold 3 { r1 } \break
        \repeat unfold 18 { \repeat unfold 3 { d1 } \break }
        \repeat unfold 3 { d1 }
      }
      \new Staff { \clef bass \repeat unfold 60 { d1 } }
    >>
  }
}

%% HKCD / HKCN — THE DECLARATION ON ITS OWN, ON A COMPRESSED PAGE AND WITHOUT LYRICS. This
%%     is JSK's book with \RemoveAllEmptyStaves added to the upper staff and NOTHING ELSE
%%     CHANGED — same music, same paper, no staff ever empty — so LilyPond's two readings are
%%     identical by construction (a suicide plus a live-filter leaves no trace of a grob that
%%     never dies: page-layout-problem.cc:1366-1370, align-interface.cc:90) and whatever Lily#
%%     reads differently between them is entirely its own.
%%
%%     ⚠️ WHY THIS IS NOT LYRHKD/LYRHKN AGAIN, which asked the same question and could only
%%     carry its COUNTS. Those books hang two verses under the lower staff, and the two
%%     engines then do not fit the same number of systems on the page (LilyPond 8, Lily# 7),
%%     so a gap on one page and the same-named gap on the other are not the same quantity.
%%     Strip the lyrics and the shape is JSK's, where both engines already agree exactly —
%%     16 staves to page 1, inside 8.651797 — so here the DISTANCES can be carried.
%%
%%     ⚠️ AND WHY THE INK IS DELIBERATELY LOW, unlike HKW above. A spring's MINIMUM is what
%%     compression drives it onto, and the minimum is the alignment distance; with HKW's tall
%%     ink the floor would be 9.595000 and a compressed page would sit on it and measure
%%     nothing (HANDOFF 5.0, "do not sit on the floor"). JSK's music leaves the alignment
%%     minimum at 7.545 (3.545 under the treble's middle line + 3.0 of bass up-stem + 1), well
%%     below the 8.651797 the page solves for, so the spring is genuinely between its floor
%%     and its ideal — which is the only regime in which the SPRING can be measured at all.
%%
%%     THE STAGE THIS IS THE NET FOR. b415dd16 gave the hara-kiri staff springs their
%%     skylines; before it, a declared score rebuilt those springs WITHOUT skylines, the floor
%%     fell back to the drawn distance and the page could not squeeze — measured at the time
%%     as 9.000000 declared against 8.651797 undeclared, on exactly this music. It was the one
%%     stage of the island with no ledger key at all; LYRHKD/LYRHKN were built to be that key
%%     and the page-count mismatch stopped them.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       (a) LilyPond's two books are identical to the last digit, and equal to JSK's ledger
%%           values: inside 8.651797, system-to-system 11.303595, 16 staves on page 1.
%%       (b) inside < 9.000000 on page 1, or the page did not compress and the pair measures
%%           nothing (trap 7).
%%     (falsifier: any difference between the two books on LilyPond's side, which would mean
%%      LilyPond reacts to the bare declaration and the invariant Lily# is held to is not
%%      LilyPond's.)
%%
%%     BOTH HELD, on all three pages and not just the first: 8.651797 / 11.303595 / 16 staves
%%     on page 1, 8.565484 / 11.130968 on page 2, 8.565484 on the last — the two books print
%%     the same numbers throughout, and 8.651797 is below the ideal 9.000000 so the regime is
%%     confirmed compressed.
%%
%%     ★ AND THE POINT WAS MEASURED BACKWARDS BEFORE BEING RECORDED, the same way HKW/HKWN
%%     were. Lily#, at b415dd16's PARENT (cf59a00d) against today:
%%
%%                                       cf59a00d    HEAD       LilyPond
%%       HKCD  inside (declared)         9.000000    8.651797   8.651797
%%       HKCN  inside (control)          8.651797    8.651797   8.651797
%%       HKCD  system-to-system         10.927848   11.303595  11.303595
%%       both  staves on page 1               16          16         16
%%
%%     The declared page could not squeeze — its staff spring sat at the ideal, floor fallen
%%     back to the drawn distance — while the control was already exact. ⚠️ AND THE SLACK IS
%%     VISIBLE IN THE OTHER SPRING: (9 − 9)/2 = 0 for the staff spring against
%%     (12 − 10.927848)/4 = 0.268038 for the system one, so a rigid spring is not a local
%%     defect and an entry reading only the staff gap would have understated the page. Both
%%     halves close together today, which is what says the spring was restored rather than the
%%     force re-tuned. The counts hold throughout — that is what makes the distances
%%     comparable at all, and it is precisely what LYRHKD/LYRHKN could not achieve.
\book {
  \probeTag "HKCD"
  \paper { max-systems-per-page = #8 }
  \score {
    \new GrandStaff <<
      \new Staff \with { \RemoveAllEmptyStaves } { \clef treble \repeat unfold 120 { c'4 d' e' f' } }
      \new Staff { \clef bass \repeat unfold 120 { c4 d e f } }
    >>
  }
}

\book {
  \probeTag "HKCN"
  \paper { max-systems-per-page = #8 }
  \score {
    \new GrandStaff <<
      \new Staff { \clef treble \repeat unfold 120 { c'4 d' e' f' } }
      \new Staff { \clef bass \repeat unfold 120 { c4 d e f } }
    >>
  }
}

%% LYRB / LYRBV — THE LYRIC BLOCK BETWEEN TWO STAVES OF ONE SYSTEM, which is the last branch
%%     of the loose chain still laid out at force 0 (HANDOFF 1, the second entry under
%%     "next"). Every lyric book above it hangs its block from the system's LAST staff, so
%%     the chain closes on the NEXT SYSTEM's first staff through a null line; here it closes
%%     on a staff of the SAME system, and LyricEngraver.DistributeLooseLines keeps out of it
%%     because the minimum that closes it is "an input this engraver is not given".
%%
%%     ★ THE ROOM IS THE SAME REFPOINT-TO-REFPOINT SPAN AS EVERY OTHER BLOCK'S, and that is
%%     source rather than observation: distribute_loose_lines is handed
%%     `last_spaceable_line_translation` and `-solution_[spring_idx]`
%%     (page-layout-problem.cc:936-939) — the previous spaceable staff's position in the
%%     PAGE's chain and this one's. The same call site serves a block between two systems and
%%     a block between two staves of one system. ONLY THE CLOSING MINIMUM DIFFERS: with the
%%     next staff inside the same system it is `min_offsets[k-1] - min_offsets[k]` and there
%%     is NO null line (:923-925), where a system boundary inserts one and closes on
%%     `elements_[i].padding - min_offsets[0]` (:927-933).
%%
%%     ⚠️ THE CLOSING SPRING IS A DIFFERENT SPEC FROM ANYTHING THE CORPUS HAS MEASURED. A
%%     Lyrics line's affinity is UP and the staff below it is spaceable, so get_spacing_spec
%%     takes its :1299-1312 branch and returns the LINE's `nonstaff-unrelatedstaff-spacing`
%%     with LARGE_STRETCH (10e5) added. The Lyrics context overrides that spec's padding to
%%     1.5 and declares NOTHING ELSE (engraver-init.ly:658), so the spring keeps the ideal
%%     1.0 and the compress strength 1.0 of the caller's own `Spring spring (1.0, 0.0)`
%%     (:1035) — which is exactly the "absent member" shape HANDOFF 1's item 0 is about, and
%%     this pair is where it stops being latent.
%%
%%     ⚠️ THE TWO BOOKS ARE ONE VERSE AND TWO, and the pair is what separates a chain that is
%%     SOLVED from one left at force 0. The block's own minimums are
%%       staff -> verse 1   2.050000 + 1.187880 + 0.500000 = 3.737890  (the same floor LYRV,
%%                          LYRMV and LYRHK all read; the melody stays inside the staff so its
%%                          own ink cannot bind it)
%%       verse -> verse     2.800000                                    (nonstaff-nonstaff's
%%                          minimum-distance; the ink distance 1.387880 is under it)
%%       verse -> staff     0.000000 + 2.050000 + 1.500000 = 3.550000  ("no" has no descender,
%%                          the next staff's up-skyline is its own top line, padding 1.5)
%%     so ONE verse needs 7.287890 and TWO need 10.087890 against the staff spring's ideal
%%     9.000000 (default-staff-staff-spacing, both staves bare — lyrics.two-staff.*
%%     established that reading). ⇒ ONE VERSE LEAVES SLACK AND TWO DO NOT, and those are the
%%     two regimes a force-0 port cannot tell apart (HANDOFF 5.3: record which regime you are
%%     in, and do not sit the measurement on a floor on both sides at once).
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       (a) LYRB  staff -> verse 1 = 5.450000. The room is the spring's ideal 9.000000
%%           (its floor 7.287890 is under it), the closing spring is blocked at its own
%%           minimum 3.550000 for every force below 2.55e-6, so the chain compresses on the
%%           first spring alone: 9.000000 - 3.550000, i.e. f = -0.009091 on a compress
%%           strength of 5.500000 - 0.000000. ★ FONT-FREE ON LILYPOND'S SIDE: 5.450000 is
%%           built from a basic-distance, a staff half-height and a padding, and the
%%           syllable's own ink drops out because 3.737890 does not bind.
%%       (b) LYRB  staff -> staff INSIDE the system = 9.000000, the spring's ideal.
%%       (c) LYRBV staff -> verse 1 = 3.737890 and verse step = 2.800000. With two verses the
%%           chain's minimums sum to exactly the room, so it is CRITICALLY COMPRESSED and
%%           every spring sits on its floor.
%%       (d) LYRBV staff -> staff INSIDE the system = 10.087890 — the staff spring's floor,
%%           `minimum_offsets_with_min_dist[staff1] - [staff2]` (:699-704), which is the same
%%           three numbers because the alignment applies minimum-distance but NOT
%%           basic-distance on the non-pure call (align-interface.cc:230-238 gates that on
%%           `INT_MAX == end`, and get_minimum_translations passes end = 0).
%%       (e) 8 staves on page 1 in both — four systems of two (HANDOFF 5.0 trap 8).
%%     (falsifier for (a): 5.500000, which would mean the chain between two staves is NOT
%%      solved on LilyPond's side either and the branch Lily# leaves at force 0 is right by
%%      accident. The whole port planned on top of this pair would then be unnecessary.)
%%     (falsifier for (c): 5.450000 or anything else above 3.737890 — that would mean the
%%      room is NOT the sum of the alignment's own minimums and the staff spring keeps slack
%%      the block cannot reach, i.e. the reservation and the solve read different minimums.)
%%     (falsifier for (d): 9.000000, which would mean the loose lines are NOT inside the
%%      staff spring's floor at all and the block overlaps the staff below it.)
%%
%%     MEASURED — (b), (c) and (e) HELD, (a) AND (d) MISSED, AND THE MISS IS THE USEFUL HALF.
%%       LYRB   staff -> verse 1 = 4.027851  (predicted 5.450000)
%%              staff -> staff   = 9.000000  ✔
%%       LYRBV  staff -> verse 1 = 3.737890  ✔   verse step = 2.800000  ✔
%%              staff -> staff   = 11.073064 (predicted 10.087890)
%%       8 staves on page 1 in both, on all three pages alike.  ✔
%%
%%     ★ BOTH MISSES ARE ONE WRONG TERM: the CLOSING MINIMUM, which the predictions built as
%%     "the next staff's top line 2.050000 + padding 1.500000 = 3.550000". It is neither end
%%     of that. Dumped straight off the VerticalAxisGroups (ly:grob-extent on the groups the
%%     System's 'vertical-alignment holds):
%%       - a staff's own up-extent is 3.800000, its CLEF and its STEMS, not its top line;
%%         its down-extent is -3.550000, the clef again. (The lyric line's is
%%         (-0.037044 . 1.187880): "no" has essentially no descender, as intended.)
%%       - and the skyline that meets it is the ACCUMULATED down-skyline — every staff and
%%         verse above, each raised by the distance already fixed (align-interface.cc:272-273
%%         raises and merges as it walks) — so the binding X is where the LYRIC HAS NO INK,
%%         over the next staff's clef, and what meets it there is the staff ABOVE.
%%     ⇒ THE SAME-LOOKING GAP IS A DIFFERENT NUMBER IN THE TWO BOOKS: 4.972149 with one verse
%%     (the staff above is only 3.737890 up) and 4.535174 with two (it is 6.537890 up, so the
%%     lyric's own outline binds instead). A port that writes this minimum as an extent sum
%%     will be right in neither book, and one book alone could not have shown that.
%%     ⚠️ SO NEITHER staff-to-lyric READING IS FONT-FREE, which the predictions also had
%%     wrong: (a) was expected to be built from a basic-distance, a staff half-height and a
%%     padding with the syllable dropping out. The syllable is in it twice over, through the
%%     raise. Both entries name that in their `why` so the port is not judged against zero.
%%
%%     WHAT LILY# READS TODAY: 5.500000 for BOTH staff-to-verse-1 readings — the force-0
%%     ideal, since LyricEngraver.DistributeLooseLines asks for a room only for the non-upper
%%     family — so the residuals are +1.472149 and +1.762110. The verse step is exact (it is
%%     rigid at every force) and so is the ONE-VERSE room, 9.000000: there the block's floor
%%     8.710039 is under the staff spring's ideal and the reservation cannot show. The
%%     TWO-VERSE room is +0.126936, which is where it does.
%%
%%     ⚠️ THE MELODY IS ON THE UPPER STAFF HERE, which is the swap that makes the book: every
%%     other two-staff lyric book puts it on the lower one. Both staves carry g'/a' so
%%     neither one's ink can bind (LYRMV's upper staff is high because the SYSTEM's up-ink
%%     was the quantity being held constant there; here the quantity is the gap BELOW the
%%     lyrics, so the lower staff must be the plain one). Syllables are "no" for the reason
%%     LYRV's header gives at length: an ascender or a descender would put the reading on a
%%     font metric and it would stop being a spec measurement.
\book {
  \probeTag "LYRB"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

\book {
  \probeTag "LYRBV"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

%% LYROS / LYRCH — A SYSTEM THAT ALSO CARRIES AN OSSIA, OR A CHORD ROW.  The last branch of
%%     the loose chain Lily# still lays out at FORCE 0: LayoutEngine.BuildLooseChainEnds and
%%     ComputeBetweenStavesEnd BOTH return null as soon as the system holds an ossia or a
%%     text ROW, so the lyric block gets room = +infinity and every spring sits at
%%     max(min, ideal) — 5.500000 — however tight the page is.
%%
%%     WHY THESE TWO BOOKS AND NOT ONE.  The two Lily# constructs decline for the same
%%     reason but are DIFFERENT THINGS to LilyPond, and the pair is what says whether the
%%     defect is one or two:
%%       - an OSSIA is a Staff.  It is not spaceable in Lily#'s model (it carries no staff
%%         spring, MultiStaffLayouter.StaffSprings skips it) but in LilyPond it is an
%%         ordinary staff in the alignment, spaced like any other.
%%       - a CHORD ROW is a ChordNames context, which LilyPond does treat as a loose line:
%%         non-spaceable, distributed by distribute_loose_lines exactly as the Lyrics are.
%%     ⇒ THEY SHOULD NOT READ ALIKE ON LILYPOND'S SIDE, and if they do, that is the finding.
%%
%%     THE CONTROL IS LYRB, deliberately, not a fourth book: LYROS and LYRCH are LYRB with
%%     ONE line added and nothing else changed — same two staves, same 120 bars of g'/a',
%%     same 480 syllables, same paper.  So every difference from LYRB's readings is the added
%%     line, and LYRB's own numbers are already ported and understood
%%     (lyrics.between-staves.staff-to-lyric, +0.131349).
%%     ⚠️ ADDING A LINE IS NOT AN IDENTITY (HANDOFF 5.0 prefers those): one more line per
%%     system changes how many systems fit, which changes the page's spring solution, which
%%     changes the room the chain is solved into.  So LilyPond's staff-to-lyric here is NOT
%%     expected to equal LYRB's — what is expected is that it is SOLVED, i.e. off 5.500000.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0):
%%       (a) LYROS staff -> verse 1: BELOW 5.500000, compressed like LYRB's 4.027851 and
%%           probably close to it — the ossia sits ABOVE the upper staff, so it does not come
%%           between the block and the staff under it.
%%       (b) LYRCH staff -> verse 1: BELOW 5.500000 as well.  The chord row is ABOVE the
%%           staff too, so the block's own chain is untouched; what the row costs is page
%%           height, hence fewer systems.
%%       (c) staves-on-first-page: FEWER than LYRB's 8 in both, since each system is taller.
%%       (d) staff -> staff INSIDE the system: 9.000000 in both, the spring's ideal, exactly
%%           as LYRB — neither added line is BETWEEN the two staves.
%%     (falsifier for (a)/(b): 5.500000, which would mean LilyPond leaves the chain at force
%%      zero too and Lily#'s branch is right by accident.  Then the whole item is closed by
%%      deleting it rather than by porting anything.)
%%     (falsifier for (d): anything else — that would mean the added line lands between the
%%      staves and this pair is measuring a different quantity from the one it is named for.)
%%
%%     ⚠️ WHAT LILY# READS TODAY, so the residual is not mistaken for a font quantity:
%%     5.500000 for staff -> verse 1 in both, the force-0 ideal.
%%
%%     MEASURED — (a) AND (b) HELD EXACTLY, (c) IS UNMEASURABLE HERE, (d) MISSED ON LYROS.
%%       LYROS  staff -> next loose line = 4.027851   staff-to-staff INSIDE = 18.000000
%%       LYRCH  staff -> next loose line = 4.027851   staff-to-staff INSIDE =  9.000000
%%       LYRB   staff -> next loose line = 4.027851   staff-to-staff INSIDE =  9.000000
%%
%%     ★★ AND THAT IS THE STRONGEST FORM OF THE PAIR, ARRIVED AT BY ACCIDENT (HANDOFF 5.0:
%%     the best pairs are the ones where LILYPOND IS THE IDENTITY). LYRCH is identical to
%%     LYRB on EVERY spacing quantity in the dump — the chain 4.027851, the inside distance
%%     9.000000, system-to-system 12.000000, four systems on page 1 — and differs only in
%%     where the first ink starts, which is the chord row's own glyphs above the staff. So
%%     LilyPond's difference is ZERO and whatever Lily# reads differently is the defect
%%     outright, with no font quantity and no page-breaking term in it.
%%     LYROS is the identity too for the quantity this item is about: the same 4.027851.
%%
%%     ⇒ THE FALSIFIER DID NOT FIRE. LilyPond does not leave the chain at force 0 when an
%%     ossia or a chord row is present; it solves it to exactly the number it solves LYRB to.
%%     So Lily#'s "decline to supply a room when the system carries one of these" has NO
%%     counterpart in the source, and the item is a port rather than a decision to delete.
%%
%%     ★ (d) MISSED ON LYROS AND THE MISS NAMES THE MECHANISM: 18.000000, not 9.000000.
%%     `staff-refpoint-extent` spans every SPACEABLE staff (lily/system.cc:705-717), so its
%%     width is 9.000000 only while there are two of them. An ossia is a `\new Staff`, so
%%     LilyPond has THREE and the width is the whole span. ⇒ AN OSSIA IS SPACEABLE IN
%%     LILYPOND — it carries a staff spring and sits in the page's chain like any staff —
%%     where Lily# treats it as non-spaceable (MultiStaffLayouter.StaffSprings skips it, and
%%     the loose-chain builders decline the whole system because of it). That is a DIFFERENT
%%     defect from the chord row's, which is why the two books were opened together, and it
%%     is the one that has to be decided first: porting the room for an ossia system means
%%     deciding whether the ossia joins the spring chain.
%%     ⚠️ SO THE TWO INSIDE READINGS ARE NOT COMPARABLE: LYROS's 18.000000 is a three-staff
%%     span and LYRB's 9.000000 is a two-staff gap. Only LYRCH's is a like-for-like control.
%%
%%     ⚠️ (c) CANNOT BE READ ON THIS PAPER: `max-systems-per-page = #4` caps all three books
%%     at four systems, so "fewer systems" has nowhere to show. The staff COUNT still
%%     differs (8 for LYRB and LYRCH, 12 for LYROS) because LYROS's systems hold three
%%     staves. Anyone wanting the page-breaking half of this must lift the cap first
%%     (HANDOFF 5.0 trap 7: a cap cannot create the regime it is capping).
%% LYRMC — LYRM WITH A CHORD ROW, and it asks the ONE question the LYRCH pair could not.
%%     LYRCH's row sits ABOVE the anchor staff, so it is outside the span the room covers
%%     and the narrowing that closed it never had to decide anything hard. THIS book puts
%%     the lyrics under the system's LAST staff (LYRM's arrangement), so the block's room
%%     runs from that staff to the NEXT SYSTEM's first staff — and the next system's chord
%%     row is INSIDE it. LilyPond puts that row into the very chain the lyrics are
%%     distributed by (page-layout-problem.cc:948-990 collects every non-spaceable line
%%     between two spaceable ones); Lily# gives it a band of its own (HANDOFF 3). So this is
%%     the case where 'what room is there' genuinely has two answers, and it is the one
%%     LayoutEngine.BuildLooseChainEnds still declines whole-score.
%%
%%     WHAT THE PAIR DECIDES, and it is a fork rather than a number:
%%       - if LilyPond's staff-to-lyric here EQUALS LYRM's, the row costs the lyric line
%%         nothing, the two engines can disagree about where the row lives and still agree
%%         about the lyric, and Lily#'s decline is over-cautious exactly as it was for LYRCH
%%         — narrow it the same way and the item closes.
%%       - if it DIFFERS, the row and the lyrics really are sharing one room, Lily#'s
%%         independent band cannot express that, and closing this means moving the HANDOFF 3
%%         decision rather than the guard. That is a judgement call, not a port.
%%
%%     PREDICTION, written before running (HANDOFF 5.0): DIFFERS. The row is a loose line in
%%     the chain, so the chain has one more spring and one more minimum, and the solve
%%     cannot land where it lands without it — expect staff-to-lyric ABOVE LYRM's 5.500001,
%%     since the row eats room the lyric line was stretching into.
%%     (falsifier: exactly 5.500001, which would mean the row is spaced independently after
%%      all and the fork above takes its first branch.)
%%
%%     ⚠️ THE CONTROL IS LYRM, unchanged: this is LYRM with one ChordNames added and nothing
%%     else touched — same two staves, same 120 bars, same 480 syllables, same paper.
%%
%%     MEASURED — IT DIFFERS, SO THE FORK TAKES ITS SECOND BRANCH. The direction of the
%%     prediction was WRONG and the miss is the useful half (HANDOFF 5.0).
%%       LYRM   staff/loose -> next loose line = 5.500000, 5.500001
%%       LYRMC  staff/loose -> next loose line = 4.608814, 5.500001
%%       both   system-to-system = 12.000000    staff-to-staff INSIDE = 9.000000
%%
%%     ★ THE ROOM DID NOT GROW FOR THE ROW — 12.000000 in both, which is the same principle
%%     LooseLineSpacer's remarks already carry: a loose line is absent from the page's chain,
%%     so system-system-spacing is whatever it would have been. The row is SQUEEZED INTO the
%%     room that already exists, alongside the lyrics. ⇒ the prediction said the lyric would
%%     be pushed FURTHER from its staff and it is pulled CLOSER, 5.500000 -> 4.608814: one
%%     more spring in a fixed room means the solve compresses, and the first spring is the
%%     one with slack to give.
%%
%%     ⇒ THE ROW AND THE LYRICS REALLY DO SHARE ONE ROOM, which is what the pair was built to
%%     decide. Lily# gives the row a band of its own, and a band cannot be squeezed by
%%     somebody else's chain — so BuildLooseChainEnds' decline is NOT over-cautious here the
%%     way ComputeBetweenStavesEnd's was. Closing this means putting the row into the
%%     alignment as a loose line, not narrowing a guard.
%%     ⚠️ THIS HEADER FIRST CALLED THAT 'a judgement call and not a port' AND THAT WAS WRONG
%%     (corrected 2026-07-27). The policy is literal porting of LilyPond's code and it does
%%     not bend for a Lily# model: :948-990 puts every non-spaceable staff into loose_lines
%%     and closes the run on the next spaceable one, so the row IS in the Lyrics' chain, and
%%     "Lily# models it as a band" is the thing to change rather than a reason to stop.
%%     ⚠️ THE PORT MOVES WHERE THE ROW SITS, which is the quantity HANDOFF 3's
%%     independent-row decision names, so the two are one island and not two.
%%
%%     ⚠️ ONLY THE FIRST OF THE TWO READINGS MOVES. The second, 5.500001, is the LAST system
%%     on the page, whose chain runs to the page edge instead of to a next system — no row
%%     between it and the foot, so nothing shares its room. The pair therefore also says the
%%     defect is per-chain and not per-score, which is what BuildLooseChainEnds' own remark
%%     already suspected when it called the whole-score bail-out coarser than it needs to be.
%%
%%     ★★ THE CHAIN, TERM BY TERM (read off this book's own PROBEV VAG lines, page 1,
%%     2026-07-27).  The total 4.608814 was never the hard part; these four are, because a
%%     port can be checked one spring at a time against them and a port checked on a total
%%     is a port checked on a coincidence.
%%
%%       room                       12.000000   staff 2 of a system -> staff 1 of the next
%%       s0  staff  -> lyrics        4.608814   nonstaff-relatedstaff (5.5 / str 1 / cmp 5.5)
%%                                              min 3.737890 -- OFF its floor
%%       s1  lyrics -> null          0.837966   HUGE_STRETCH, cmp 1, min 0   = 1 + f
%%       s2  null   -> row           2.973743   HUGE_STRETCH  -- AT its minimum
%%       s3  row    -> next staff    3.579477   ChordNames' own nonstaff-relatedstaff
%%                                              -- AT its minimum
%%                                 ----------
%%                                  12.000000   f = -0.162033841
%%
%%     ⚠️ s3's SPRING IS NOT THE Lyrics ONE even though the property has the same name.
%%     ly/engraver-init.ly:722 declares only `(padding . 0.5)` for ChordNames and
%%     define-grobs.scm has no default for that property at all, so read_spacing_spec writes
%%     nothing else and the ideal 1.0 with both strengths 1.0 are the CALLER's
%%     `Spring spring (1.0, 0.0)` (page-layout-problem.cc:1035).  A port that reuses the
%%     Lyrics' 5.5 here builds a different spring with the same name.
%%
%%     ★ AND BOTH BINDING MINIMUMS BREAK INTO FORMULAS:
%%       m2 = 0.037044 (the lyric line's own descent)
%%          + 1.936699 (the row's up ink)
%%          + 1.000000 (system-system-spacing padding)
%%          = ONE MORE STEP OF THE SAME ALIGNMENT WALK that produced m0, taken from the
%%            accumulation the lyric line left behind.  That is what
%%            `elements_[i].min_distance + elements_[i].padding` IS: :644-645 recomputes
%%            min_distance as `first_skyline.distance (bottom_skyline_) -
%%            bottom_loose_baseline_`, and the subtraction is exactly the re-referencing a
%%            running walk does for free.
%%       m3 = 0.034477 (the row's descent) + 3.045000 + 0.500000 (the ChordNames padding),
%%            where 3.045000 is the staff's UP SKYLINE AT THE CHORD'S x.
%%     ⚠️ THE 3.045000 IS SOLID AND ITS BREAKDOWN IS NOT.  The number is
%%     3.579477 - 0.034477 - 0.500000, arithmetic on dumped values.  Reading it as
%%     2.500000 (g'' on its staff position) + 0.545000 (the notehead's ink above its centre,
%%     the same 0.545 Lily#'s LILC carries) is an INFERENCE -- nobody has dumped the notehead
%%     grob to confirm it.  It is a six-digit fit and it predicts that Lily# reproduces m3
%%     exactly, so it is worth writing down AND worth checking before it is leaned on; the
%%     PROBEV GROB lines this file already emits can settle it in one run.
%%     ⚠️ THE BAR NUMBER DOES NOT ENTER m3, and that is the check that this is a SKYLINE
%%     distance and not an extent: systems 2..4 carry one, so their first staff's up extent
%%     is 4.303666 rather than 3.800000, and yet the row-to-staff distance is the same
%%     3.579477 on all four.  The number sits left of the clef, the first chord above the
%%     first note; they never share an x.
%%
%%     ★ WHAT THE PORT SHOULD LAND ON, written before it (HANDOFF 5.0).  With s0 and s1 the
%%     only free springs, room 12 gives  f = (5.5 - m2 - m3) / 6.5  and  s0 = 5.5 + 5.5 f,
%%     so  d(s0)/d(m2 + m3) = -0.846154  exactly.  Lily#'s m2 is made of ITS lyric descent
%%     and ITS chord-symbol cap height, both larger, so the port should land BELOW 4.608814
%%     by 0.846154 times whatever its (m2 + m3) exceeds 6.553220 by: a NEGATIVE residual
%%     made of font metrics rather than of mechanism.
%%     ⚠️ FALSIFIER: landing ABOVE 4.608814, or staying at 5.500000, means the row is not in
%%     the chain and the port did not take.
\book {
  \probeTag "LYRMC"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode { \repeat unfold 120 { c1 } } }
      \new Staff { \repeat unfold 120 { g''4 a'' g'' a'' } }
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
    >>
  }
}

\book {
  \probeTag "LYROS"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff = "main" <<
        \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } }
        \new Staff \with {
          \remove "Time_signature_engraver"
          alignAboveContext = "main"
          fontSize = #-3
          \override StaffSymbol.staff-space = #(magstep -3)
          \override StaffSymbol.thickness = #(magstep -3)
        } { \repeat unfold 120 { g'4 a' g' a' } }
      >>
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

\book {
  \probeTag "LYRCH"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode { \repeat unfold 120 { c1 } } }
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

%% CHR1 / CHR2 — WHERE THE FIRST STAFF SITS WHEN A CHORD ROW STANDS OVER IT, and the
%%     pair is built to STRADDLE THE BOUNDARY that quantity has (HANDOFF 5.0: when a
%%     quantity switches regime, put a point on either side of the switch and one across
%%     it). top-system-spacing's spring is max(basic-distance 6, header + the ink the
%%     system carries above its first SPACEABLE staff's refpoint + padding 1), so the ink
%%     is measured at all only while it exceeds 5.
%%
%%     ⚠️ WHY THE CORPUS HAD NO SUCH POINT. Book LYRCH already reads that ink — its
%%     ChordName's stencil is [3.000000, 4.998884] about the refpoint — and 4.998884 + 1
%%     is 5.998884, which LOSES to 6 by 0.001116. So every chord-row book here reads
%%     11.690551 whatever the floor does, and the corpus was blind to the floor for its
%%     whole life: session 255 shipped a port for a USER-REPORTED overlap (a lead sheet's
%%     chord symbols printed through the title) that no entry in this ledger could see.
%%
%%     THE TWO BOOKS DIFFER IN ONE WORD: the chords are `c1` in CHR1 and `cis1:m` in CHR2.
%%     LilyPond prints a chord name's accidental as an Emmentaler glyph lifted magstep*0.6
%%     above the baseline (scm/chord-name.scm:80-95), which raises the symbol's ink TOP and
%%     moves nothing else — MEASURED session 254 on the same construction: Am's ink top is
%%     1.907290480437992 and A#m's is 2.224872498154520, +0.317582017716528. Lily# ports
%%     that as ChordNameGlyphRun and agrees with LilyPond to fifteen digits, so the one
%%     word this pair varies is a quantity where the two engines are already identical.
%%
%%     PREDICTION, written before running (HANDOFF 5.0-2):
%%       CHR1  first STAFF refpoint = 11.690551 = top-margin 5.690551 + basic-distance 6.
%%             The floor is 4.998884 + 1 = 5.998884 and LOSES, exactly as in LYRCH.
%%       CHR2  first STAFF refpoint = 12.007017
%%             = 5.690551 + (4.998884 + 0.317582 + 1). The floor BINDS, and this is the
%%             FIRST book in this corpus where it does.
%%     FALSIFIER, and it is a real one: if CHR2 also reads 11.690551 then one accidental is
%%     not enough to cross the boundary and the pair measures nothing until the symbol is
%%     made taller (a second row, or a name with a superscript). The prediction is falsified
%%     by 0.008 of margin, so it is worth writing down rather than assuming.
%%     ⚠️ THE COUNTS TRAVEL WITH THEM (HANDOFF 5.0, trap 8): a refpoint reading off a page
%%     holding a different number of systems is a plausible number about another page.
%%     ⚠️ ONE STAFF ON PURPOSE. LYRCH carries two, so its page also solves a staff spring;
%%     here the only spring above the first staff is the one being measured.

\book {
  \probeTag "CHR1"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode { \repeat unfold 120 { c1 } } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

\book {
  \probeTag "CHR2"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode { \repeat unfold 120 { cis1:m } } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}


%% TABS / NST — WHAT UNIT A STAFF-TO-STAFF DISTANCE IS IN when one of the staves is a
%%     TAB staff, and the pair is built so that LilyPond's side is a CONTROL rather than
%%     an unknown (HANDOFF 5.0): the two books are the same music on the same paper and
%%     differ only in whether the LOWER staff is a TabStaff or an ordinary Staff.
%%
%%     ⚠️ WHY IT IS WORTH A PAIR. LilyPond's TabStaff sets StaffSymbol.staff-space = 1.5
%%     for every string count (ly/engraver-init.ly), so a six-string tab staff's LINES
%%     span (6-1) * 1.5 = 7.500000 where a notation staff spans 4.000000 — already pinned
%%     exact by tab.staff.line-span.six-string. The open question is what that does to the
%%     DISTANCE to the staff above it, and there are two possible answers with a factor of
%%     nearly two between them:
%%       (a) Align_interface works between VerticalAxisGroup REFERENCE POINTS and
%%           staff-staff-spacing's basic-distance is in the PAGE's staff spaces, so the
%%           refpoint distance is 9.000000 whatever the lines below it do; or
%%       (b) the distance is measured in the staff's OWN space, or from its edges, and a
%%           taller staff pushes the pair apart.
%%
%%     PREDICTION, written before running (HANDOFF 5.0-2): (a). Both books read the SAME
%%     refpoint-to-refpoint distance, 9.000000 to six digits, because
%%       - align-interface.cc:201-285 accumulates translations between refpoints, and a
%%         VerticalAxisGroup's refpoint is staff position 0 — the middle LINE of whatever
%%         staff it is, six-line or five-line;
%%       - the basic-distance is read off the spec in output staff-spaces, and nothing in
%%         TabStaff overrides staff-staff-spacing.
%%     ⚠️ The floor must not bind or the reading is the two staves' ink instead of the
%%     spec: the music is kept inside both staves for that reason, and if either book
%%     comes back ABOVE 9.000000 the floor DID bind and the pair measures nothing until
%%     the pitches are lowered.
%%
%%     FALSIFIER, and it is a real one rather than decoration: if TABS reads 10.750000 —
%%     9 + (7.5 - 4)/2 — then LilyPond is spacing from the staff EDGES and Lily#'s own
%%     nominal-height arithmetic (MultiStaffLayouter.GapSpan, which subtracts a flat 4.0)
%%     is accidentally right for tab. The whole "convert the rest to the refpoint frame"
%%     island would then be a defect that does not exist, and its ~20 snapshots must not
%%     move.
%%
%%     ⇒ WHICHEVER WAY IT READS, IT SELECTS THE NEXT PIECE OF WORK (HANDOFF 5.0's fork):
%%     9.000000 means Lily# places a tab pair 1.750000 too far apart and the island is
%%     real; 10.750000 means it is not.
\book {
  \probeTag "TABS"
  \paper { ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \repeat unfold 8 { g'4 a' g' a' } }
      \new TabStaff { \repeat unfold 8 { g4 a g a } }
    >>
  }
}

%% NST — THE CONTROL. Identical to TABS except that the lower staff is an ordinary Staff,
%%     so it is the same music, the same paper and the same two elements of one
%%     VerticalAlignment. Its reading is the number TABS is compared against; carrying it
%%     rather than assuming 9.000000 is what makes the pair say whether the floor bound
%%     (HANDOFF 5.0: a control that is non-zero for its own reasons is still a control).
\book {
  \probeTag "NST"
  \paper { ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \repeat unfold 8 { g'4 a' g' a' } }
      \new Staff { \repeat unfold 8 { g4 a g a } }
    >>
  }
}

%% OSSD — THE SAME QUESTION FOR AN OSSIA-SIZED STAFF, and it is asked separately from TABS
%%     because the two differ in what is scaled. A TabStaff's staff-space is 1.5 by
%%     declaration and everything else about it is full size; an ossia is fontSize -3 with
%%     StaffSymbol.staff-space AND thickness scaled by magstep -3, so if any distance in
%%     LilyPond followed a staff's own space rather than the page's, this is the book where
%%     it would show. The lower staff is spelled exactly as LYROS's ossia is, minus the
%%     alignAboveContext that would nest it, so it is an ordinary element of the alignment.
%%
%%     PREDICTION, written before running: 9.000000, the same as TABS and NST.
%%     staff-staff-spacing's basic-distance is read in output staff-spaces and
%%     Align_interface accumulates between refpoints; magstep scales the STAFF, not the
%%     alignment's units.
%%     FALSIFIER: 9 * (magstep -3) = 6.363961, or any other reading that tracks the ossia's
%%     own space. That would mean Lily#'s `gap * OssiaScale` is LilyPond's rule after all and
%%     the ossia half of the frame island is not a defect.
%%
%%     ⇒ THE FORK: 9.000000 means Lily# is wrong twice over on an ossia pair — once in the
%%     nominal span it subtracts and once in the scale it multiplies by — and the two are
%%     separate quantities that must not be closed with one number.
\book {
  \probeTag "OSSD"
  \paper { ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \repeat unfold 8 { g'4 a' g' a' } }
      \new Staff \with {
        fontSize = #-3
        \override StaffSymbol.staff-space = #(magstep -3)
        \override StaffSymbol.thickness = #(magstep -3)
      } { \repeat unfold 8 { g'4 a' g' a' } }
    >>
  }
}

%% TABL / NTL — WHERE A TAB STAFF SITS ON THE PAGE, which is the question TABS/NST asked
%%     one frame down. Those two measured a distance BETWEEN two staves of one system and
%%     found it to be 9.000000 whatever the lower staff's line span is. These two measure
%%     the PAGE's own anchors against the same staff: how far below the paper edge the
%%     first staff's refpoint lands, and how far apart consecutive systems sit. Both books
%%     are ONE staff and many systems, so every distance here is the page's and none of it
%%     is Align_interface's.
%%
%%     The pair is again built so that LilyPond's side is a CONTROL rather than an unknown
%%     (HANDOFF 5.0): the same music, the same paper, and the ONE difference is whether the
%%     staff is a TabStaff or an ordinary Staff. NTL is spelled exactly as NST's lower staff
%%     is — no clef, so `g` and `a` hang below the treble staff on their ledger lines, which
%%     is where Lily# draws the same part too.
%%
%%     ⚠️ WHY IT IS WORTH A PAIR, and it is not the same question TABS answered. A six-string
%%     tab staff's LINES span (6-1) * 1.5 = 7.500000 (tab.staff.line-span.six-string), so its
%%     refpoint — staff position 0, the middle of the span — sits 3.750000 below its top line
%%     where an ordinary staff's sits 2.000000 below. Every page anchor LilyPond writes is
%%     against the REFPOINT (top-system-spacing to the first one, system-system-spacing
%%     between them), and Lily# converts between that frame and its own system-origin frame
%%     with a NOMINAL half staff: LayoutUtilities.CalculateFirstSystemY subtracts
%%     `_options.StaffHeight / 2.0`, which is 2.000000 for every staff there is. On a tab
%%     staff that conversion is 1.750000 short, and nothing in the corpus reads a page anchor
%%     over a staff that is not 4.000000 tall.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2). BOTH books read the SAME two
%%     numbers, to six digits:
%%       - first STAFF refpoint below the paper edge = 11.690551, i.e. top-margin plus
%%         top-system-spacing's basic-distance 6.000000, because the floor loses: the ink a
%%         tab system carries above its refpoint is its own top line at 3.800000 (7.6/2 —
%%         MEASURED already, in the TABS dump, and recorded in the `why` of
%%         staff.tab-pair.staff-staff-inside), and 3.800000 + padding 1 = 4.800000 < 6.
%%       - staff-to-staff = system-to-system = 12.000000, system-system-spacing's
%%         basic-distance, because that floor loses too: 3.800000 below + 3.800000 above +
%%         padding 1 = 8.600000 < 12.
%%
%%     ⚠️ THE FLOOR MUST NOT BIND ON EITHER, and the dump says which: a reading ABOVE either
%%     basic-distance means the book has stopped measuring the page's spec and started
%%     measuring the tab staff's ink, and the numbers have to be re-read as such rather than
%%     compared with Lily#'s.
%%
%%     FALSIFIER, and it is the one the ⑷ island actually turns on: `ink below it` on TABL's
%%     last system is 3.800000 EXACTLY — the outermost string line and nothing further. If it
%%     comes back LARGER, then a fret digit or the TAB clef reaches past the outermost line,
%%     the silhouette of a tab system is NOT its staff symbol, and the height at which those
%%     grobs enter it is the excess — which is precisely the quantity Lily#'s
%%     SkylineBuilder.BuildSystemSkylines has no reading for today (it seeds notes and clef
%%     about a derived `-staffHeight/2` while seeding the LINES at the placed span). The
%%     Clef's own ink extent is printed alongside for the same reason.
%%
%%     ⇒ THE FORK. If the two books read identically, then LilyPond spaces a tab page exactly
%%     as it spaces a notation page and the whole of Lily#'s difference on the tab side is its
%%     own — the island is real and this is the key that justifies moving its snapshots. If
%%     they do NOT, LilyPond's own anchor moves with the staff's span, and the port is a
%%     different one: not "convert with the placed half span" but "the anchor is not the
%%     refpoint at all".
%%
%%     ⚠️ THE LILY# TWIN MUST USE A SILENT SECTION REFERENCE (`form main { ~Main }`). A
%%     printed rehearsal mark is ~3.86 ss of ink landing exactly where the first-refpoint
%%     reading looks, and it is what made the first draft of probe V read 14.350551 against
%%     11.690551. Octaves are the probe's: LilyPond `g` is Lily# `g,`.
%%
%%     MEASURED 2026-07-28 — EVERY PREDICTION HELD, and the falsifier did not fire:
%%       - both books: first STAFF refpoint 11.690551, staff-to-staff 12.000000, 3 systems
%%         on one page. LilyPond spaces a tab page exactly as it spaces a notation page.
%%       - TABL `ink below it` = 3.800000 EXACTLY (the outermost string line, 7.6/2) against
%%         NTL's 5.045000 (its notes hang below the staff on ledger lines). The ink differs
%%         by over a staff space and neither anchor moves — which is what makes both readings
%%         the SPEC's rather than the ink's.
%%       - the TAB clef's own ink about the refpoint is [-2.880000, 2.880000], INSIDE the
%%         lines, where the treble clef's is [-3.550000, 3.800000].
%%     ⇒ ★ THE FALSIFIER'S FAILING TO FIRE IS THE FINDING. A tab system's silhouette IS its
%%     staff symbol: no fret digit and no clef reaches past the outermost line. HANDOFF's
%%     item (4) asked for "a point that measures at what height a tab staff's notes and clef
%%     enter the system silhouette" — they do not enter it, so seeding them at the placed
%%     offset cannot change the silhouette. What the nominal 4.000000 does change is the
%%     ANCHOR: Lily# reads 13.440551 here, +1.750000 = 3.750000 - 2.000000, and the gap on
%%     the same page is exact.
%%
%%     ⚠️ THE BAR NUMBER IS A THIRD, UNCARRIED READING and is worth knowing before someone
%%     opens it: `staff refpoint -> BarNumber baseline` is 4.826208 on TABL against 3.076208
%%     on NTL — 1.750000 apart, and on LilyPond's side, because the number rides padding 1
%%     above the staff's own INK top (3.800000 for tab, 2.050000 for notation). That one IS
%%     ink-driven, so it is a different quantity from the two carried here and needs its own
%%     pair rather than being read off this dump.
\book {
  \probeTag "TABL"
  \paper { indent = 0 ragged-bottom = ##t }
  \score { \new TabStaff { \repeat unfold 24 { g4 a g a } } }
}

%% OSSU / OSSUN — THE OSSIA DISTANCE ITSELF, in the arrangement Lily# actually renders.
%%     OSSD (above) already established the LilyPond FACT that an ossia-sized staff sits
%%     9.000000 from its neighbour — but it puts the small staff BELOW, while Lily#'s `ossia`
%%     hangs ABOVE the staff it decorates, so the two sides were not the same arrangement and
%%     it could not become a ledger pair (HANDOFF 5.0, traps 5 and 6). This is that pair.
%%
%%     ⚠️ THE PAIR ISOLATES THE SCALE AND NOTHING ELSE. Both books nest a second Staff into
%%     the first with `alignAboveContext`, which is LilyPond's own ossia idiom (NR "Ossia
%%     staves") and the same spelling book LYROS uses; they differ ONLY in whether that staff
%%     is shrunk (fontSize -3 with StaffSymbol.staff-space and thickness at magstep -3). So a
%%     difference between them is the SCALE's doing and cannot be the nesting's.
%%
%%     PREDICTION, written before running: BOTH read 9.000000 — staff-staff-spacing's
%%     basic-distance, in the PAGE's staff spaces — because Align_interface accumulates
%%     between VerticalAxisGroup REFERENCE POINTS and magstep scales the STAFF, not the
%%     alignment's units. OSSD read exactly that with the small staff below.
%%     FALSIFIER, and it is the whole point of the pair: 9 * (magstep -3) = 6.363961 on OSSU
%%     against 9.000000 on OSSUN. That would mean LilyPond DOES shrink the distance with the
%%     staff, and Lily#'s `gap * OssiaScaleFactor` would be its rule after all.
%%
%%     ⇒ THE FORK: equal means `gap * OssiaScaleFactor` is an invention with no counterpart
%%     and the ossia half of the frame island is a real defect with a key; 6.363961 means it
%%     is a port and the island's ossia half does not exist.
%%
%%     ⚠️ The music is kept inside both staves so the FLOOR does not bind — a reading ABOVE
%%     9.000000 on either side means the ink is being measured and not the spec.
%%
%%     MEASURED 2026-07-28 — THE PREDICTION HELD AND THE FALSIFIER DID NOT FIRE:
%%     OSSU 9.000000, OSSUN 9.000000, equal to the digit, on a page with slack (neither floor
%%     binds — the ink below the last refpoint is 3.550000 on both). The ossia's own ink IS
%%     scaled and says so in the same dump: its clef reads [-2.510179, 2.687006] about its
%%     refpoint against the full-size [-3.550000, 3.800000], and its refpoint still sits
%%     exactly 9.000000 above the staff it decorates.
%%     ⇒ ★ THE FORK TOOK THE FIRST BRANCH, now in the arrangement Lily# actually renders
%%     (the small staff ABOVE, which is what OSSD could not say): LilyPond scales the STAFF
%%     and not the DISTANCE, so Lily#'s `gap * OssiaScaleFactor` has no counterpart. Together
%%     with OSSD (small staff BELOW) and TABS (a tab staff below) this is the third
%%     arrangement to read 9.000000 — the distance does not know what it is spacing.
%%
%%     ⚠️ IT IS A LEDGER PAIR AS OF 2026-07-28 — `staff.ossia-pair.staff-staff-inside` and
%%     `staff.ossia-control.staff-staff-inside`. It was held back for one session on a
%%     reported LILY# DRAWING defect: 'an `ossia` that spans EVERY measure draws its staff
%%     lines at the system origin with UNSCALED 1.000000 spacing and a width of 135.55 on a
%%     119.50 page'. ⇒ ★ THAT READING WAS OF THE OSSIA'S OWN SCALE GROUP, not of the page.
%%     Measured again through RecordingDocumentContext, which composes the group transform:
%%     the lines sit at 6.158232..8.986632 with a staff space of 0.707100 and a width of
%%     95.844921 on a page 119.501575 wide. 95.844921 / 0.7071 = 135.546 — the reported
%%     width to six digits — and 'the system origin' with 'unscaled 1.000000' is that same
%%     group-local frame, since SharedRenderer.cs:445 draws an ossia's content at
%%     localStaffY = pageHeight and lets the transform place it. The drawing is CORRECT and
%%     no fragment-shaped pair is needed. Lily#'s reading on this arrangement is 7.363919,
%%     which is 1.636081 below LilyPond, and all of it is `gap * OssiaScaleFactor`.
%%     ⚠️ THE GENERAL FORM (HANDOFF 5.3): a coordinate read inside a scale group is not a
%%     coordinate on the page. Compose the transform before calling a number a defect.
\book {
  \probeTag "OSSU"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff = "main" <<
      \new Voice { \repeat unfold 8 { g'4 a' g' a' } }
      \new Staff \with {
        \remove "Time_signature_engraver"
        alignAboveContext = "main"
        fontSize = #-3
        \override StaffSymbol.staff-space = #(magstep -3)
        \override StaffSymbol.thickness = #(magstep -3)
      } { \repeat unfold 8 { g'4 a' g' a' } }
    >>
  }
}

%% OSSUN — THE CONTROL: the same nesting, the same alignAboveContext, a FULL-SIZE staff.
\book {
  \probeTag "OSSUN"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff = "main" <<
      \new Voice { \repeat unfold 8 { g'4 a' g' a' } }
      \new Staff \with {
        \remove "Time_signature_engraver"
        alignAboveContext = "main"
      } { \repeat unfold 8 { g'4 a' g' a' } }
    >>
  }
}

%% OSSK / OSSKN — IS AN OSSIA'S SPRING IN THE PAGE'S CHAIN?  The pair that decides whether
%%     MultiStaffLayouter.StaffSprings' skip of an ossia pair is a DEFECT or a PORT. It is
%%     OSSU/OSSUN with two things changed and nothing else: 120 bars instead of 8, and a
%%     page that must SQUEEZE them. Same nesting, same alignAboveContext, same fontSize -3
%%     with StaffSymbol.staff-space and thickness at magstep -3, same g'/a' kept inside the
%%     staff so the ink decides nothing.
%%
%%     WHY THE PAIR AT REST COULD NOT ANSWER IT. OSSU/OSSUN are one page with slack, so both
%%     springs sit at their ideal — and a RIGID pair and a SOLVED one both read 9.000000
%%     there. Lily#'s skip was measured by perturbation on 2026-07-28 and moved NOTHING,
%%     because every ossia book in the corpus is a single content-sized page at force 0
%%     (HANDOFF 5.3: "perturbed and nothing moved" can be a regime rather than a death).
%%     This is the regime where the two readings come apart.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0):
%%       (a) OSSK inside < 9.000000. An ossia is a `\new Staff`, so it is a SPACEABLE staff
%%           and every consecutive spaceable pair gets a spring.
%%       (b) the falsifier is inside the same dump, one force per page, as JSK's is:
%%           (9 - inside) / 2 == (12 - system-to-system) / 4 to six digits.
%%       (c) neither reading may sit on its floor or it measures ink and not the spring:
%%           OSSK's alignment minimum is the ossia's scaled clef 2.510179 + the staff's
%%           3.800000 + padding 1 = 7.310179, OSSKN's is 3.550000 + 3.800000 + 1 = 8.350000.
%%       (d) OSSKN is the like-for-like control and is NOT expected to print OSSK's number:
%%           the ossia's ink is smaller, so its systems are shorter, its page keeps a
%%           different slack and solves a different force. What must hold on BOTH is (b).
%%
%%     MEASURED 2026-07-28 — (a), (c) AND (d) HELD; (b) MISSED, AND THE MISS NAMES THE SPEC.
%%       OSSK  page 1 (8 systems): inside 8.787816   system-to-system 11.151264
%%       OSSKN page 1 (8 systems): inside 8.786452   system-to-system 11.145808
%%     Each page is one force, but the STAFF spring's compress strength is 1, not the 2 the
%%     prediction borrowed from JSK:
%%       OSSK  (9 - 8.787816) / 1 = 0.212184 == (12 - 11.151264) / 4 = 0.212184
%%       OSSKN (9 - 8.786452) / 1 = 0.213548 == (12 - 11.145808) / 4 = 0.213548
%%     ⇒ ★ THIS PAIR IS NOT SPACED BY THE SPEC JSK IS. JSK's staves are in a PianoStaff, so
%%     Axis_group_interface::calc_maybe_pure_staff_staff_spacing finds a staff-grouper and
%%     hands back StaffGrouper.staff-staff-spacing — basic 9, minimum 7, stretchability 5,
%%     so compress strength = 9 - 7 = 2 (axis-group-interface.cc:1007-1027 is the lookup,
%%     define-grobs.scm:3352-3355 the values). These two staves have NO grouper, so the same
%%     function falls through to the VerticalAxisGroup's own default-staff-staff-spacing —
%%     basic 9, minimum 8, padding 1, NO stretchability declared — and compress strength is
%%     9 - 8 = 1 (define-grobs.scm:4237-4239). The measured 1 IS that fall-through, and an
%%     ossia takes it because an ossia is a bare `\new Staff` and not a group.
%%
%%     ⇒ THE FORK, both branches written before the number existed:
%%       inside < 9 with (b) holding ⇒ the ossia's spring is an ordinary staff spring in
%%         LilyPond's chain, Lily#'s skip is a DEFECT, and the port has a measured target.
%%         ★ THIS IS THE BRANCH THAT FIRED.
%%       inside == 9.000000 on a page whose system springs did compress ⇒ LilyPond holds it
%%         rigid too, the skip is a PORT, and the open item closes without being ported.
%%
%%     ⇒ ★★ AND THE SAME DUMP NAMES A SECOND DEFECT THE PAIR AT REST COULD NOT SEE. Lily#
%%     already ports default-staff-staff-spacing exactly — StaffSpacingParameters
%%     .DefaultStaffStaff is 9 / 8 / 1 / absent, and its own remarks say "the COMPRESS
%%     strengths do differ (ideal - minimum-distance = 1 here against 2.5), which a
%%     compressed page would see — no corpus point measures that yet". This is that point.
%%     But MultiStaffLayouter overrides the spec to sp.StaffStaff — the GROUPED one —
%%     whenever either side is an ossia (:130-131, :222-223). At force 0 the two specs agree,
%%     because only the basic-distance 9 is ever read; on this page they cannot, because one
%%     minimum is 7 and the other is 8. ⇒ Dropping the spring skip is NOT the whole port:
%%     the ossia pair must also stop taking the grouper's spec.
%%
%%     ⚠️ READ PAGE 1 ONLY, and no entry here may read another. The LAST page of each book
%%     prints the PREVIOUS page's force rather than one of its own: OSSKN's page 3 carries
%%     ONE system over 144 units of foot slack and still reads 8.728721719, page 2's number
%%     to nine digits, and OSSK's 7-system last page reads page 1's 8.787815898. Whatever
%%     that is — the systems of an unjustified last page are evidently not re-solved — it is
%%     not a spring solution, and a point that read it would be pinning an artefact.
%%
%%     ⚠️ `max-systems-per-page = #8` is what the breaker picks unaided on BOTH books
%%     (measured with no paper block at all before it was pinned). It is written down for the
%%     reason JSK's is: so the entries stay measurements of the spring and cannot silently
%%     become measurements of the page breaker.
\book {
  \probeTag "OSSK"
  \paper { max-systems-per-page = #8 indent = 0 }
  \score {
    \new Staff = "main" <<
      \new Voice { \repeat unfold 120 { g'4 a' g' a' } }
      \new Staff \with {
        \remove "Time_signature_engraver"
        alignAboveContext = "main"
        fontSize = #-3
        \override StaffSymbol.staff-space = #(magstep -3)
        \override StaffSymbol.thickness = #(magstep -3)
      } { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

%% OSSKN — THE CONTROL: the same nesting, the same alignAboveContext, the same paper, a
%%     FULL-SIZE staff. One word apart from OSSK, exactly as OSSUN is from OSSU.
\book {
  \probeTag "OSSKN"
  \paper { max-systems-per-page = #8 indent = 0 }
  \score {
    \new Staff = "main" <<
      \new Voice { \repeat unfold 120 { g'4 a' g' a' } }
      \new Staff \with {
        \remove "Time_signature_engraver"
        alignAboveContext = "main"
      } { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

%% NTL — THE CONTROL. Identical to TABL except that the staff is an ordinary Staff, so it is
%%     the same music on the same paper with the same page springs, and its reading is the
%%     number TABL is compared against. Carrying it rather than leaning on book L (which is
%%     the same quantity but different music and different paper) is what makes the pair able
%%     to say that a tab staff changed nothing on LilyPond's side.
\book {
  \probeTag "NTL"
  \paper { indent = 0 ragged-bottom = ##t }
  \score { \new Staff { \repeat unfold 24 { g4 a g a } } }
}

%% ROWB / ROWH — AN INDEPENDENT LYRICS ROW STANDING BETWEEN TWO STAVES, and the pair that
%%     says whether Lily#'s decline over that arrangement is a property of the SYSTEM it
%%     stands on or of the SCORE. Every book above holds the arrangement constant down the
%%     page, so none of them can tell the two apart.
%%
%%     ⚠️ WHY A ROW AND NOT A NOTE-BOUND VERSE. Book LYRB already puts a \lyricsto line
%%     between two staves and its entries are exact. To Lily# those are two different
%%     things: a note-bound verse hangs off its staff and is not an element of the
%%     alignment at all, while a bare row lays out as a staff-like band and IS one
%%     (Staff.IsLyricsTextRow). LayoutEngine.ClassifySystem calls a row between two
%%     spaceable staves UNMODELLED, and BuildLooseChainEnds' `return null` on that flag
%%     leaves the WHOLE SCORE's chain unbuilt. LilyPond has one model for both spellings
%%     (book LYRR's identity), so its side of ROWB is LYRB's number and any difference
%%     Lily# shows between them is entirely Lily#'s.
%%
%%     THE PAIR IS ONE VARIABLE: ROWH is ROWB with the LOWER staff silent from system 1 on
%%     and \RemoveAllEmptyStaves declared, so the row is BETWEEN two staves on system 0 and
%%     BELOW the only staff on every system after it. Same music otherwise, same paper,
%%     same 19 breaks carried by the top staff (a break belongs to the score, not a staff —
%%     LYRHKG's header).
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       (a) ROWB staff -> row = 4.027851, LYRB's number to six digits. The arrangement is
%%           LYRB's and the spelling is the one LilyPond does not read (LYRR).
%%       (b) ROWB staff -> staff INSIDE a system = 9.000000, staff-staff-spacing.
%%       (c) ROWB page 1 = 8 staves, four systems of two.
%%       (d) ROWH system 0 = 4.027851 as well: that system's alignment is ROWB's, unchanged.
%%       (e) ROWH systems 1 and 2 = 5.500000, LYRR's and LYRC's number — one loose line in a
%%           12.000000 room reaches its ideal.
%%       (f) ROWH system 3 (the last on the page, whose chain runs to the page edge instead
%%           of to a next system) = 5.500001, LYRM's second reading.
%%       (g) ROWH page 1 = 5 staves, 2 + 1 + 1 + 1.
%%       (h) ROWH staff -> staff on system 0 = 9.000000, as ROWB.
%%     (falsifier for (a)/(d): anything other than LYRB's number, which would mean the
%%      association DOES reach the vertical spacing and LYRR's identity is regime-bound —
%%      the pair would then be measuring two arrangements rather than one.)
%%     (falsifier for (e): 4.027851 there, which would mean the removal did not happen and
%%      the row is still between two staves on those systems; the book would be ROWB again
%%      under another tag and could say nothing about granularity.)
%%
%%     MEASURED — ALL EIGHT HELD, which makes this pair a check on the port rather than a
%%     discovery (HANDOFF 5.0: a prediction that lands is the collation).
%%       ROWB  page 1: 4 systems, 8 staves.  staff -> row = 4.027851 on every system.
%%                     staff -> staff INSIDE = 9.000000.  system-to-system = 12.000000.
%%       ROWH  page 1: 4 systems, 5 staves (2 + 1 + 1 + 1).
%%                     staff -> row = 4.027851, 5.500000, 5.500001 — one reading per system,
%%                     and the THREE VALUES ARE THE POINT: system 0 still has the row between
%%                     two staves, systems 1 and 2 have it below the only staff with another
%%                     system under them, and system 3's chain runs to the page edge.
%%                     staff -> staff INSIDE (system 0) = 9.000000.
%%                     pages 2 and 3 read 5.500000 and 5.500001 only — no lower staff
%%                     survives there at all.
%%
%%     ★★★ SO LILYPOND FORKS BY SYSTEM AND NOT BY SCORE, and it does so inside ONE book:
%%     4.027851 and 5.500000 stand seven staff spaces apart on the same page of the same
%%     score. Lily#'s `if (alignment.UnmodelledRow) return null;` is a `return` out of the
%%     METHOD, so one system of this shape takes the chain away from all twenty. That is the
%%     quantity ROWH carries and ROWB is the control for: ROWB has the arrangement on EVERY
%%     system, so a per-system decline and a per-score decline read the same on it, and only
%%     the pair separates them.
%%     ⚠️ ROWB IS EXPECTED TO STAY DIVERGENT. Its arrangement — a row strictly between two
%%     spaceable staves — is the one this port does not model at all (SystemAlignment.
%%     UnmodelledRow's own remark), so narrowing the bail-out cannot close it. What the
%%     narrowing closes is ROWH's systems 1..3, and ROWB is what proves the narrowing did
%%     not quietly widen into the arrangement it has no formula for.
\book {
  \probeTag "ROWB"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff {
        \repeat unfold 19 { \repeat unfold 3 { g'4 a' g' a' } \break }
        \repeat unfold 3 { g'4 a' g' a' }
      }
      \new Lyrics \lyricmode { \repeat unfold 240 { no4 } }
      \new Staff { \repeat unfold 60 { g'4 a' g' a' } }
    >>
  }
}

\book {
  \probeTag "ROWH"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff {
        \repeat unfold 19 { \repeat unfold 3 { g'4 a' g' a' } \break }
        \repeat unfold 3 { g'4 a' g' a' }
      }
      \new Lyrics \lyricmode { \repeat unfold 240 { no4 } }
      \new Staff \with { \RemoveAllEmptyStaves } {
        \repeat unfold 3 { g'4 a' g' a' }
        \repeat unfold 57 { r1 }
      }
    >>
  }
}

%% ROWV / ROWVH — THE SAME PAIR WITH A SECOND VERSE, and it exists because ROWB/ROWH
%%     MEASURED NOTHING. Lily# reads 5.500000 on ROWH's systems 1..3 and LilyPond reads
%%     5.500000 there too, so the pair is EXACT on the very systems it was built to
%%     separate — not because the chain is solved but because a chain that is never solved
%%     sits at max(min, ideal) and ONE loose line in a 12.000000 room relaxes to that same
%%     ideal. HANDOFF 5.2.1 (4): exact can mean "that regime does not move".
%%
%%     ⇒ THE FIX IS A SECOND VERSE, which is the one variable that takes the solved answer
%%     off the ideal: two loose lines no longer fit in the room the system spring keeps, so
%%     LilyPond solves at a NEGATIVE force and the first line drops to its ink floor
%%     (book LYRV's header, and lyrics.two-staff.two-verse.staff-to-lyric carries it). An
%%     engine that declines to solve still reads 5.500000 there, and now that is wrong by
%%     1.762110 rather than right by accident.
%%
%%     PREDICTIONS, written before running (HANDOFF 5.0-2):
%%       (i)  ROWV  staff -> verse 1 = 3.737890, verse step = 2.800000, staff -> staff
%%            INSIDE = 11.073064, page 1 = 8 staves — book LYRBV's four numbers, since LYRR's
%%            identity says LilyPond does not read the spelling.
%%       (j)  ROWVH system 0 = the same three; its alignment is ROWV's, unchanged.
%%       (k)  ROWVH systems 1 and 2 = 3.737890 with step 2.800000 — LYRV's inner systems.
%%       (l)  ROWVH system 3, whose chain runs to the page edge, relaxes to 5.500001.
%%       (m)  ROWVH page 1 = 5 staves, 2 + 1 + 1 + 1, as ROWH.
%%     (falsifier for (k): 5.500000 there, which would mean two verses DO fit in the room on
%%      this book and the pair measures nothing again — the next variable to try would then
%%      be a third verse, not a different guard.)
\book {
  \probeTag "ROWV"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff {
        \repeat unfold 19 { \repeat unfold 3 { g'4 a' g' a' } \break }
        \repeat unfold 3 { g'4 a' g' a' }
      }
      \new Lyrics \lyricmode { \repeat unfold 240 { no4 } }
      \new Lyrics \lyricmode { \repeat unfold 240 { no4 } }
      \new Staff { \repeat unfold 60 { g'4 a' g' a' } }
    >>
  }
}

\book {
  \probeTag "ROWVH"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff {
        \repeat unfold 19 { \repeat unfold 3 { g'4 a' g' a' } \break }
        \repeat unfold 3 { g'4 a' g' a' }
      }
      \new Lyrics \lyricmode { \repeat unfold 240 { no4 } }
      \new Lyrics \lyricmode { \repeat unfold 240 { no4 } }
      \new Staff \with { \RemoveAllEmptyStaves } {
        \repeat unfold 3 { g'4 a' g' a' }
        \repeat unfold 57 { r1 }
      }
    >>
  }
}


%% ROWA / ROWAC — A CHORD ROW THAT IS NOT ON EVERY SYSTEM, and the pair that isolates it.
%%
%%     THE REPORT.  The user read a delivered picture and said the gap between systems 2
%%     and 3 looked longer than the one between 1 and 2.  It is: Lily# reads 12.000000 and
%%     16.970000 between refpoints.  Their book (U6) spells
%%
%%         chords prog as names / staff melody / lyrics verse sings melody / staff melody
%%         form main { A |: B :| A "A2" }        and `chords prog` HAS ONLY SECTION A
%%
%%     so the chord row is on systems 1 and 3 and absent from system 2.  These two books
%%     carry that arrangement and nothing else.
%%
%%     ⚠️ WHY LYRMC DOES NOT ALREADY COVER THIS.  LYRMC's row is on EVERY system, and both
%%     of its gaps read 12.000000 in BOTH engines -- it is exact and it has been exact
%%     since the row was ported into the chain.  "No row -> row" is an arrangement the
%%     ledger has never carried, and it is the only one that diverges.  An entry that is
%%     exact on every arrangement it spells is not a guard against the arrangements it
%%     does not spell (HANDOFF bone 2, 2026-08-25: an exclusion written as "no fixture
%%     reaches it" is a fact about the corpus, not about the geometry).
%%
%%     ⚠️ AND LYRMC DIFFERS IN A SECOND WAY, so it could not be edited into this: its
%%     lyrics hang UNDER the system's last staff.  U6 puts them BETWEEN the two staves,
%%     which is the arrangement 2026-08-25 ported (the run through a bare row).  These
%%     books keep U6's placement.
%%
%%     THE PAIR, and it is the strongest shape there is (HANDOFF 5.0): LilyPond is the
%%     IDENTITY side.  ROWA has the row on systems 1 and 3; ROWAC is ROWA with the row on
%%     system 2 as well and NOTHING else touched -- same three systems, same 12 bars, same
%%     48 syllables, same paper, same pitches.  LilyPond does not widen a system gap for a
%%     chords row (it puts the row INSIDE the room, alongside the lyrics, which is what
%%     LYRMC's own header established term by term), so every gap in BOTH books should
%%     read the same 12.000000.  With the LilyPond side flat, whatever Lily# does with the
%%     difference IS the defect, in staff spaces, with no subtraction of engines.
%%
%%     THE FOUR READINGS, named before running:
%%       ROWA   gap 1   system 1 -> 2   the NEXT system has NO row
%%       ROWA   gap 2   system 2 -> 3   the NEXT system HAS a row, the previous one does not
%%       ROWAC  gap 1   system 1 -> 2   both have a row
%%       ROWAC  gap 2   system 2 -> 3   both have a row
%%
%%     ★ PREDICTION FOR LILYPOND, written before running: 12.000000 on all four.  This is
%%     not a guess -- 2026-08-25 measured exactly this arrangement on 2.26.0 and got
%%     12.000000 for both of ROWA's gaps, and LYRMC already carries 12.000000 for the
%%     every-system case.  That book was never committed and is gone, which is why this
%%     one exists.  ⚠️ FALSIFIER, and it is the reason to re-run rather than copy the
%%     number across: anything other than 12.000000 means THIS book is not the book that
%%     was measured, and the reading to trust is this one, because this one is in the tree.
%%
%%     ★ PREDICTION FOR LILY#, written before running: 12.000000 / 16.970000 on ROWA and
%%     12.000000 / 12.000000 on ROWAC.  The second half is the half that carries the
%%     question.  2026-08-25 measured, on U6 itself, that adding chords to section B moves
%%     CreatePages' X-aware Distance() from 6.0949 to 19.0631 and moves toStaffFrame with
%%     it, leaving the gap at 12.000000 -- THE SAME BAND CANCELS.  So the defect is not
%%     "the band is counted"; it is that the band is counted twice on one pair and once on
%%     the other, and the pair that cancels is the pair whose PREVIOUS system also has a
%%     row.  ⚠️ FALSIFIER: if ROWAC's gaps are not both 12.000000, the cancellation is not
%%     about the previous system and the arithmetic below is describing something else.
%%
%%     ★ THE TERM IS ALREADY NAMED (2026-08-25, on U6): BandUp is 0.0000 on both pairs and
%%     is NOT involved.  What drives it is CreatePages' X-aware Distance(), 26.1075 on
%%     ROWA's second pair, which becomes staffToStaff = 26.1075 - 10.1335 = 15.974 and
%%     then max(12, 15.974 + 1) = 16.974.  ⚠️ That is 16.974 against the 16.970 the picture
%%     reads; the four-micron difference is not accounted for and should not be smoothed
%%     over -- it is a reading off a delivered picture against a reading off a probe.
%%
%%     ⚠️ THE PITCHES ARE DELIBERATELY INSIDE THE STAFF (g'/a' on both staves, LYRM's
%%     melody).  The row-to-staff spring binds on the staff's UP SKYLINE AT THE CHORD'S x
%%     (LYRMC's m3), so a note poking above the staff would put a notehead's ink into the
%%     quantity being measured.  Nothing here needs that term, and leaving it out means a
%%     residual cannot hide in it.
%%
%%     ⚠️ THE BREAKS ARE EXPLICIT.  Which bars share a system decides which system has a
%%     row, so it cannot be left to the breaker: a re-measure on another machine, or after
%%     a font change, has to get the same three systems or it is not the same book.
\book {
  \probeTag "ROWA"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode {
        \repeat unfold 4 { c1 } \repeat unfold 4 { s1 } \repeat unfold 4 { c1 } } }
      \new Staff { \new Voice = "mel" {
        \repeat unfold 4 { g'4 a' g' a' } \break
        \repeat unfold 4 { g'4 a' g' a' } \break
        \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff { \repeat unfold 12 { g'4 a' g' a' } }
    >>
  }
}

%% ROWAC — THE CONTROL.  ROWA with system 2's four bars carrying chords too, and nothing
%%     else changed.  See ROWA's header for what the pair decides.
\book {
  \probeTag "ROWAC"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode {
        \repeat unfold 4 { c1 } \repeat unfold 4 { c1 } \repeat unfold 4 { c1 } } }
      \new Staff { \new Voice = "mel" {
        \repeat unfold 4 { g'4 a' g' a' } \break
        \repeat unfold 4 { g'4 a' g' a' } \break
        \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff { \repeat unfold 12 { g'4 a' g' a' } }
    >>
  }
}


%% ROWM / ROWMN — THE SAME ARRANGEMENT WITH A SECTION MARK, and the pair that replaces the
%%     one ROWA/ROWAC was built for.
%%
%%     ⚠️⚠️ ★★★ READ THE BLOCK ABOVE ROWMX FIRST (2026-08-25, session 253).  These books were
%%     written `\mark \markup \box' -- a REHEARSAL MARK -- against a Lily# twin that engraves
%%     a SECTION LABEL, and LilyPond gives the two different break-align-symbols.  They now
%%     write \sectionLabel, and re-taking the family on 2.26.0 turned ROWM's and ROWMA's
%%     gap 2 from 12.563793 into a flat 12.000000.  ⇒ EVERY "0.563793" AND EVERY "the mark is
%%     lifted" IN THE HEADERS BELOW IS A READING OF THE OLD SPELLING.  It is correct about
%%     LilyPond and no longer describes what these books measure.
%%
%%     WHY THESE EXIST.  ROWA/ROWAC were built to carry the fifth report and they came out
%%     EXACT on both sides -- LilyPond 12.000000 four times, Lily# 12.000000 four times.
%%     The falsifier ROWA's header wrote ("12.000000 would mean the book does not reproduce
%%     the report") fired, and the miss is the useful half: sweeping one variable at a time
%%     back toward the user's book found that the chord row is NOT the term.
%%
%%     MEASURED IN LILY# 2026-08-25, three systems, gap 1 = system 1->2, gap 2 = 2->3:
%%       no mark, chord row on systems 1 and 3   12.000000  12.000000
%%       no mark, chord row on every system      12.000000  12.000000
%%       MARK, chord row on systems 1 and 3      12.000000  16.188166
%%       MARK, chord row on EVERY system         12.000000  16.188166   <- identical
%%       MARK, NO chord row anywhere             12.000000  12.241073
%%       MARK, no lyrics, chord row on 1 and 3   12.000000  16.188166
%%       no mark, no chord row, no lyrics        12.000000  12.000000
%%     ⇒ NEITHER INGREDIENT DOES IT ALONE.  A mark on its own costs 0.241073; a chord row on
%%     its own costs nothing; the two together cost 4.188166.  ⇒ AND THE ALTERNATION IS NOT
%%     THE VARIABLE: the row on EVERY system reads the same 16.188166, which is what the
%%     ROWA/ROWAC pair was built to test and is the reading that refutes it.  The lyrics are
%%     not involved either.
%%
%%     ⚠️ WHAT IS STILL UNMEASURED, AND IT IS THE WHOLE POINT OF THESE TWO BOOKS: whether
%%     LilyPond widens that gap too.  Lily# reading 16.188166 is not a defect until LilyPond
%%     has been asked the same question with the same book.  The Lily# numbers above say
%%     WHERE to look; they do not say who is wrong.
%%
%%     THE PAIR: ROWMN is ROWM with the chord row taken out and NOTHING else changed -- same
%%     marks, same three systems, same 12 bars, same 48 syllables, same indent, same
%%     instrument names, same paper, same pitches.  So the difference between them is the
%%     chord row's cost ON A MARKED SYSTEM, which is the quantity Lily# puts at 3.947093
%%     (16.188166 - 12.241073) and LilyPond has never been asked about.
%%
%%     ★ PREDICTION FOR LILYPOND, written before running (HANDOFF 5.0): 12.000000 on all
%%     four readings, i.e. the mark costs nothing and the row costs nothing.  The reasoning
%%     is the one LYRMC's header established term by term -- a loose line is absent from the
%%     page's own chain, so system-system-spacing is whatever it would have been without it
%%     -- plus the fact that a RehearsalMark is a Score-level grob that LilyPond puts in the
%%     system's own vertical skyline rather than into the page's springs.
%%     ⚠️ FALSIFIER, and it is a real one: if LilyPond widens gap 2 in ROWM and not in ROWMN,
%%     then the mark and the row DO share a floor in LilyPond too, Lily#'s 16.188166 is the
%%     right SHAPE, and what is left is the size of it.  That outcome would move this from a
%%     defect to a calibration and the ledger entries must be written so that either answer
%%     is readable.
%%
%%     ⚠️ HAND-ADDED PARTS, NAMED (the exporter drops them, HANDOFF 5.0): `lysc ly` on the
%%     Lily# book emits the two staves, the instrument names, the indent, the \fixed c'
%%     octaves and the three \mark \markup \box lines; the ChordNames context and the Lyrics
%%     context are written here BY HAND because `lysc ly` warns "chord row 'prog' is not
%%     exported" and "lyrics row 'one' is not exported".  ⚠️ The exporter also duplicated the
%%     marks onto the LOWER staff; a mark is a Score-level event, so they are on the top
%%     staff only here.
\book {
  \probeTag "ROWM"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode {
        \repeat unfold 4 { c1 } \repeat unfold 4 { s1 } \repeat unfold 4 { c1 } } }
      \new Staff \with { instrumentName = "Melody" } { \new Voice = "mel" {
        \sectionLabel \markup \box "A" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "B" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "C" \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff \with { instrumentName = "Lower" } {
        \repeat unfold 12 { g'4 a' g' a' } }
    >>
    \layout { indent = 15\mm }
  }
}

%% ROWMN — THE CONTROL.  ROWM with the chord row taken out and nothing else changed.
\book {
  \probeTag "ROWMN"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff \with { instrumentName = "Melody" } { \new Voice = "mel" {
        \sectionLabel \markup \box "A" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "B" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "C" \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff \with { instrumentName = "Lower" } {
        \repeat unfold 12 { g'4 a' g' a' } }
    >>
    \layout { indent = 15\mm }
  }
}

%% ROWNN — THE TOP-SPRING CONTROL (2026-08-27, session 270).  ROWMN with the marks taken
%%     out and NOTHING else changed: same two staves, same 48 syllables, same indent,
%%     instrument names and paper.  It exists for the FIRST-STAFF-REFPOINT pair, not for
%%     the gaps: ROWMN's first system's tallest element above its top staff is the boxed
%%     label, so first-staff-refpoint(ROWMN) - first-staff-refpoint(ROWNN) is what the
%%     engine charges the MARK against the top of the page — the reading the gap-second
%%     family cannot take, because a gap is a PAIR quantity and the top spring is not.
%%     WHY IT IS NEEDED: on the Lily# side the silhouette's mark reservation reads the
%%     drawn box since session 270, but the FIRST system's Y is priced by a separate
%%     SCALAR estimate (EnrichExtentsWithAnnotationProtrusions' mark arm, still the flat
%%     [mY - 0.7, mY + 2.1] envelope, 0.800000 over a boxed label's drawn top) — that arm
%%     was left unchanged precisely because no LP point refereed it.  This pair is that
%%     referee.
\book {
  \probeTag "ROWNN"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff \with { instrumentName = "Melody" } { \new Voice = "mel" {
        \repeat unfold 4 { g'4 a' g' a' } \break
        \repeat unfold 4 { g'4 a' g' a' } \break
        \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff \with { instrumentName = "Lower" } {
        \repeat unfold 12 { g'4 a' g' a' } }
    >>
    \layout { indent = 15\mm }
  }
}

%% ROWMA — THE THIRD READING: the mark with the chord row on EVERY system.  Lily# gives this
%%     the SAME 16.188166 as ROWM, which is the reading that refutes "no row -> row".  It is
%%     carried so that LilyPond's answer to the alternation question is in the tree too.
\book {
  \probeTag "ROWMA"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode { \repeat unfold 12 { c1 } } }
      \new Staff \with { instrumentName = "Melody" } { \new Voice = "mel" {
        \sectionLabel \markup \box "A" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "B" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "C" \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff \with { instrumentName = "Lower" } {
        \repeat unfold 12 { g'4 a' g' a' } }
    >>
    \layout { indent = 15\mm }
  }
}

%% ⚠️⚠️ ★★★ READ THIS BEFORE THE REST OF THE FAMILY: THE PAIR IS MISMATCHED IN THE GROB
%%     (2026-08-25, session 253).  Every book below writes its A/B/C as `\mark \markup \box',
%%     which is a REHEARSAL MARK.  The Lily# twin writes them as `form main { A B C }', which
%%     is a SECTION LABEL, and Lily# means it -- MusicMarkEngraver gives them
%%     outside-staff-priority 1450 and cites SectionLabel's own grob definition.
%%     LilyPond gives the two grobs DIFFERENT break-align-symbols:
%%       RehearsalMark  (staff-bar key-signature clef)   scm/define-grobs.scm RehearsalMark
%%       SectionLabel   (left-edge staff-bar)            scm/define-grobs.scm SectionLabel
%%     At a line start the first anchors on the CLEF and the second on the LEFT EDGE, so only
%%     the rehearsal mark ever stands over the first chord -- and standing over the chord is
%%     the whole of the mechanism this header goes on to name.
%%
%%     ⇒ FIXED THE SAME DAY: THESE SIX BOOKS NOW WRITE \sectionLabel, and every LilyPond
%%     number in this family was re-taken on the canonical 2.26.0
%%     (C:\bin\lilypond-2.26.0, the path Measure-LilyPondPageGeometry.ps1 defaults to).
%%
%%     MEASURED 2.26.0, system-to-system (last staff -> next first), the same reading the
%%     ledger's gap-first / gap-second take -- BOTH SPELLINGS, one variable moved:
%%       book     \mark (was)              \sectionLabel (is)
%%       ROWMN    12.000000                12.000000
%%       ROWM     12.000000, 12.563793     12.000000   (flat: no second number at all)
%%       ROWMA    12.000000, 12.563793     12.000000
%%       ROWMX    12.000000                12.000000
%%       ROWMZ    12.000000                12.000000
%%     Systems per page is 3 in every book under both spellings, so the count entries do not
%%     move.  => WITH THE GROB THE TWIN ACTUALLY ENGRAVES, LILYPOND CHARGES THE CHORD ROW
%%     NOTHING, ANYWHERE.  The 0.563793 was the REHEARSAL MARK's, and only its.
%%
%%     AND THE GROB DUMP SAYS WHY, TO SIX DIGITS.  Under \mark the RehearsalMark's X span is
%%     [11.900827, 14.619250] on system 1 and [3.365000, 6.083424] on system 3, while the
%%     Clef's is [9.335827, 11.900827] and [0.800000, 3.365000] -- the mark's LEFT EDGE IS
%%     THE CLEF'S RIGHT EDGE, exactly, in both.  That is (staff-bar key-signature clef)
%%     resolving to the clef at a line start, and it is what carries the mark across to the
%%     first chord at x 5.800000 on system 3.  A SectionLabel anchors on `left-edge' and
%%     never gets there.  Lily#'s own label stands at x [0.300000, 2.407165] on system 3 and
%%     [8.835827, 10.908849] on system 1 -- 3.065000 left of the rehearsal mark in BOTH, i.e.
%%     at the left edge, which is where LilyPond puts a SectionLabel too.
%%
%%     ⚠️ WHAT THE PARAGRAPHS BELOW STILL SAY CORRECTLY, and it is worth keeping: LilyPond
%%     DOES lift a mark over a chord that stands under it in X, by the mark's own
%%     outside-staff-padding (0.460000, lily/axis-group-interface.cc:44), and it does it by
%%     RE-PARENTING the grob into the extremal line's VerticalAxisGroup
%%     (Side_position_interface::move_to_extremal_staff, side-position-interface.cc:510-563;
%%     the line is chosen by Staff_grouper_interface::get_extremal_staff,
%%     staff-grouper-interface.cc:31-56, which intersects the grob's X extent widened by 1.0
%%     with each line's own and tests neither is_spaceable nor for a StaffSymbol).  Lily#
%%     has none of that.  ⚠️ BUT NOTHING IN THIS FAMILY MEASURES IT ANY MORE: a book that
%%     does needs a REHEARSAL MARK on the Lily# side too, and Lily#'s Rehearsal X is wrong
%%     for a different reason (MusicMarkEngraver.CalculateXPosition anchors Rehearsal and
%%     SectionLabel BOTH on Indent + 0.3; LilyPond anchors only the second one there).
%%     ⇒ THE ORDER, if that island is ever opened: fix Lily#'s Rehearsal X first, then open
%%     a @mark-spelled pair, THEN port the lift.  Porting the lift first has nothing to
%%     observe it.
%%
%% ROWMX / ROWMY -- THE TWO CONTROLS THAT SAY WHAT LILYPOND IS ACTUALLY CHARGING FOR, and
%%     the reading that turns ROWM's 0.563793 from a number into a mechanism.
%%
%%     ⚠️⚠️ EVERYTHING FROM HERE TO THE END OF THIS HEADER WAS MEASURED ON THE \mark SPELLING
%%     THESE BOOKS NO LONGER USE.  It is kept because it is a correct and hard-won reading of
%%     what LilyPond does with a REHEARSAL MARK, and because it is the evidence that the
%%     0.563793 was never the row's.  As written now (\sectionLabel) every book below reads a
%%     flat 12.000000 and the two controls have nothing left to separate -- they are kept as
%%     controls for the row itself, which is what they always were on the Lily# side.
%%     See the block above for the re-measurement and for what would be needed to measure the
%%     lift again.
%%
%%     ROWM's gap 2 is 12.563793 and ROWMN's is 12.000000, so the chord row costs 0.563793 on
%%     a marked pair.  ⚠️ THAT SENTENCE IS STILL AMBIGUOUS: "the row" can mean the row
%%     EXISTS, or it can mean ink OF THE ROW STANDS UNDER THE MARK.  ROWM cannot tell the two
%%     apart -- its mark spans x [3.365000, 6.083424] and its first chord spans
%%     x [5.800000, 7.677882], so they overlap and BOTH readings predict the same number.
%%
%%     ROWMX keeps the row and moves its first chord one bar later: the third system's row is
%%     `s c c c`, and nothing else changes.  ROWMY keeps all four chords and shoves them 40 ss
%%     to the right.  Between them they vary x with the row held, and x with the CHORD COUNT
%%     held as well.
%%
%%     MEASURED 2.26.0 on 2026-08-26 -- gap 2 = system 2 -> 3, staff refpoint to staff refpoint:
%%       ROWMN   no row at all                        12.000000
%%       ROWM    row, first chord under the mark      12.563793
%%       ROWMX   row, first chord one bar later       12.000000
%%       ROWMY   row, all four chords shoved right    12.000000   (every gap in the book)
%%     => THE ROW'S EXISTENCE COSTS NOTHING.  Only ink standing under the mark costs anything,
%%     and ROWMY holds the count at four while moving it, so "three chords instead of four"
%%     cannot account for ROWMX either.
%%
%%     AND THE MECHANISM IS IN THE GROB DUMP, to six digits.  Ink above the THIRD system's top
%%     staff refpoint:
%%       ROWMN   mark [2.850000, 5.638444]                                    -- nothing above it
%%       ROWMX   mark [2.850000, 5.638444]   chord [3.000000, 4.998884] at x 30.357916
%%       ROWM    mark [5.458884, 8.247328]   chord [3.000000, 4.998884] at x  5.800000
%%     => THE MARK IS LIFTED ONLY WHEN THE TWO OVERLAP IN X, and its bottom then sits at the
%%     chord's ink top + 0.460000 EXACTLY (5.458884 - 4.998884).  The chord's own band never
%%     moves -- [3.000000, 4.998884] in every book that has one, ROWM included.
%%     => SO LILYPOND IS CHARGING THE PAGE FOR THE MARK'S NEW HEIGHT, NOT FOR THE ROW.  The
%%     first system is exempt for the same reason in every book here: it is indented, so the
%%     mark stands at x [11.900827, 14.619250], clear of a first chord at x 17.120827.
%%
%%     ⚠️ AND THIS IS WHY A TERM-BY-TERM COMPARISON AGAINST THE SYSTEM ORIGIN CANNOT CLOSE.
%%     The origin is not a geometric feature of the printed system: it is the
%%     VerticalAlignment's own zero, i.e. where the alignment's FIRST element sat BEFORE
%%     Page_layout_problem re-spaced the staves and distribute_loose_lines re-placed the loose
%%     ones.  PROOF, from this file's own dump: across ROWMN's three systems, which are the
%%     same shape, the origin stands 5.594098 / 5.555286 / 5.638444 above the top staff --
%%     three numbers for one shape -- and it reads EXACTLY 0.000000 on the one element the
%%     spacer cannot move, ROWM's system 2, whose first alignment element is an EMPTY
%%     ChordNames VerticalAxisGroup.  => NOTHING MAY BE PORTED TO IT.  Every gap in this file
%%     is staff refpoint to staff refpoint, which is origin-free, and those close exactly.
%%
%%     ⚠️ ROWMY CARRIES NO LEDGER ENTRY and ROWMX carries three.  ROWMY needs a per-grob
%%     X-offset override, for which Lily#'s language has no spelling, so there is no mirror to
%%     measure it against; and the override widens the score enough that LilyPond breaks it
%%     into FOUR systems rather than three, so its gaps are not index-comparable with the rest
%%     of the family.  It is carried because it is the reading that holds the chord COUNT
%%     fixed, which ROWMX alone cannot do, and because deleting it would leave only prose
%%     saying so (HANDOFF 5.2.1 (4)).
\book {
  \probeTag "ROWMX"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode {
        \repeat unfold 4 { c1 } \repeat unfold 5 { s1 } \repeat unfold 3 { c1 } } }
      \new Staff \with { instrumentName = "Melody" } { \new Voice = "mel" {
        \sectionLabel \markup \box "A" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "B" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "C" \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff \with { instrumentName = "Lower" } {
        \repeat unfold 12 { g'4 a' g' a' } }
    >>
    \layout { indent = 15\mm }
  }
}

%% ROWMY -- the same book with all four chords shoved right instead.  See ROWMX above.
\book {
  \probeTag "ROWMY"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames \with { \override ChordName.X-offset = #40 } { \chordmode {
        \repeat unfold 4 { c1 } \repeat unfold 4 { s1 } \repeat unfold 4 { c1 } } }
      \new Staff \with { instrumentName = "Melody" } { \new Voice = "mel" {
        \sectionLabel \markup \box "A" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "B" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "C" \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff \with { instrumentName = "Lower" } {
        \repeat unfold 12 { g'4 a' g' a' } }
    >>
    \layout { indent = 15\mm }
  }
}

%% ROWMZ -- THE ROW WITH NOTHING IN IT ON THE MARKED SYSTEM, and the reading that separates
%%     Lily#'s NOMINAL BAND from the row's ink.  ROWM with one variable moved: the third
%%     system's four chords are replaced by four spacers, so the ChordNames context still
%%     exists, still prints on system 1, and has NO INK AT ALL where the mark stands.
%%
%%     WHY IT IS NEEDED.  ROWMX moved the row's ink sideways and ROWMY moved it further; both
%%     say "ink away from the mark costs nothing".  Neither of them removes the ink, so
%%     neither can tell "LilyPond charges only for ink under the mark" apart from "LilyPond
%%     charges for the row's WIDTH-weighted presence and ROWMX simply moved most of it away".
%%     ROWMZ removes it, holding the context, the paper, the break, the marks and the lyrics.
%%
%%     ⚠️ AND IT IS THE ONLY BOOK IN THE TREE THAT CAN SEE Lily#'s TextRowHeight.  Measured in
%%     Lily# 2026-08-25 (this session), gap 2 by spelling of the third system's row:
%%       row absent from the score entirely     12.241073   (= ROWMN, the mark's own 0.241073)
%%       row present, four spacers               14.741073   (+2.500000 exactly)
%%       row present, four chords                16.188166   (+3.947093)
%%       row present, four TALLER chords         17.211417   (+4.970344)
%%     => Lily# charges an EMPTY row exactly 2.500000, which is
%%     MultiStaffLayouter.TextRowHeight, a LILYSHARP-OWN constant that until now had no
%%     observer anywhere in the corpus (its sibling TextRowVerseSpacing says as much in its
%%     own remark).  The three inked readings differ by exactly the ink, so the charge is
%%     max(nominal band, the row's own step) -- and it is X-BLIND, which is what ROWMX proves
%%     on the Lily# side by reading the same 16.188166 as ROWM.
%%
%%     ★ PREDICTION FOR LILYPOND, written before running (HANDOFF 5.0): 12.000000 on gap 2,
%%     the same as ROWMX and ROWMY and ROWMN.  The reasoning is the mechanism ROWMX's header
%%     established -- LilyPond charges the page for the MARK'S lifted height and lifts the
%%     mark only where the two overlap in X -- and an empty row has no ink to lift it with.
%%     ⚠️ FALSIFIER, and it is a real one: if LilyPond reads MORE than 12.000000 here, then a
%%     declared-but-empty ChordNames line does cost the page something, "the row's existence
%%     costs nothing" is wrong, and ROWMX's 12.000000 stops being readable as an X statement.
\book {
  \probeTag "ROWMZ"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new ChordNames { \chordmode {
        \repeat unfold 4 { c1 } \repeat unfold 8 { s1 } } }
      \new Staff \with { instrumentName = "Melody" } { \new Voice = "mel" {
        \sectionLabel \markup \box "A" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "B" \repeat unfold 4 { g'4 a' g' a' } \break
        \sectionLabel \markup \box "C" \repeat unfold 4 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 48 { no } }
      \new Staff \with { instrumentName = "Lower" } {
        \repeat unfold 12 { g'4 a' g' a' } }
    >>
    \layout { indent = 15\mm }
  }
}

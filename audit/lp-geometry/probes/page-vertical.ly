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
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
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

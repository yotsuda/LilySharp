\version "2.24.4"
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
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
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
  \paper { paper-height = 123.0109\mm }
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

\version "2.24.4"
%% LP FIDELITY PROBE — page VERTICAL geometry (Page_layout_problem / the page breaker).
%%
%% The X probe (barline-spacing.ly) measures inside one system. This one measures the page:
%% how far below the paper edge the first system's ink starts, how far apart consecutive
%% systems sit, and how many of them the breaker puts on a page. Those are the quantities
%% HANDOFF.md 2-8 is about.
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

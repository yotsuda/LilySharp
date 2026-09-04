\version "2.26.0"
%% LP FIDELITY PROBE — THE HEAD OF A PAGE THAT CARRIES A TITLE (session 335, leg 5 → 336).
%%
%% WHY THIS PROBE EXISTS. Every page book in this corpus (page-vertical.ly and its children)
%% deliberately carries NO \header, so that the first system is a system and
%% `top-system-spacing` governs the top of the page. That was the right first measurement,
%% and it left one quantity unread on purpose: where the first staff lands when a TITLE
%% stands over it. Express Yourself (a user book: title + composer over staff + tab) put 8
%% systems on LilyPond's first page and 7 on Lily#'s, with the SAME line breaking and the
%% same page count; the two engines differed in the SIGN of the first page's force, and
%% about 3.5 of the 5 ss between them sat above the first system (HANDOFF §1 第335 ⑼).
%% No entry measured that band. These do.
%%
%% HOW LILYPOND BUILDS THE HEAD OF A TITLED PAGE — read, not assumed:
%%   * The book title is a paper-system PROB in the page's `lines`, with is-title #t
%%     (lily/paper-book.cc:570-580 Paper_book::get_system_specs wraps book_title()), and
%%     the stencil is ALIGNED TO ITS TOP: `title.align_to (Y_AXIS, UP)` at :443
%%     (Paper_book::book_title). So the prob's refpoint is the TOP of the title ink, and
%%     its Y-extent is (−depth . 0).
%%   * With a title first, the top spring is `top-markup-spacing` (basic 4, min 0,
%%     padding 1 — ly/paper-defaults-init.ly:81-83) rather than top-system-spacing
%%     (lily/page-layout-problem.cc:468-469 swaps the variable when the first line is a
%%     Prob). Its floor is padding + the ink above the refpoint, and that ink is 0 for a
%%     top-aligned stencil, so the floor loses to 4 by 3.
%%   * The spring from the title to the first system is `markup-system-spacing` (basic 5,
%%     padding 0.5, stretchability 30 — :70-72), chosen at page-layout-problem.cc:506-507
%%     (`last_system_was_title`), and its floor is the title's down-skyline against the
%%     system's up-skyline + 0.5 (:625-629 Page_layout_problem::append_system). On a
%%     one-staff system whose top ink under the title's X range is the staff line (2.05),
%%     that floor is depth + 2.05 + 0.5, and it BINDS whenever depth > 2.45 — i.e. for
%%     any real title.
%% The markup itself is ly/titling-init.ly:68-97 bookTitleMarkup: a \column with
%% baseline-skip 3.5 whose title line is \huge \larger \larger \bold (four steps up from
%% the 11pt text font) and whose composer stands on the poet/instrument/composer line at
%% text size.
%%
%% WHAT LILY# DOES INSTEAD (LayoutUtilities.CalculateHeaderHeight / CreateTopSystemSpring,
%% SharedRenderer.DrawHeader): the title is not a system. Its BASELINE is drawn at
%% MarginTop, so its ascender ink stands INSIDE the top margin, and only the ink below the
%% baseline (3.49 for a title, plus the composer's descender) is counted as `headerHeight`,
%% which enters the FLOOR of the ordinary top-system-spacing spring (header + ink above the
%% refpoint + padding 1 against basic 6). A title-plus-composer page therefore puts the
%% first staff at top-margin + max (6, 3.97 + 2.0 + 1) = top-margin + 6.97.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2):
%%   TTN (no header, the control): first-ref = 5.690551 + 6.000000 = 11.690551, the number
%%       page.first-staff-refpoint already pins on other music. Exact on both sides.
%%   TTL (title + composer): title Y-offset = 4.000000 exactly (top-markup-spacing's basic
%%       distance; the floor is 1.0 and loses). first-ref = 5.690551 + 4 + (depth + 2.55)
%%       with depth the title stencil's Y-extent length, read from the same dump. Lily#
%%       reads 12.660551, so the residual is −(depth + 0.55 + ... ) — a NEGATIVE number of
%%       several staff spaces, and it is the head of Express Yourself's missing 5.
%%   TTT (title only) and TTC (composer only): same construction with a shallower column;
%%       Lily# reads 5.690551 + max (6, 3.49×0.22 + 3) = 11.690551 for both (the floor
%%       loses), so the residual there is the WHOLE of LilyPond's band above 6.
%%   FALSIFIER: a title Y-offset that is not 4.000000, which would mean the floor binds
%%       (the stencil is not top-aligned, or the padding is not 1) and the reading above
%%       is of the wrong spring.
%%
%% ⚠️ THE STRINGS ARE THE USER BOOK'S. "Express Yourself" has ascenders (E, Y, l, f) and
%% descenders (p, y); "Madonna" has neither below the baseline. The depth of the column is
%% glyph-dependent, so the pair is filed on the strings that raised it rather than on
%% invented ones, and the .lys twins carry the same two strings.
%%
%% ⚠️ THE FONTS ARE PINNED (see page-vertical.ly's header): under -dbackend=svg the serif
%% falls back to this machine's fontconfig pick, and a title is nothing but serif text.
%%
%% ⚠️ tagline = ##f, and it is load-bearing for the LAST-page foot only: the tagline is a
%% footer markup and enters footer_height_, not the head. It is switched off so that the
%% dump's foot readings are of the music, and because Lily# prints none.
%%
%% ⚠️ ragged-bottom, so every spring is at its natural length and the readings are the
%% SPEC's (HANDOFF 5.3: do not mix regimes). 8 bars is one system.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe titled-page.ly -Prefix PROBET
%%
%% PROBET PAPER top-margin=… paper-height=…
%% PROBET PAGE <page> systems=<n>          (titles count as lines here, as in LilyPond)
%% PROBET SYS <page> <i> title=<0|1> y=<Y-offset> ext=(lo . hi) staff=(lo . hi)
%%            ink-top=<paper edge → topmost ink> ink-bottom=<paper edge → lowest ink>
%%            first-ref=<paper edge → first spaceable staff refpoint>
%%            last-ref=<paper edge → last spaceable staff refpoint>
%% first-ref / last-ref are top-margin + Y-offset − staff-refpoint-extent, the arithmetic
%% Measure-LilyPondPageGeometry.ps1 does (scm/page.scm:190 places the stencil at
%% −(Y-offset + top-margin)); for a title line staff=(0 . 0) and they are the refpoint.

#(define (probe-dump-titled layout pages)
   (let ((top (ly:output-def-lookup layout 'top-margin)))
     (format #t "PROBET PAPER top-margin=~a paper-height=~a\n"
             top (ly:output-def-lookup layout 'paper-height))
     (let loop ((ps pages) (n 1))
       (if (pair? ps)
           (let* ((page (car ps))
                  (lines (ly:prob-property page 'lines)))
             (format #t "PROBET PAGE ~a systems=~a\n" n (length lines))
             (let inner ((ls lines) (i 0))
               (if (pair? ls)
                   (let* ((sys (car ls))
                          (y (ly:prob-property sys 'Y-offset 0.0))
                          (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                          (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0)))
                          (title (if (equal? #t (ly:prob-property sys 'is-title)) 1 0)))
                     (format #t "PROBET SYS ~a ~a title=~a y=~a ext=(~a . ~a) staff=(~a . ~a) ink-top=~a ink-bottom=~a first-ref=~a last-ref=~a\n"
                             n i title y
                             (car ext) (cdr ext)
                             (car staff) (cdr staff)
                             (- (+ top y) (cdr ext))
                             (- (+ top y) (car ext))
                             (- (+ top y) (cdr staff))
                             (- (+ top y) (car staff)))
                     (inner (cdr ls) (1+ i)))))
             (loop (cdr ps) (1+ n)))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               ragged-bottom = ##t
               indent = 0
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBET BOOK ~a\n" tag)
                                      (probe-dump-titled layout pages)) } #})

music = { \repeat unfold 8 { c'4 d' e' f' } }

%% TTL — TITLE AND COMPOSER, the user book's frame.
\book {
  \probeTag "TTL"
  \header { title = "Express Yourself" composer = "Madonna" tagline = ##f }
  \score { \new Staff \music }
}

%% TTT — TITLE ONLY. The column has one line; the composer line of TTL is what this
%%       subtracts.
\book {
  \probeTag "TTT"
  \header { title = "Express Yourself" tagline = ##f }
  \score { \new Staff \music }
}

%% TTC — COMPOSER ONLY. Text-size serif with no descender: the shallowest band a header
%%       can make, and Lily#'s third CalculateHeaderHeight branch.
\book {
  \probeTag "TTC"
  \header { composer = "Madonna" tagline = ##f }
  \score { \new Staff \music }
}

%% TTN — THE CONTROL: the same music with no header at all, so the first line is a system
%%       and top-system-spacing governs. Must read 11.690551 like page.first-staff-refpoint.
\book {
  \probeTag "TTN"
  \header { tagline = ##f }
  \score { \new Staff \music }
}

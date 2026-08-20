\version "2.26.0"
%% LP FIDELITY PROBE — THE LYRIC BAND IN THE INTER-SYSTEM FLOOR, READ WITH X.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe lyric-band-x.ly -Prefix PROBEY
%%
%% THE DEFECT THIS MEASURES (HANDOFF 2D, opened session 218, unmeasured until now): Lily#'s
%% LyricReservationBelowSystem WALKS the block's skylines and then flattens the profile to a
%% SCALAR (its deepest point), and both page paths (CreatePages' single-page floor and
%% PageLayouter's chain) spread that scalar under EVERY X — the next system's tallest ink is
%% held off by the band even where the band has no ink. LilyPond never flattens:
%% build_system_skyline is handed Align_interface::get_minimum_translations
%% (page-layout-problem.cc:593-599), so the block is IN the system's down skyline at its
%% alignment minimum, and the inter-system spring's floor is up.distance(down, padding) —
%% an X-resolved minimum (page-layout-problem.cc:625-632).
%%
%% THE PAIR (HANDOFF 5.0-1): LBL and LBR are ONE variable apart — the same first system
%% (four bars, syllables under bar 1 only, all four with descenders), the same second
%% system's NOTE CONTENT (one bar of g''', three of a'), and the only difference is WHICH
%% bar carries the g''': bar 5 (leftmost X, straight under the band) in LBL, bar 8
%% (rightmost X, a system-width away from the band) in LBR. The spring's ideal and minimum
%% are taken away (the system-clef-floor recipe) so the gap IS the X-resolved floor plus
%% the shipping padding 1 — no 12.000000 to hide behind.
%%
%% LBS is the REAL-WORLD FACE on shipping spacing: the exact music of Lily#'s
%% SystemGap_ReadsARowsBandOnce pin (session 218 measured its machine-exported twin at
%% 12.000 in LilyPond against 14.571 in Lily#), re-measured here so the number the port
%% must land on is recorded in the ledger and not only in a test comment.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), mechanism first:
%%  * LBL — the g''' faces the band at the SAME X: gap ≈ band depth below the first
%%    system's refpoint (bottom line 2 + lyric drop ≈ 3.9 + descender ≈ 1, order 6.5-7)
%%    plus the g''' bar's reach above the second system's refpoint (head top ≈ 7)
%%    plus padding 1 ⇒ order 14-15. FALSIFIER: a reading equal to LBR's means the X did
%%    not matter and the whole probe is measuring something scalar.
%%  * LBR — the g''' faces the BARE staff under bar 4 and the band faces the a' bars:
%%    gap = max over X of the two lesser sums, order 10-11. THE FORK LBL − LBR (predicted
%%    2-4 ss) is the mechanism itself: an X-blind floor CANNOT split these two books.
%%  * Which side diverges (HANDOFF 5.0): Lily# today reads the SAME number on LBL and LBR
%%    (its scalar sees one band depth and one up-extent, both identical across the pair),
%%    so LBR is the diverging side — Lily# holds the g''' off a band it never crosses.
%%  * LBS — 12.000000 exactly: the band's X-resolved distance is under the shipping
%%    basic-distance 12, so the spring reads its ideal (that is what session 218 measured
%%    on the twin; this re-measures it inside the ledger's own probe).
%%  * All books: 2 systems, 1 page. A book that wraps differently is out of its regime.
%%
%% ⚠️ indent = 0 is load bearing (system-clef-floor.ly's reason): append_system SHIFTS each
%% system's skylines by its own indent before measuring, so a default-indented first system
%% would slide the band 8.5 ss right and the "disjoint" book would stop being disjoint by
%% construction.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEY PAPER top-margin=~a bottom-margin=~a paper-height=~a paper-width=~a output-scale=~a line-width=~a\n"
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
           (format #t "PROBEY PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEY SYS ~a ~a y=~a ext=(~a . ~a) staff=(~a . ~a) title=~a\n"
                           n i
                           (ly:prob-property sys 'Y-offset 0.0)
                           (car ext) (cdr ext)
                           (car staff) (cdr staff)
                           (if (equal? #t (ly:prob-property sys 'is-title)) 1 0))
                   (let* ((sg (ly:prob-property sys 'system-grob))
                          (align (if (ly:grob? sg)
                                     (ly:grob-object sg 'vertical-alignment)
                                     #f)))
                     (if (ly:grob? align)
                         (for-each
                          (lambda (g)
                            (format #t "PROBEY VAG ~a ~a rel=~a aff=~a ext=(~a . ~a)\n"
                                    n i
                                    (ly:grob-relative-coordinate g sg Y)
                                    (ly:grob-property g 'staff-affinity)
                                    (car (ly:grob-extent g g Y))
                                    (cdr (ly:grob-extent g g Y))))
                          (ly:grob-array->list (ly:grob-object align 'elements))))
                     ;; LyricText carries the band (its rel/ext/x-span verifies WHERE the band
                     ;; is, so disjointness is checked rather than assumed); NoteHead carries
                     ;; the g''' bar (same check on the other side of the gap); Clef because it
                     ;; is the one other tall thing standing at a line start.
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(LyricText Clef NoteHead))
                                        (format #t "PROBEY GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a)\n"
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

%% Both text faces are pinned (pedal-lyric-stack.ly's reason): the syllables ARE ink in a
%% binding pair here, so the face must be the one Lily# measures with.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEY BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% The shared first system: four bars, syllables (all with descenders) under bar 1 ONLY,
%% notes chosen to stay inside the staff so the band is the only deep ink.
firstSystem = { e'4 f' g' f' | e'4 e' e'2 | e'4 f' g' f' | e'2 e'2 | \break }
firstWords = \lyricmode { gyp jog pyx pug }

%% LBL — THE g''' OVER THE BAND. Bar 5 (leftmost X of system 2) carries the tall ink, which
%% faces the syllables of bar 1 at the same X. The X-resolved floor binds DEEP here.
\book {
  \probeTag "LBL"
  \paper {
    ragged-bottom = ##t
    indent = 0
    system-system-spacing.basic-distance = #0
    system-system-spacing.minimum-distance = #0
  }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \time 4/4
        \firstSystem
        g'''4 g''' g''' g''' | a'4 a' a' a' | a'4 a' a' a' | a'2 a'2 | } }
      \new Lyrics \lyricsto "mel" \firstWords
    >>
  }
}

%% LBR — THE g''' PAST THE BAND. The SAME notes, with the g''' bar moved to bar 8
%% (rightmost X), a system-width away from the band. One variable apart from LBL.
\book {
  \probeTag "LBR"
  \paper {
    ragged-bottom = ##t
    indent = 0
    system-system-spacing.basic-distance = #0
    system-system-spacing.minimum-distance = #0
  }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \time 4/4
        \firstSystem
        a'4 a' a' a' | a'4 a' a' a' | a'4 a' a' a' | g'''4 g''' g''' g''' | } }
      \new Lyrics \lyricsto "mel" \firstWords
    >>
  }
}

%% LBS — SHIPPING SPACING, the exact music of Lily#'s SystemGap_ReadsARowsBandOnce pin:
%% two 2-bar systems, syllables under every note. The band's X-resolved distance is under
%% basic-distance 12, so LilyPond reads the IDEAL — the number Lily#'s scalar floor
%% overshoots (14.571 measured, session 218).
\book {
  \probeTag "LBS"
  \paper {
    ragged-bottom = ##t
    indent = 0
  }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \time 4/4
        e''4 f'' g'' f'' | e''4 e'' e''2 | \break
        g''4 g'' f'' f'' | e''4 e'' e''2 | } }
      \new Lyrics \lyricsto "mel" { One two three four five six se -- ven
                                    Aa bb cc dd ee ff gg }
    >>
  }
}

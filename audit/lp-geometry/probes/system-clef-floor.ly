\version "2.26.0"
%% LP FIDELITY PROBE — the SYSTEM-TO-SYSTEM spring SITTING ON ITS SKYLINE FLOOR, with the
%% LINE-START CLEF as the ink that binds it.
%%
%% Run it with ../Measure-LilyPondPageGeometry.ps1 -Probe system-clef-floor.ly (about eighty
%% seconds — it is two books, not page-vertical.ly's sixty-two), or with
%% ../Measure-LilyPondProbe.ps1 -Probe system-clef-floor.ly to see the raw dump.
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% LILYPOND-REF: lily/page-layout-problem.cc:625-632 Page_layout_problem::append_system —
%%
%%     minimum_distance = up_skyline.distance (bottom_skyline_, skyline-horizontal-padding)
%%                        + padding;
%%     spring_copy.ensure_min_distance (minimum_distance);
%%
%% i.e. the distance between two systems' SKYLINES, plus system-system-spacing's padding, is a
%% FLOOR under the system-to-system spring. Lily# computes the same quantity in two places
%% (PageLayouter's page chain and LayoutEngine's content-sized single-page path), and NOTHING
%% in the ledger reaches either of them with the CLEF as the binding ink — because on shipping
%% paper it cannot:
%%
%%     clef up 3.776 + clef down 3.540 + padding 1 = 8.316  <  basic-distance 12
%%
%% so the spring reads its IDEAL and the skyline term leaves no trace at all. That is exactly
%% what `system.clef-bounded-distance` (book S in page-vertical.ly) records: 12.000000, exact
%% on both sides, and blind to everything this file is about. Two staves do not fix it either:
%% build_system_skyline (:1080-1127) raises the up skyline to the FIRST spaceable staff and the
%% down skyline to the LAST, so a system's own height is NOT in the distance and a plain
%% two-staff score gets the same 8.316.
%%
%% HOW THE FLOOR IS MADE TO BIND: by taking the spring's IDEAL away, not by adding ink.
%%
%%     system-system-spacing.basic-distance   = 0
%%     system-system-spacing.minimum-distance = 0
%%
%% The padding stays LilyPond's shipping 1, so what the dump prints IS
%% `up.distance(down) + 1` and nothing else — no ideal to subtract off, no note about which of
%% two terms won. Adding ink instead (a deep note, a bigger padding) was rejected for the
%% reason HANDOFF 5.0 gives about book S: the moment something else is deep enough to beat 12,
%% that something else is what the entry measures, and the clef's own profile goes back to
%% being invisible.
%%
%% THE MUSIC IS BOOK S'S, unchanged, so this is "an existing book with one line added" —
%% HANDOFF 5.0's recipe for a pair whose LilyPond side is nearly an identity. `a'` sits one step
%% BELOW the middle line, which is what makes its stem point UP: the head reaches 1.045 below
%% the middle, the stem goes the other way at 3.0 ABOVE it, and the staff's own lines at 2.05
%% are all that is left on either side. The clef reaches 3.540 down and 3.776 up, so it decides
%% BOTH profiles by a wide margin, and it is the only ink at the line start in any case.
%% ⚠️ Do NOT write this on the middle line: a note there takes a DOWN stem reaching 3.5, which
%% shadows the clef's 3.540 to within 0.04.
%%
%% ⚠️ `indent = 0` IS LOAD BEARING HERE, and not for the usual reason (HANDOFF 5.0 trap 3, "the
%% two sides of the pair must set the same page"). append_system SHIFTS each system's skylines
%% by that system's indent (:600-601) before measuring. At LilyPond's default 15mm indent the
%% FIRST system's clef stands about 8.5 staff spaces right of every other system's clef, so the
%% first gap would not be measuring clef against clef at all — it would pair the indented
%% system's clef against the next system's NOTES, and the entry would quietly become a
%% measurement of a stem tip.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2):
%%
%%   * SCF's system-to-system distance is the clef's profile against itself, plus 1.
%%     A skyline made of the glyph's BOX predicts 3.776000 + 3.540000 + 1 = 8.316000.
%%     A skyline made of the glyph's OUTLINE predicts LESS, because the G clef's deepest point
%%     (x = 1.84) and its highest (x = 2.228) are at DIFFERENT x, so two of them facing each
%%     other touch somewhere in between: skyline-binding.ly measured that pair at 7.210039
%%     (touching at x = 2.188), which predicts 8.210039 here. ⇒ the two hypotheses are
%%     0.105961 apart, which is the SAME 0.105961 the held clef-silhouette port is about.
%%     ⚠️ The horizon padding is 1.0 here and was not in that probe, so the outline figure may
%%     move; the BOX figure cannot, since a box is flat and dilating it horizontally changes
%%     nothing. A reading of exactly 8.316000 therefore falsifies the outline hypothesis
%%     outright, and any reading below it confirms the shape of the argument.
%%   * FALSIFIER — a reading of exactly 12.000000 means the floor did NOT bind and the paper
%%     edit did nothing; the entry would be a second copy of system.clef-bounded-distance.
%%   * SCC, the control, must read exactly 12.000000: the same music on shipping spacing, where
%%     the ideal wins. That is what makes the pair a FORK rather than one number — SCF and SCC
%%     differ in TWO paper values and nothing else, so LilyPond's difference between them is
%%     the mechanism itself.
%%   * Both books must print the SAME "first STAFF refpoint below edge": top-system-spacing is
%%     untouched, so a difference there means the edit reached further than intended.
%%
%% ⚠️ ragged-bottom, and the music is short (24 bars, one page) so the page keeps slack — a
%% FULL ragged page compresses anyway (HANDOFF 5.0 trap 7) and would read a force instead of
%% the floor. At force 0 a spring whose min exceeds its ideal returns exactly that min
%% (spring.cc:219-237 via the blocking force), which is the floor and nothing else.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

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
                        (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEV SYS ~a ~a y=~a ext=(~a . ~a) staff=(~a . ~a) title=~a\n"
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
                            (format #t "PROBEV VAG ~a ~a rel=~a aff=~a ext=(~a . ~a)\n"
                                    n i
                                    (ly:grob-relative-coordinate g sg Y)
                                    (ly:grob-property g 'staff-affinity)
                                    (car (ly:grob-extent g g Y))
                                    (cdr (ly:grob-extent g g Y))))
                          (ly:grob-array->list (ly:grob-object align 'elements))))
                     ;; The Clef rides along here for the reason this whole file exists: it is
                     ;; the ink the inter-system floor is made of, and `x` is its own span about
                     ;; the SYSTEM, so the two systems' clefs can be checked for overlap rather
                     ;; than assumed to overlap. (BarNumber too, because it is the other grob
                     ;; standing at a line start and the one that would silently take the clef's
                     ;; place at the top of the profile.)
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
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

%% The serif font is pinned for the reason page-vertical.ly's header gives: on the svg backend
%% LilyPond's `fonts.serif` falls back to whatever fontconfig resolves. Nothing in THIS file has
%% text in a binding pair, so it changes no number here — it is pinned so the next book added
%% to the file cannot inherit the bug.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% SCF — THE FLOOR. system-system-spacing has no ideal and no minimum left, so the spring's
%%     length IS ensure_min_distance's argument: the two systems' skyline distance plus the
%%     shipping padding of 1. The clef is the only ink at the line start and out-reaches
%%     everything else on both profiles, so what is printed is the G clef's own silhouette
%%     measured against a second copy of itself.
\book {
  \probeTag "SCF"
  \paper {
    ragged-bottom = ##t
    indent = 0
    system-system-spacing.basic-distance = #0
    system-system-spacing.minimum-distance = #0
  }
  \score { \new Staff { \repeat unfold 24 { a'4 a' a' a' } } }
}

%% SCC — THE CONTROL, and the pair's other fork. Identical in every respect except the two
%%     spacing numbers, so it reads the spring's IDEAL (12.000000) and says nothing about any
%%     skyline. Its job is to prove that SCF's reading is the FLOOR and not some property of
%%     this music or this paper: if both books printed the same number, the paper edit did
%%     nothing and SCF is not in the regime it claims to be in.
\book {
  \probeTag "SCC"
  \paper {
    ragged-bottom = ##t
    indent = 0
  }
  \score { \new Staff { \repeat unfold 24 { a'4 a' a' a' } } }
}

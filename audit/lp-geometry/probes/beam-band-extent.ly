\version "2.26.0"
%% LP FIDELITY PROBE — the BEAM's X extent in the vertical skyline: does a grob that reaches
%% into the gap at the beam's first COLUMN, but left of its first STEM, meet the beam?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe beam-band-extent.ly (three books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% A Beam's vertical skyline is its stencil (scm/define-grobs.scm Beam: vertical-skylines
%% from the stencil), i.e. the drawn beam, which runs from half a stem thickness outside the
%% first stem to the same outside the last (lily/beam.cc:631 calc_beam_segments). An UP stem
%% stands one attachment — 1.2392 on a black head — right of its column's left edge, so the
%% beam's skyline begins 1.174 right of the first note column's left edge. Lily#'s band
%% (SkylineBuilder.AddBeamsToSkyline) stood at the COLUMN ANCHORS until session 344 measured
%% it: an up-stem beam's band began one attachment too far left. Session 344's sweep moved
%% 93 of 920 books by 0.01–0.12 when the band was put at the drawn beam, and NONE of the 803
%% ledger points saw it — this probe is the point that does.
%%
%% THE FLOOR IS MADE TO BIND as tuplet-bracket-follow-beam.ly does it: default-staff-staff-
%% spacing loses basic-distance and minimum-distance, keeping the shipping padding 1, so the
%% gap IS the skyline distance + 1.
%%
%% THE BOOKS. Lower staff (bass): eight beamed eighths on c (C3, one step below the middle
%% line, stems UP, heads inside the staff) — the beam reaches above the staff's ink by its
%% stem length (face 3.05 above the middle); nothing else of the lower staff reaches up.
%% Upper staff (treble), two voices so the stem can be FORCED down:
%%   BXS — voice two's g'4 (G4, position −3) with its stem forced DOWN reaches 5.0 below
%%         the middle — the only thing reaching below the upper staff's ink further than its
%%         own treble clef (3.54) — at the FIRST column, i.e. at x = the lower beam's first
%%         column edge + 0.065 (a down stem stands at the head's left edge), which is LEFT
%%         of the lower beam's first stem (column edge + 1.174) and so left of the drawn beam.
%%         LilyPond: that stem meets the lower STAFF LINES, not the beam.
%%   BXW — no stem in the upper staff (whole rests and a final whole): the gap is the
%%         treble clef against the lower staff's lines, the floor both engines share.
%%   BXO — the same upper quarter, the lower beam starting one column LATER (r8 then seven
%%         beamed eighths): the stem is now left of BOTH the anchor band and the drawn beam,
%%         so old and new Lily# read alike — the control that isolates the band's left end.
%%
%% ⚠️ THE FIRST TWO DRAFTS OF THIS PROBE WERE DEAF, and their own falsifiers said so
%% (2026-09-07). Draft 1: a middle-line b'4's down stem reaches only 3.333 (the middle-line
%% shortening), less than the treble clef's 3.54, so all three books printed the clef-bound
%% 6.590000. Draft 2 forced a g'4 down in a second voice (LilyPond puts its tip at 4.0) — and
%% wrote that voice's remaining time as RESTS, which \voiceTwo pushes down until they meet the
%% lower beam's band in BOTH engines (7.330000, above the stem's 7.05). The stem's own voice
%% must be filled with SPACERS. Recorded so the next probe in this family starts from the
%% clef and from the rests, not from the stem.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * BXW = 3.54 + 2.05 + 1 = 6.59 (clef-bound) in both engines.
%%   * BXS = 4.0 + 2.05 + 1 = 7.05 in LilyPond (the stem against the lines) and in Lily#
%%     with the band at the drawn beam; 4.0 + 3.05 + 1 = 8.05 (+1.0) with the band at the
%%     anchors. FALSIFIER: BXS == BXW means the stem is not binding and the book is deaf.
%%   * BXO == BXS in LilyPond (7.05): moving the beam one column right cannot change what a
%%     stem meets if it never met the beam; and old Lily# reads BXO = 7.05 too, which is what
%%     says the +1.0 on BXS is the band's LEFT END and nothing else. A Lily# stem length
%%     other than LilyPond's 4.0 would show on BXS and BXO ALIKE.

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
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (eq? nm 'Beam)
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a) pos=~a\n"
                                                n i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (car (ly:grob-extent g g X)))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (cdr (ly:grob-extent g g X)))
                                                (ly:grob-property g 'positions)))
                                    (if (eq? nm 'Stem)
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

zeroStaffStaff = \layout {
  \context {
    \Staff
    \override VerticalAxisGroup.default-staff-staff-spacing =
      #'((basic-distance . 0) (minimum-distance . 0) (padding . 1))
  }
}

%% BXS — the upper quarter's down stem at the beam's first column, left of its first stem.
\book {
  \probeTag "BXS"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { << { r1 | r1 | b'1 } \\ { g'4 s4 s2 | g'4 s4 s2 | s1 } >> \bar "|." }
      \new Staff { \clef bass c8 c c c c c c c | c8 c c c c c c c | c1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% BXW — no upper stem: the treble clef against the lower staff's lines.
\book {
  \probeTag "BXW"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { r1 | r1 | b'1 \bar "|." }
      \new Staff { \clef bass c8 c c c c c c c | c8 c c c c c c c | c1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% BXO — the lower beam starts one column later than the upper stem.
\book {
  \probeTag "BXO"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { << { r1 | r1 | b'1 } \\ { g'4 s4 s2 | g'4 s4 s2 | s1 } >> \bar "|." }
      \new Staff { \clef bass r8 c8[ c c c c c c] | r8 c8[ c c c c c c] | c1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

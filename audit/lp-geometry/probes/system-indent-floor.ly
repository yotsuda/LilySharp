\version "2.26.0"
%% LP FIDELITY PROBE — WHAT STOPS TWO SYSTEMS CLOSING WHEN THE UPPER ONE'S SILHOUETTE
%% DOES NOT REACH THE COLUMN THE LOWER ONE'S MARK STANDS IN.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe system-indent-floor.ly -Prefix PROBESIF
%%
%% THE DEFECT THIS MEASURES (user report, 2026-08-25): on
%% scratch/ベースタブLy/Untitled-6.lys written `staff melody` twice, Lily# put 3.050000
%% between the first system's lower staff and the second system's upper one, where the
%% SAME A→B pair later in the same score got 8.200000, and the second system's boxed
%% section label printed through the first system's instrument name.
%%
%% HALF OF THE MECHANISM IS NOT A DEFECT, and this probe exists to say which half.
%% The next system's rehearsal mark and bar number stand at the far left, in the INDENT
%% column — and the first system, being the indented one, has no staff there and so no
%% silhouette. A per-X clearance therefore finds no obstruction above the mark and lets
%% the system rise. LilyPond does this too; books SIF2 and SIF2N below are the pair that
%% shows it. What LilyPond does NOT do is let the pair fall through the floor, and the
%% floor is what Lily# had lost.
%%
%% LILYPOND'S MECHANISM: the page's spring chain is over SPACEABLE STAVES, not over
%% systems. Between two systems the spring runs from the upper system's LAST staff to the
%% lower system's FIRST, and `system-system-spacing` (basic-distance 12, minimum-distance
%% 8, padding 1 — ly/paper-defaults-init.ly:62-65) is stated in THAT frame. The system's
%% own body is not inside the quantity those numbers floor. build_system_skyline states
%% the same conversion as a shift of the skylines themselves
%% (lily/page-layout-problem.cc:1120-1126, first_spaceable_dy / last_spaceable_dy).
%%
%% PREDICTION, written before running (HANDOFF 5.0-2), mechanism first: the collapsed
%% pair reads basic-distance 12.000000 between the two FACING staves' refpoints, and
%% reads the SAME 12.000000 at one, two and three staves — because the body is outside
%% it. FALSIFIER, and it is the whole probe: a reading that GROWS with the staff count
%% means LilyPond floors an origin-to-origin distance after all, Lily#'s old frame was
%% right, and the report is about something else entirely.
%%
%% THE PAIR (HANDOFF 5.0-1): SIF1 / SIF2 / SIF3 are ONE VARIABLE apart — how many staves
%% the system has — and nothing else. Same music, same marks, same names, same indent,
%% same paper. SIF2N is SIF2 with a second variable moved on purpose (no instrument name,
%% no indent) and is the control for the half that is NOT a defect.
%%
%% ⚠️ THE MARK IS DELIBERATELY TALL. With an ordinary \mark "B" every pair in the book
%% sits on the floor and the probe cannot tell a floor from a silhouette. The five-line
%% box makes the skyline term bind wherever it is allowed to, so the collapsed pair and
%% the free pair read different numbers and the floor is visible as the smaller one.
%%
%% ⚠️ ragged-bottom, so nothing is stretched: a page that fills its springs would report
%% the page's arithmetic rather than the pair's.

#(define (dump tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob))
               (yo (ly:prob-property sys 'Y-offset)))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (eq? nm 'StaffSymbol)
                              (format #t "PROBESIF ~a StaffSymbol sysY=~a rel=~a ext=(~a . ~a)\n"
                                      tag yo
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

%% The music every book engraves: four systems, A B A B, with the B systems led by a mark
%% tall enough to make the skyline term bind.
tune = \relative c' {
  \key c \major
  \mark \markup \box "A" c4 c g' g | a a g2 | f4 f e e | d d c2 | \break
  \mark \markup \box \column { "X" "X" "X" "X" "X" } g'4 g f f | e e d2 | \break
  \mark \markup \box "A" c,4 c g' g | a a g2 | f4 f e e | d d c2 | \break
  \mark \markup \box \column { "X" "X" "X" "X" "X" } g'4 g f f | e e d2 |
}

probeSIF =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBESIF BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% SIF1 — one staff. The frame under test is DEGENERATE here: a five-line staff's body is
%% 4.000000 and 12.000000 - 4.000000 is 8.000000, so an origin-to-origin floor and a
%% staff-to-staff floor give the same answer. This book is why the defect survived 572.
\book {
  \probeSIF "SIF1"
  \score {
    << \new Staff \with { instrumentName = "I" } { \clef "treble" \tune } >>
    \layout { indent = 15\mm }
  }
}

%% SIF2 — two staves. Body 13.000000, past basic-distance, where the two frames part.
\book {
  \probeSIF "SIF2"
  \score {
    <<
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tune }
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tune }
    >>
    \layout { indent = 15\mm }
  }
}

%% SIF3 — three staves. Body 22.000000. If the floor tracked the body, this would differ
%% from SIF2 by nine staff spaces.
\book {
  \probeSIF "SIF3"
  \score {
    <<
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tune }
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tune }
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tune }
    >>
    \layout { indent = 15\mm }
  }
}

%% ── THE LEDGER'S BOOKS ────────────────────────────────────────────────────────────────
%% SIFO1 / SIFO2 / SIFO3 are SIF1 / SIF2 / SIF3 with the tall mark replaced by an ordinary
%% one, which is the shape the reported book actually has AND the only shape Lily# can be
%% written to match: `\markup \box \column` has no Lily# spelling, so a five-line mark could
%% never have a twin. The pair still collapses — an ordinary mark is far under the floor —
%% so these read the floor too, and they read it against a Lily# source that exists.
%% ⚠️ THE TALL-MARK BOOKS ABOVE ARE NOT REDUNDANT: they are what shows the number is a
%% FLOOR rather than a coincidence, because they are the only books here in which the free
%% pair reads something else.

tuneO = \relative c' {
  \key c \major
  \mark \markup \box "A" c4 c g' g | a a g2 | f4 f e e | d d c2 | \break
  \mark \markup \box "B" g'4 g f f | e e d2 | \break
  \mark \markup \box "A" c,4 c g' g | a a g2 | f4 f e e | d d c2 | \break
  \mark \markup \box "B" g'4 g f f | e e d2 |
}

\book {
  \probeSIF "SIFO1"
  \score {
    << \new Staff \with { instrumentName = "I" } { \clef "treble" \tuneO } >>
    \layout { indent = 15\mm }
  }
}

\book {
  \probeSIF "SIFO2"
  \score {
    <<
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tuneO }
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tuneO }
    >>
    \layout { indent = 15\mm }
  }
}

\book {
  \probeSIF "SIFO3"
  \score {
    <<
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tuneO }
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tuneO }
      \new Staff \with { instrumentName = "I" } { \clef "treble" \tuneO }
    >>
    \layout { indent = 15\mm }
  }
}

%% SIF2N — SIF2 with the indent column removed. THE CONTROL FOR THE HALF THAT IS NOT A
%% DEFECT: with no indent the first system's staves reach the mark's column, the mark has
%% something to clear, and the first pair reads what the later ones read. With the indent
%% (SIF2) it does not, and LilyPond collapses that pair to the floor exactly as Lily# does.
\book {
  \probeSIF "SIF2N"
  \score {
    <<
      \new Staff { \clef "treble" \tune }
      \new Staff { \clef "treble" \tune }
    >>
    \layout { indent = 0\mm }
  }
}

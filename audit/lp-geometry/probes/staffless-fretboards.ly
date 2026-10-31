\version "2.26.0"
%% LP FIDELITY PROBE — a FretBoards line between a ChordNames line and a Lyrics line, on a
%% system with NO STAFF, and the same three lines over a staff.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe staffless-fretboards.ly -Prefix PROBESF
%%
%% WHY THIS EXISTS (owner's report 2026-10-31, Downloads/lyrics.lys). A `score { chords prog
%% lyrics verse }` sheet drew a chord's diagram THROUGH the lyrics: on a rows-only sheet the
%% two text rows were stacked by a flat band gap (MultiStaffLayouter.TextRowPairGap 0.6)
%% that never looked at the chord row's skyline, while the diagram hangs ~6 ss out of the
%% row's 2.5 ss band. The owner's decision: no floor, the LilyPond chain — ChordNames /
%% FretBoards / Lyrics are three loose lines, each spaced from the one above by skylines and
%% that line's nonstaff-nonstaff-spacing. This probe reads the steps so the port carries
%% measured numbers and not the engraver-init.ly defaults read by eye.
%%
%% THE STEPS, refpoint to refpoint, and which spec each is (page-layout-problem.cc:1313-1331,
%% loose/loose reads the UPPER line's nonstaff-nonstaff-spacing):
%%   chords -> fretboards : ChordNames' nonstaff-nonstaff-spacing (padding 0.5 — already
%%                          measured ink-to-ink on a ChordNames+FretBoards score, see
%%                          ChordNameEngraver.DiagramUnderNamePadding)
%%   fretboards -> lyrics : FretBoards' nonstaff-nonstaff-spacing — NOT YET MEASURED
%%   chords -> lyrics     : ChordNames' (CHL1 of chord-lyric-run.ly read 2.320115015 between
%%                          two staves; SFL1 asks whether a staffless system reads the same)
%%
%% Every VerticalAxisGroup's reference point is dumped relative to the system, with its Y
%% extent and its staff-affinity, and so is every ChordName, FretBoard and LyricText, so the
%% ink-to-ink distance can be read next to the refpoint step. Fonts are pinned (see
%% Measure-LilyPondProbe.ps1 .NOTES).

#(define (dump tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (memq nm '(VerticalAxisGroup StaffSymbol ChordName FretBoard LyricText))
                              (format #t "PROBESF ~a ~a rel=~a ext=(~a . ~a) aff=~a nn=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)
                                      (ly:grob-property g 'nonstaff-nonstaff-spacing 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeSF =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBESF BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

\include "predefined-guitar-fretboards.ly"

mel = \relative c'' { \time 4/4 d4 d a' a | c c a2 | }
prog = \chordmode { d1:m7 | f:maj7 }
vers = \lyricmode { Twin2 -- kle2 twin2 -- kle2 }

%% SFL1 — chords over lyrics, no staff: the owner's sheet without diagrams.
\book { \probeSF "SFL1"
  \score { <<
    \new ChordNames \prog
    \new Lyrics \vers
  >> } }

%% SFL2 — chords / fretboards / lyrics, no staff: the owner's sheet with diagrams.
\book { \probeSF "SFL2"
  \score { <<
    \new ChordNames \prog
    \new FretBoards \prog
    \new Lyrics \vers
  >> } }

%% SFL3 — chords / fretboards, no staff: the chords-only grid with diagrams.
\book { \probeSF "SFL3"
  \score { <<
    \new ChordNames \prog
    \new FretBoards \prog
  >> } }

%% SFL4 — the CONTROL: the same three lines over a staff, i.e. the run the loose chain
%%   already models between a staff and the page edge. The two loose/loose steps must read
%%   as in SFL2 if a staffless system is the same chain.
\book { \probeSF "SFL4"
  \score { <<
    \new ChordNames \prog
    \new FretBoards \prog
    \new Staff \new Voice = "one" \mel
    \new Lyrics \lyricsto "one" \vers
  >> } }

%% SFL5 — chords / fretboards / lyrics ABOVE a staff: all three loose, spaced down onto the
%%   staff — the owner's sheet with a melody staff added below the rows.
\book { \probeSF "SFL5"
  \score { <<
    \new ChordNames \prog
    \new FretBoards \prog
    \new Lyrics \vers
    \new Staff \mel
  >> } }

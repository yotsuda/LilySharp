\version "2.26.0"
%% LP FIDELITY PROBE - a CHORDS row and a LYRICS row in ONE loose run.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe chord-lyric-run.ly -Prefix PROBECL
%%
%% THE DEFECT THIS MEASURES (user report, session 257): the arrangement in
%% scratch/ベースタブLy/Untitled-6.lys — a chords row AND a lyrics row both standing
%% BETWEEN two spaceable staves. Lily# dropped the chords row out of the run
%% (LayoutEngine.ClassifySystem kept only lyrics rows and set an UnmodelledRow flag that
%% only two of its three readers honoured), so the lyrics row was solved as the ONLY
%% occupant of a run that had two and the chord names were engraved ON the lyric line.
%% MEASURED before the fix on a reduced book: chord baseline 21.070000 against lyric
%% baseline 20.980000 — the lyric row exactly where a run of one puts it.
%%
%% This dumps every VerticalAxisGroup's REFERENCE POINT relative to the system, so the
%% run comes out as three steps rather than one sum. THEY ARE THREE DIFFERENT BRANCHES of
%% get_spacing_spec (page-layout-problem.cc:1266-1342), which is the whole reason a chords
%% row could not be added to a chain whose springs came from two score-wide constants:
%%   staff1 -> chords : spaceable/DOWN  (:1284-1294) ChordNames'
%%                      nonstaff-unrelatedstaff-spacing + LARGE_STRETCH
%%   chords -> lyrics : loose/loose, before_affinity = DOWN (:1313-1331) ChordNames'
%%                      nonstaff-nonstaff-spacing — NOT the Lyrics context's, whose
%%                      minimum-distance 2.8 would floor this step ABOVE what LilyPond
%%                      actually reads
%%   lyrics -> staff2 : loose/spaceable, before_affinity = UP (:1299-1305) Lyrics'
%%                      nonstaff-unrelatedstaff-spacing + LARGE_STRETCH
%%
%% MEASURED (2026-08-26, 2.26.0), refpoint to refpoint:
%%   CHL1  staff / chords / lyrics / staff   5.659653422  2.320115015  5.045000000
%%                                           total 13.024768437
%%   CHL2  staff / chords / staff            5.659653422       -       4.045000000
%%                                           total  9.704653422
%%   CHL3  staff / lyrics / staff            4.650841258       -       5.045000000
%%                                           total  9.695841258
%%   CHL4  staff / lyrics / chords / staff   4.650841258  4.037867745  4.045000000
%%                                           total 12.733709002
%%
%% ★★★ WHAT THE CONTROLS CARRY: CHL1 and CHL2 read the FIRST step IDENTICALLY
%% (5.659653422), and CHL3 and CHL4 read it identically the other way round
%% (4.650841258). A spring that holds a loose line under a staff is that LINE's own
%% property, so what stands BELOW the line cannot move it. That is an invariant a per-line
%% spec selection has and a per-run one does not, and it is what the ledger's CHL1/CHL2
%% pair watches.
%%
%% ★★★ AND THE SUMS ARE THE FLOORS. On CHL1 the three steps add to the room EXACTLY
%% (5.659653422 + 2.320115015 + 5.045000000 = 13.024768437), so LilyPond's chain has no
%% slack here and every number above is that spring at its alignment minimum. A Lily#
%% residual on any one step is therefore a statement about the ROOM unless the room reads
%% exact — see the ledger entries, whose four CHL1 readings were opened together so that
%% can be said rather than guessed.
%%
%% ⚠️ LILYPOND WARNS ON CHL1 — "staff-affinities should only decrease" (:1322-1325), since
%% the run leans DOWN then UP — AND LAYS IT OUT ANYWAY. The warning is about the spelling
%% being unusual, not about the spacing being undefined, so these are ordinary readings.
%% Whether Lily# should emit the same diagnostic is a language decision, not a geometry
%% one, and is deliberately not settled here.
%%
%% ⚠️ THE LYRICS NEED A NAMED **Voice**, not a named Staff: \lyricsto takes a Voice, and
%% with the name on the Staff LilyPond warns "cannot find context: Voice = one", the
%% Lyrics context stays empty, remove-empty kills it, and the book silently measures the
%% chords-only arrangement instead. The first run of this probe did exactly that — CHL1
%% and CHL2 came back byte-identical, which is what gave it away.

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
                          (if (memq nm '(VerticalAxisGroup StaffSymbol ChordName LyricText))
                              (format #t "PROBECL ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeCL =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBECL BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

mel = \relative c'' { \time 4/4 c4 c g' g | a a g2 | }
prog = \chordmode { d1:maj7 | e:m7 }
vers = \lyricmode { Twin -- kle twin -- kle lit -- tle star }

\book { \probeCL "CHL1"
  \score { <<
    \new Staff \new Voice = "one" \mel
    \new ChordNames \prog
    \new Lyrics \lyricsto "one" \vers
    \new Staff \mel
  >> } }

\book { \probeCL "CHL2"
  \score { <<
    \new Staff \new Voice = "one" \mel
    \new ChordNames \prog
    \new Staff \mel
  >> } }

\book { \probeCL "CHL3"
  \score { <<
    \new Staff \new Voice = "one" \mel
    \new Lyrics \lyricsto "one" \vers
    \new Staff \mel
  >> } }

\book { \probeCL "CHL4"
  \score { <<
    \new Staff \new Voice = "one" \mel
    \new Lyrics \lyricsto "one" \vers
    \new ChordNames \prog
    \new Staff \mel
  >> } }

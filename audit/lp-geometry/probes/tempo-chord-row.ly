\version "2.26.0"
%% LP FIDELITY PROBE — WHERE THE METRONOME MARK SITS ON A STAFFLESS SHEET (a ChordNames
%% line, with or without a Lyrics line under it, and no Staff at all).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tempo-chord-row.ly -Prefix PROBET
%%
%% WHY (session 786, 2026-10-04). Session 785 gave the staffless section LABEL its LilyPond
%% number (mark-chord-row.ly MKY/MKZ) and left one sibling open: the TEMPO on the same sheet
%% still takes a STAFF's quiet baseline in Lily# -- MetronomeMarkGeometry.QuietBaselineAboveMiddle
%% = 2.0 + 0.05 + 0.8 - ink bottom, which is "padding 0.8 over a staff symbol's outer edge" --
%% on a row that has no staff symbol (samples/amazing-grace.lys's "grid" score is the shape).
%% The ledger's tempo points (tempo-mark.ly TMQ/TMT) all stand over a staff.
%%
%% ⚠️ THE MARK IS AT THE LINE START ON PURPOSE. A rows-only Lily# score draws only the HEADER
%% tempo (bar 1); a section-level or in-music tempo of a part the score does not place is not
%% engraved there (measured 2026-10-04, Lab sessions/p786/probes v2/v3), so a mid-line pair
%% like MKY/MKZ's cannot be built in Lily# today. At bar 1 both engines agree about what is
%% under the mark: Lily# aligns the tempo on the line's meter (which the lyric row's grid
%% carries, left of the chord) and LilyPond on the first musical column (the chord's left);
%% both inks reach over bar 1's chord symbol, which is what the reading asks about.
%% ⚠️ `4 = 111' AND NOT `120': the digit 1 has a FLAT bottom on the baseline, the \smaller
%% note is DOWN-aligned onto it and `=' stands above it, so the mark's ink bottom is the
%% baseline EVERYWHERE along its width -- a pointwise skyline pass and a flat-box one read the
%% same edge, whatever part of the mark stands over the chord. (`120' carries the round
%% digits' -0.033010 overshoot under its right half only; TMQ's header has that story.)
%%
%% LILYPOND'S MECHANISM (read in source before running):
%%   * MetronomeMark (define-grobs.scm:2335-2366): direction UP, side-axis Y,
%%     Y-offset side-position-interface::y-aligned-side, padding 0.8, NO staff-padding,
%%     outside-staff-priority 1300, outside-staff-horizontal-padding 0.2,
%%     after-line-breaking ly:side-position-interface::move-to-extremal-staff.
%%   * Its supports are the STAVES: metronome-engraver.cc:137-139 side-support-elements =
%%     stavesFound -- and stavesFound is kept by Staff_collecting_engraver, which lives in
%%     Staff and Score (engraver-init.ly:74, :754) and collects staff-symbol grobs. A sheet
%%     with no Staff has NO staff symbol, so the support set is EMPTY.
%%   * aligned_side with an empty support set (side-position-interface.cc:347-351): the
%%     support skyline is set to height 0.0 in the common refpoint's frame, which with no
%%     supports is the mark's own Y-PARENT; then padding 0.8 (:360-370). The Y-parent is the
%%     row move-to-extremal-staff re-parented the mark onto (:513-547 -- the top live axis
%%     group whose X extent meets the mark's, a ChordNames line included, as
%%     barnumber-staffless.ly established), whose refpoint is the ChordName baseline.
%%     => QUIET: the mark's ink bottom stands 0.800000 over the chord row's BASELINE.
%%   * Then the outside-staff pass (axis-group-interface.cc avoid_outside_staff_collisions)
%%     lifts it clear of the row's own skyline -- the symbols' extent boxes (lily/grob.cc:81-85,
%%     a ChordName declares no vertical-skylines) -- by outside-staff-padding 0.460000, where
%%     they meet in X.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * TCY / TCZ (a chord under the mark; TCZ's chords all sharped, nothing else moved):
%%     mark ink bottom (= its baseline, see above) - chord ink top = 0.460000 on BOTH books.
%%     The quiet 0.8 over the baseline lands under the symbol's own top (1.907 for `Am'), so
%%     the outside-staff pass decides, and it reads the box skyline, so raising the ink raises
%%     the mark by exactly the ink. FALSIFIER: the two books' gaps differ -> the mark is not
%%     placed against the symbols' ink; a gap near 0.8 + something -> no outside-staff lift
%%     happens on a ChordNames line and the quiet offset alone places it.
%%   * TCQ (the quiet regime: bar 1 holds NO chord, `s1', and the next symbol stands a whole
%%     lyric bar to the right, beyond the mark's width): mark ink bottom - the row's chord
%%     BASELINE = 0.800000 exactly (padding alone, support height 0 at the refpoint).
%%     FALSIFIER: anything else -> the empty-support branch is not what places it (e.g. the
%%     parent is still the System and the 0.8 is paid from the system refpoint).
%%   * TCG (CONTROL: TCY without the Lyrics line): the same number as TCY. A Lyrics line below
%%     the chords is not in the mark's support set and not in the row it is re-parented onto.
%%     FALSIFIER: TCG != TCY -> the lyric row reaches the mark somehow, and the Lily# books
%%     (whose grid moves from the lyric row to the chord row when the lyrics go) must be
%%     read against the right half.

%% MEASURED (2026-10-04, first run; three entries opened the same session):
%%   * TCY: MetronomeMark rel -3.161440591 ext (0 . 3.161440591) xext (0 . 7.408893) at X 1.305312;
%%     ChordName `Am' rel -5.528731072 ext (0 . 1.907290480) at X 1.305312 (the mark's left IS
%%     the chord's left: no TimeSignature on the line, so it aligns on the musical column).
%%     Gap = -3.161440591 + 5.528731072 - 1.907290480 = 0.460000000. HELD.
%%   * TCZ: `A#m' rel -5.846313090 ext (-0.953517 . 2.224872498); ink top -3.621440592, the same
%%     as TCY's to nine digits (the row moved by the ink growth); gap 0.460000000. HELD -- the
%%     pair's identity is LilyPond's.
%%   * TCG: identical to TCY's two lines (rel -3.161440591 / -5.528731072, X 0.5 / 0.5). HELD.
%%   * TCQ: FALSIFIED. MetronomeMark rel -5.338862910; ChordName row rel -1.938699656; LyricText
%%     row rel -7.438701107 -- the mark stands BETWEEN the chord row and the lyric row, 0.46
%%     over the syllable `two': 2.099838197 above the lyric baseline, against the syllable's
%%     box top 1.639812346 + 0.46 = 2.099812346 -- 0.000025851 apart, the syllable's OUTLINE
%%     skyline rather than its box (LyricText declares no vertical-skylines override but the
%%     outside-staff pass reads the Lyrics group's accumulated outline). WHY: an axis
%%     group's X extent is its ELEMENTS', and bar 1 of the ChordNames line holds none, so
%%     get_extremal_staff passed the chord row over and re-parented the mark onto the LYRICS
%%     line, whose padded refpoint + 0.8 then lost to the outside-staff pass over the syllables.
%%   * TCE: NOT the quiet regime after all -- rel -3.161440591, i.e. 0.46 over bar 2's `f'
%%     (X 6.298 under a mark 7.4 wide: an empty chords-only bar is 5.8 wide). Kept as it was
%%     run; TCF is the book that asks the question.
%%   * TCF: FALSIFIED the other way. MetronomeMark rel 0.800000 EXACTLY, the chords at
%%     -1.938699656 -- no group's X extent met the mark (three empty bars), move_to_extremal_staff
%%     returned #f, and aligned_side paid the 0.8 from the SYSTEM's refpoint.
%% => THE PORT TAKES THE HELD HALF: a row anchor's quiet base is its refpoint + 0.8 and the row's
%%    symbols lift the mark by 0.46 (MusicMarkEngraver's tempo arm, session 786). Lily# anchors
%%    every Score-level mark on the top score-grob row, not on a per-mark X-extent test, so the
%%    two falsified regimes (a tempo wedged between the rows; a tempo padded off the system top)
%%    are deliberately not reproduced -- stated at the arm as the port's one non-literal step.

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
                          (if (memq nm '(MetronomeMark ChordName LyricText))
                              (format #t "PROBET ~a ~a rel=~a ext=(~a . ~a) X=~a xext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-relative-coordinate g sg X)
                                      (car (ly:grob-extent g g X))
                                      (cdr (ly:grob-extent g g X))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeT =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBET BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% TCY -- the mark over bar 1's chord, a Lyrics line under the chords (the shape the reporting
%% user writes). `a:m' first: wide enough (3.93) that the mark's note and `=' stand over it.
\book {
  \probeT "TCY"
  \score {
    <<
      \new ChordNames \chordmode { \tempo 4 = 111 a1:m f c g }
      \new Lyrics \lyricmode {
        \set stanza = "" one4 two three four five six sev -- en
        eight nine ten e -- le -- ven twelve thir -- teen
      }
    >>
  }
}

%% TCZ -- TCY with every chord sharped: every symbol's ink top rises, nothing else moves.
\book {
  \probeT "TCZ"
  \score {
    <<
      \new ChordNames \chordmode { \tempo 4 = 111 ais1:m fis cis gis }
      \new Lyrics \lyricmode {
        \set stanza = "" one4 two three four five six sev -- en
        eight nine ten e -- le -- ven twelve thir -- teen
      }
    >>
  }
}

%% TCQ -- the QUIET regime: no chord under the mark (bar 1 is `s1'), the row's other symbols
%% a lyric bar to the right.
\book {
  \probeT "TCQ"
  \score {
    <<
      \new ChordNames \chordmode { \tempo 4 = 111 s1 f c g }
      \new Lyrics \lyricmode {
        \set stanza = "" one4 two three four five six sev -- en
        eight nine ten e -- le -- ven twelve thir -- teen
      }
    >>
  }
}

%% TCE -- the QUIET regime on a CHORDS-ONLY sheet (amazing-grace's grid: a pickup bar with no
%% chord under the header tempo, and no Lyrics line). Added after TCQ's run (see the header).
\book {
  \probeT "TCE"
  \score {
    <<
      \new ChordNames \chordmode { \tempo 4 = 111 s1 f c g }
    >>
  }
}

%% TCF -- the QUIET regime on a CHORDS-ONLY sheet for real: THREE empty bars first, so no symbol
%% stands within the mark's width (TCE's bar 2 ' did, at 6.3 under a 7.4-wide mark).
\book {
  \probeT "TCF"
  \score {
    <<
      \new ChordNames \chordmode { \tempo 4 = 111 s1 s1 s1 f c g }
    >>
  }
}

%% TCG -- the CONTROL: TCY with no Lyrics line (a chords-only grid in Lily#).
\book {
  \probeT "TCG"
  \score {
    <<
      \new ChordNames \chordmode { \tempo 4 = 111 a1:m f c g }
    >>
  }
}

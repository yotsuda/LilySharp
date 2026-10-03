\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A MID-LINE BAR NUMBER SITS WHEN A CHORDNAMES LINE LEADS THE
%% SYSTEM (`barNumbers every 1' on a lead sheet).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe barnumber-mid-line.ly -Prefix PROBEN
%%
%% THE DEFECT THIS MEASURES (session 767, dogfood leadsheet-collide bars 12-13): with
%% `barNumbers every 1' Lily# sets every number at the STAFF's height — padding 1.0 over the
%% top staff line — and on a lead sheet whose chord row carries diagrams the mid-line numbers
%% print through the diagrams' fingering digits. LilyPond's twin of the same book sets its
%% mid-line numbers beside the CHORD NAMES, a small superscript left of each symbol, and only
%% the line-start numbers at the staff's left (Lab sessions/p767/twin/lp-leadsheet-collide…).
%%
%% LILYPOND'S MECHANISM (barnumber-chord-row.ly's header has the line-start half): BarNumber
%% carries after-line-breaking = move-to-extremal-staff (define-grobs.scm:320-321), which
%% re-parents the number onto the first LIVE element of the vertical alignment, from the top,
%% whose X extent meets the number's own widened by 1.0 (side-position-interface.cc:513-547,
%% staff-grouper-interface.cc:31-56 get_extremal_staff — an axis group's X extent is its
%% ELEMENTS'). A line-start number hangs into the left margin and the chord row's ink starts
%% past the clef, so it stays on the staff (ledger barnumber.chord-row.staff-to-ink-bottom =
%% 3.05). A MID-LINE number stands at its bar line, and the bar's chord stands at the bar's
%% first column, within 1.0 of it — so the chord row takes the number. Then (:549-562) the
%% number's side-support-elements — stavesFound, the STAFF — are dropped, since the staff no
%% longer shares the number's Y-parent, and aligned_side (:347-351, :370) pads the number
%% 1.0 off an EMPTY support set, i.e. off the ChordNames group's refpoint = the symbols'
%% baseline. The outside-staff pass (priority 100) would lift it a further 0.46 over a symbol
%% it overlapped in X; the number stands LEFT of the symbol, so it does not overlap one.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * BNM (chords on every bar, mid-line numbers 2 3 4): number ink bottom − chord BASELINE
%%     = 1.000000 exactly (padding alone, support height 0 at the refpoint); the number's
%%     ink bottom over the STAFF refpoint is then the chord row's height above the staff +
%%     1.0, far above the 3.05 of a staff-anchored number. FALSIFIER: 3.05 over the staff ->
%%     no re-parenting happens mid-line either and the whole observation is a picture of
%%     something else; a gap of 0.46 + symbol top -> the number overlaps the symbol and the
%%     pass, not the padding, places it.
%%   * BNT (BNM with every chord sharped: taller symbols, the row pushed off the staff by the
%%     descender): the SAME 1.000000 over the chord baseline — the padding is paid from the
%%     refpoint, so the symbols' height does not enter. FALSIFIER: a gap that grows with the
%%     ink -> the number IS placed against the symbols' skyline after all.
%%   * BNE (a chord in bar 1 only, `s1' in bars 2-4): numbers 2 3 4 stand where NO symbol is
%%     within 1.0 — the ChordNames group's X extent (its elements') does not reach them, so
%%     they fall back to the STAFF: ink bottom 3.050000 over the staff refpoint, the
%%     line-start value. FALSIFIER: the same chord-row height as BNM -> the group's X extent
%%     is the LINE's, not its elements', and Lily# needs no reach test.

%% MEASURED (2026-10-04, first run; three entries opened the same session):
%%   * BNM: StaffSymbol rel -2.255433; ChordName row rel 2.789567 (ext (-0.060 . 1.939) /
%%     (0 . 1.907)); BarNumber "1" rel 0.794567 at X -0.956 (line start: staff -2.255433 +
%%     3.05 = 0.794567, the staff value); "2" rel 3.789567 ext (0 . 1.229) at X 20.452 -> ink
%%     bottom - chord baseline = 1.000000; "3" rel 3.815774 ext bottom -0.026208 -> ink bottom
%%     3.789567, the same 1.000000; "4" 1.000000. HELD. The number's ink bottom stands 6.045
%%     over the STAFF refpoint — not the 3.05 of a staff-anchored number. The chord of each bar
%%     starts 1.17 right of the number's left (21.619 vs 20.452) and 0.21 right of its ink's
%%     right edge: within the widened reach, outside the overlap, so the padding alone decides.
%%   * BNT: ChordName row rel 3.743083 (every symbol ext (-0.953517 . 2.224872): the row
%%     pushed up by the descender); "2" rel 4.743083, "3" ink bottom 4.743083, "4" 4.743083 ->
%%     1.000000 on all three. HELD — the symbols' height does not enter.
%%   * BNE: StaffSymbol rel -1.938700; the only ChordName at X 8.585 (bar 1); "2" and "4" rel
%%     1.111300 = staff -1.938700 + 3.05 -> back on the STAFF, 3.050000 exactly. HELD. ("3"
%%     rel 2.647508 ext bottom -0.026208 -> 4.56 over the staff: the staff's own outside-staff
%%     pass lifted it clear of the stem under it, which BNM's "3" never met because that
%%     number stood on the row.)
%% => THE PORT (BarNumberEngraver.MidLineRowAnchor, session 788): a mid-line number whose bar's
%%    chord row has a symbol within 1.0 of its X hangs on that row — ink bottom = the symbols'
%%    baseline + 1.0, lifted 0.46 over a symbol it overlaps — and keeps the staff otherwise.
%%    Lily# read 3.050000 over the staff on all three books before it (the number below the
%%    chord row; through the fingering of a row carrying diagrams).
%% ⇒ CORRECTED (session 789, barnumber-row-extent.ly): the reach is tested against the ROW's
%%    X extent — the UNION of its symbols', an axis group's extent being its elements' — not
%%    against the nearest symbol. BNM and BNE cannot tell the two readings apart (every bar
%%    has a chord / only bar 1 has one); BRX there does (chords in bars 1 and 5 only, and
%%    numbers 2-4 all ride the row). The port now asks the row's span (MidLineRowAnchor).

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
                          (if (memq nm '(BarNumber StaffSymbol ChordName))
                              (format #t "PROBEN ~a ~a rel=~a ext=(~a . ~a) X=~a xext=(~a . ~a)\n"
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
probeN =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEN BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% The `every 1' spelling the twin writes (LilyPondExporter, barNumbers every N).
everyBar = {
  \set Score.barNumberVisibility = #all-bar-numbers-visible
  \override Score.BarNumber.break-visibility = #end-of-line-invisible
}

\book {
  \probeN "BNM"
  \score {
    <<
      \new ChordNames \chordmode { c1 g a:m f }
      \new Staff \relative c'' {
        \everyBar \time 4/4
        c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

\book {
  \probeN "BNT"
  \score {
    <<
      \new ChordNames \chordmode { cis1 gis ais:m fis }
      \new Staff \relative c'' {
        \everyBar \time 4/4
        c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

\book {
  \probeN "BNE"
  \score {
    <<
      \new ChordNames \chordmode { c1 s1 s1 s1 }
      \new Staff \relative c'' {
        \everyBar \time 4/4
        c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

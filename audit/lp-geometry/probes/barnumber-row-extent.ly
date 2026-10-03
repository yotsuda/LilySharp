\version "2.26.0"
%% LP FIDELITY PROBE — WHICH MID-LINE BAR NUMBERS A CHORD ROW TAKES: the ROW'S WHOLE X EXTENT,
%% not the nearest symbol — and WHERE ALONG THE BAR LINE the number stands.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe barnumber-row-extent.ly -Prefix PROBER
%%
%% THE DEFECT THIS MEASURES (session 789; dogfood leadsheet-collide bars 3 and 11, seen by
%% session 788 after its port): Lily# re-parents a mid-line number onto the chord row only
%% when a SYMBOL's ink meets the number's X widened by 1.0 (BarNumberEngraver.MidLineRowAnchor,
%% read off barnumber-mid-line.ly's BNM/BNE). On the dogfood book the bar after a `.|:' and the
%% bar after a mid-line key+meter change keep their numbers on the STAFF (the chord stands 2.9
%% past the bar line, out of reach) where LilyPond's twin sets both beside the chord names.
%% The twin's dump (Lab sessions/p789/probes/lsc-dump.out) says why: at bar 3 the number spans
%% X 61.896..62.852, the chord starts at 64.268 — 0.42 beyond the widened reach — and LilyPond
%% re-parented it anyway (ink bottom 1.000000 over the chord baseline).
%%
%% LILYPOND'S MECHANISM, read before running: move_to_extremal_staff
%% (lily/side-position-interface.cc:521-523) widens the NUMBER's extent by 1.0 and asks
%% get_extremal_staff (lily/staff-grouper-interface.cc:42-55) for the first live element of the
%% vertical alignment, from the top, whose X extent INTERSECTS it. That element is the
%% ChordNames VerticalAxisGroup, and an axis group's X extent is the UNION of its elements'
%% (lily/axis-group-interface.cc generic_group_extent / relative_group_extent) — every chord
%% name on the line, not the one in the number's bar. So any number standing between the
%% row's first and last symbol is taken, however far the nearest symbol is; a number outside
%% the row's span (BNE's bars 2-4, whose only chord is in bar 1) is not.
%% The number's X: BarNumber X-offset = self-aligned-on-breakable with self-alignment-X
%% (break-alignment-list LEFT LEFT RIGHT) (scm/define-grobs.scm:334-337) — mid-line its LEFT
%% edge stands on the bar line's break-align-anchor, ly:bar-line::calc-anchor
%% (scm/bar-line.scm:812-852): the centre of a one-glyph bar's extent, else the centre of its
%% SPAN-BAR stencil (".|:" → ".|", the dots dropped).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * BRX (a chord in bar 1 and bar 5 only, `s1' in 2-4; numbers 2 3 4 mid-line): all three
%%     numbers' ink bottom − chord BASELINE = 1.000000 — the row's extent runs from bar 1's
%%     symbol to bar 5's and spans them all, though no symbol is within 1.0 of any of them.
%%     FALSIFIER: 3.050000 over the staff -> the test IS per symbol after all and the dogfood
%%     observation is a picture of something else. Number "3": left edge − bar line ink left
%%     = 0.095000 (half the thin stroke).
%%   * BRR (BNM's music with `\repeat volta 2' opening at bar 3, so a `.|:' stands mid-line):
%%     "3" ink bottom − chord baseline = 1.000000; "3" left − the `.|:' ink's LEFT = 0.545000
%%     (half of thick 0.6 + kern 0.3 + thin 0.19 = the ".|" span stencil's centre, as the
%%     twin read: 61.896 − 61.351). FALSIFIER: 0.000 (the glyph's left) or 0.920 (its centre).
%%   * BRK (`\key f \major \time 3/4' at bar 3, chords on every bar): "3" ink bottom − chord
%%     baseline = 1.000000; "3" left − bar ink left = 0.095000 (a plain bar; the key and the
%%     meter stand to its right and do not move the anchor).

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
                          (if (memq nm '(BarNumber BarLine StaffSymbol ChordName KeySignature TimeSignature))
                              (format #t "PROBER ~a ~a rel=~a ext=(~a . ~a) X=~a xext=(~a . ~a) glyph=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-relative-coordinate g sg X)
                                      (car (ly:grob-extent g g X))
                                      (cdr (ly:grob-extent g g X))
                                      (ly:grob-property g 'glyph-name "")))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeR =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBER BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% The `every 1' spelling the twin writes (LilyPondExporter, barNumbers every N).
everyBar = {
  \set Score.barNumberVisibility = #all-bar-numbers-visible
  \override Score.BarNumber.break-visibility = #end-of-line-invisible
}

\book {
  \probeR "BRX"
  \score {
    <<
      \new ChordNames \chordmode { c1 s1 s1 s1 f1 }
      \new Staff \relative c'' {
        \everyBar \time 4/4
        c4 d e f | g a b c | c4 b a g | f e d c | c4 d e f |
      }
    >>
  }
}

\book {
  \probeR "BRR"
  \score {
    <<
      \new ChordNames \chordmode { c1 g a:m f }
      \new Staff \relative c'' {
        \everyBar \time 4/4
        c4 d e f | g a b c | \repeat volta 2 { c4 b a g | f e d c | }
      }
    >>
  }
}

\book {
  \probeR "BRK"
  \score {
    <<
      \new ChordNames \chordmode { c1 g f2. c2. }
      \new Staff \relative c'' {
        \everyBar \time 4/4
        c4 d e f | g a b c | \key f \major \time 3/4 c4 bes a | g f e |
      }
    >>
  }
}

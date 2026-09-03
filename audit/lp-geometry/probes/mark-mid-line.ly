\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A REHEARSAL MARK SITS MID-LINE, against the bar line it
%% break-aligns on.
%%
%% Run it with ../Measure-LilyPondProbe.ps1 -Probe mark-mid-line.ly (a few seconds), or
%% lilypond -dno-print-pages; the numbers come out on stdout as PROBEX lines.
%%
%% THE REPORT (owner, 2026-09-04, on Lambada Complicada's endings): Lily# stood the `E1` and
%% `E2` boxes to the RIGHT of where LilyPond stands them. Every ledger point on the mark's X
%% so far referees a LINE START (mark-chord-row.ly MKQ/MKK/MKB, where the mark lands on the
%% clef's or key's right edge); mid-line was declared unmeasured in MusicMarkEngraver
%% ("LilyPond hangs them on the staff-bar's calc-anchor (the bar stencil's centre), a
%% placement no probe has measured yet"). This is that probe.
%%
%% THE MECHANISM, read in the source before running: RehearsalMark break-aligns on
%% (staff-bar key-signature clef) (scm/define-grobs.scm:2881); mid-line the staff-bar is
%% visible and wins (lily/break-alignment-interface.cc:299-334 find_parent), the mark's
%% refpoint lands on the parent's break-align-anchor (:337-353 self_align_callback) — for a
%% BarLine that is ly:bar-line::calc-anchor = the CENTRE of its X extent
%% (scm/bar-line.scm:812-838) — and its self-alignment-X is the opposite of the parent's
%% break-align-anchor-alignment (scm/output-lib.scm:484-488), which a BarLine leaves at the
%% default CENTER, so the mark is CENTRED on the bar line's ink.
%%
%% PREDICTION, written before running: box centre − bar centre = 0.000000 in every book,
%% whatever the bar's glyph (|, |:, :|, :|.|:) and whether or not a volta bracket stands
%% over it. FALSIFIER: a non-zero reading, or one that changes with the glyph.
%%
%% The five books are one variable apart from each other: the glyph at the mark's bar.
%%   MMB  plain |          MMS  |:  (a repeat opens at the mark)
%%   MMR  :|   (a repeat closes at the mark)
%%   MMD  :|.|: (one closes and another opens)
%%   MMV  the owner's shape — \alternative, E1 on a plain bar under the bracket, E2 on :|

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
                          (if (memq nm '(RehearsalMark BarLine))
                              (format #t "PROBEX ~a ~a X=~a xext=(~a . ~a) glyph=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg X)
                                      (car (ly:grob-extent g g X))
                                      (cdr (ly:grob-extent g g X))
                                      (ly:grob-property g 'glyph-name "")))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeX =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

\book {
  \probeX "MMB"
  \score { \new Staff \relative c'' { \time 4/4
    c4 d e f | g a b c | \mark \markup \box "B" c4 b a g | f e d c | } }
}

\book {
  \probeX "MMS"
  \score { \new Staff \relative c'' { \time 4/4
    c4 d e f | g a b c | \repeat volta 2 { \mark \markup \box "B" c4 b a g | f e d c | } } }
}

\book {
  \probeX "MMR"
  \score { \new Staff \relative c'' { \time 4/4
    \repeat volta 2 { c4 d e f | g a b c | } \mark \markup \box "B" c4 b a g | f e d c | } }
}

\book {
  \probeX "MMD"
  \score { \new Staff \relative c'' { \time 4/4
    \repeat volta 2 { c4 d e f | g a b c | } \repeat volta 2 { \mark \markup \box "B" c4 b a g | f e d c | } } }
}

\book {
  \probeX "MMV"
  \score { \new Staff \relative c'' { \time 4/4
    \repeat volta 2 { c4 d e f | g a b c | }
    \alternative { { \mark \markup \box "E1" c4 b a g | } { \mark \markup \box "E2" f e d c | } } } }
}

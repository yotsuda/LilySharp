\version "2.26.0"
%% LP FIDELITY PROBE — WHAT DOES A CONTINUATION BAR NUMBER HANG ON WHEN THE SYSTEM HAS NO
%% STAFF AT ALL (ChordNames + Lyrics only)?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe barnumber-staffless.ly -Prefix PROBESL
%%
%% THE QUESTION. barnumber-chord-row.ly settled the case where a chords row LEADS a system
%% that still has a staff: LilyPond leaves the number on the STAFF, tucked below the row,
%% and Lily# ports that as BarNumberEngraver.AnchorStaff — the topmost non-hidden SPACEABLE
%% staff. A lead sheet with no staff has none, so AnchorStaff returns null and the number
%% falls back to the SYSTEM TOP, which is the very anchor session 220 replaced. Measured on
%% the reporting book (user report, session 240): the number rides a whole band too high.
%%
%% ⚠️ THE ANSWER IS NOT "THE STAFF", AND IT IS NOT "THE BOTTOM ROW". It is the literal
%% reading of the two LilyPond functions below, and the third book is written to make that
%% reading falsifiable rather than merely plausible.
%%
%%   LILYPOND-REF: lily/side-position-interface.cc Side_position_interface::move_to_extremal_staff
%%     BarNumber.after-line-breaking. Takes the number's own X extent relative to the
%%     system, WIDENS IT BY 1.0, and asks the system's VerticalAlignment for the extremal
%%     element in the number's direction (UP). On success it re-parents the number to that
%%     element — and then DROPS every side-support element that no longer shares a refpoint
%%     with the new parent, which is what empties the support set below.
%%
%%   LILYPOND-REF: lily/staff-grouper-interface.cc Staff_grouper_interface::get_extremal_staff
%%     "Find the furthest staff in the given direction whose x-extent overlaps with the
%%     given interval." It walks the VerticalAlignment's `elements` — that is, EVERY row's
%%     VerticalAxisGroup, chord rows and lyric rows included — and returns the first LIVE
%%     one whose X extent intersects. It does NOT test is_spaceable and it does NOT test
%%     for a StaffSymbol. The name says staff; the code says row.
%%
%% So the anchor rule is: THE TOPMOST ROW WHOSE HORIZONTAL EXTENT REACHES THE NUMBER'S
%% COLUMN. A staff wins in an ordinary lead sheet only because a StaffSymbol's line spans
%% the system from x=0 while a chord row's first chord name starts after the clef — session
%% 220's ported special case is a CONSEQUENCE of this rule, not a different rule.
%%
%% ─────────────────────────────────────────────────────────────────────────────────────
%% WHAT WAS MEASURED (LilyPond 2.26.0, this file, 2026-08-24). Read the SECOND system of
%% each book; the figures below are that system.
%%
%% SLC — THE CONTROL, AND THE CALIBRATION.
%%     StaffSymbol   y=-1.938700  yext=(-2.05 . 2.05)  xext=(0.050000 . 36.450180)
%%     ChordNames row                                  xext=(5.800000 . 32.298733)
%%     Lyrics row                                      xext=(4.868975 . 32.392007)
%%     BarNumber     y=+1.131773  yext=(-0.020473 . 1.156104)   parent = the staff's row
%%   Only the staff's row reaches the number's column; the chord row starts at 5.8 and the
%%   lyric row at 4.87, both far right of it. Ink bottom 1.111300 - staff refpoint
%%   -1.938700 = 3.050000 = barnumber.chord-row.staff-to-ink-bottom. The probe is calibrated
%%   against the ledger.
%%
%%   ⚠️ AND 3.05 IS NOT A CONSTANT — IT IS DERIVED. side-support-elements holds exactly one
%%   grob, the StaffSymbol, whose yext is (-2.05 . 2.05); BarNumber.padding is 1.0; and
%%   2.05 + 1.0 = 3.05 to the digit. The ledger point is the SUM, not a magic number.
%%
%% SLN — THE REPORTED SHAPE. The number hangs on the LYRIC row, not the chord row:
%%     ChordNames row y=-1.938700  xext=( 1.237025 . 23.758529)
%%     Lyrics row     y=-7.438701  xext=( 0.000000 . 27.007975)
%%     BarNumber      y=-5.853653  xext=(-0.887726 .  0.000000)   parent y=-7.438701
%%   The number's widened interval is (-1.887726 . 1.000000). The chord row starts at
%%   1.237025 and MISSES IT BY 0.237, so the walk skips the chord row and takes the lyric
%%   row. Rendered, the "5" superscripts the first syllable.
%%
%% SL3 — THE FALSIFICATION. Three rows, Lyrics / ChordNames / Lyrics, with whole-note
%%   syllables so BOTH lyric rows stay alive on both systems (an empty row hara-kiris away
%%   and would leave a single candidate, which decides nothing). Both lyric rows reach the
%%   number's column, so "the bottom row" and "the topmost that reaches" disagree here:
%%     upper Lyrics row y=-2.741153  xext=(0.000000 . 27.622555)  <- BarNumber's parent
%%     ChordNames  row  y=-6.213629  xext=(1.237025 . 24.373108)  <- skipped, misses by 0.237
%%     lower Lyrics row y=-8.488551  xext=(0.819439 . 27.622555)  <- reaches, and LOSES
%%   The number took the UPPER row. "The bottom row" is dead; "the topmost that reaches"
%%   survives a test that could have killed it.
%%
%% ⚠️ WHAT IS STILL OPEN — THE OFFSET, NOT THE ANCHOR. On a staffless system the number's
%% side-support-elements array is EMPTY (dumped below; SLC's holds the StaffSymbol). With
%% no support there is nothing for "support top + padding" to sit above, and the measured
%% result does not follow that arithmetic: the number's baseline lands 1.585048 above the
%% lyric row's refpoint, so its ink bottom is 1.564576 — 0.150158 BELOW that row's own ink
%% top of 1.714734. That is legal because the two are horizontally disjoint (the number
%% lives at x<0, the syllable at x>=0), and it is the same 1.585048 in SLN and SL3. But
%% WHICH expression produces 1.585048 is NOT settled here, and it is not "row ink top +
%% padding" (that would be 2.714734). A port must resolve that before it places anything:
%% carrying the staffful 3.05 over to a staffless system would be wrong twice, once for the
%% anchor and once for the padding.
%%
%% NOT PORTED HERE. This probe records what LilyPond does. Giving BarNumberEngraver.AnchorStaff
%% and PageAnchorOffsets a real anchor for a staffless system changes output and is its own
%% island, with its own corpus reading and its own approval.

#(define (grob-name g) (assq-ref (ly:grob-property g 'meta) 'name))

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
                        (let ((nm (grob-name g)))
                          (if (memq nm '(BarNumber VerticalAxisGroup
                                         StaffSymbol ChordName LyricText))
                              (let ((p (ly:grob-parent g Y)))
                                (format #t
                                        "PROBESL ~a ~a y=~a xext=(~a . ~a) yext=(~a . ~a) parent=~a py=~a\n"
                                        tag nm
                                        (ly:grob-relative-coordinate g sg Y)
                                        (car (ly:grob-extent g sg X))
                                        (cdr (ly:grob-extent g sg X))
                                        (car (ly:grob-extent g g Y))
                                        (cdr (ly:grob-extent g g Y))
                                        (if (ly:grob? p) (grob-name p) "none")
                                        (if (ly:grob? p)
                                            (ly:grob-relative-coordinate p sg Y)
                                            "-"))))
                          ;; The support set is the whole point of the offset question, so
                          ;; it is dumped rather than remembered: SLC names one StaffSymbol,
                          ;; SLN and SL3 name nothing at all.
                          (if (eq? nm 'BarNumber)
                              (let ((sup (ly:grob-object g 'side-support-elements)))
                                (format #t "PROBESL ~a   BarNumber padding=~a support=~a\n"
                                        tag (ly:grob-property g 'padding)
                                        (if (ly:grob-array? sup)
                                            (map (lambda (e)
                                                   (list (grob-name e)
                                                         (car (ly:grob-extent e e Y))
                                                         (cdr (ly:grob-extent e e Y))))
                                                 (ly:grob-array->list sup))
                                            'NONE))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

probeSL =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBESL BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% Bar numbers print from the second system on; \break forces exactly two systems so the
%% second one carries a continuation number.
theChords = \chordmode { c1 f c g | \break c f c g }
theWords  = \lyricmode { one two three four | five six sev -- en }

%% BOOK SLN — NO STAFF, the reported shape. A ChordNames line and a Lyrics line and nothing
%% else. The Bar_engraver is consisted onto each so the system has barlines to number, which
%% is what a Lily# rows-only score draws.
\book {
  \probeSL "SLN"
  \score {
    <<
      \new ChordNames \with { \consists "Bar_engraver" } \theChords
      \new Lyrics \with { \consists "Bar_engraver" } \theWords
    >>
    \layout { indent = 0\mm }
  }
}

%% BOOK SLC — THE CONTROL AND THE CALIBRATION. The same two rows with a staff between them,
%% the shape barnumber-chord-row.ly already settled. Re-measured here in the same run and
%% the same paper so the pair is read together; its 3.05 is what says the probe is sound.
\book {
  \probeSL "SLC"
  \score {
    <<
      \new ChordNames \theChords
      \new Staff \relative c'' { c1 d e f | \break g a b c }
      \new Lyrics \theWords
    >>
    \layout { indent = 0\mm }
  }
}

%% BOOK SL3 — THE FALSIFICATION. Both lyric rows reach the number's column and only the two
%% hypotheses disagree about which one gets it.
topWords = \lyricmode { one1 two three four five six sev ven }
lowWords = \lyricmode { aa1 bb cc dd ee ff gg hh }

\book {
  \probeSL "SL3"
  \score {
    <<
      \new Lyrics \with { \consists "Bar_engraver" } \topWords
      \new ChordNames \with { \consists "Bar_engraver" } \theChords
      \new Lyrics \with { \consists "Bar_engraver" } \lowWords
    >>
    \layout { indent = 0\mm }
  }
}

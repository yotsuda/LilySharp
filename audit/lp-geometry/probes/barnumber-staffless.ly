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
%% ─────────────────────────────────────────────────────────────────────────────────────
%% THE OFFSET (session 241). It is NOT one expression, it is TWO STAGES, and only their sum
%% is visible in the figures above. Books SLP and SLQ exist to separate them, and the dump
%% prints stage one on its own by re-invoking the Y-offset callback.
%%
%%   STAGE ONE — side position. LILYPOND-REF: lily/side-position-interface.cc
%%     Side_position_interface::aligned_side. dist = (support UP skyline).distance(my DOWN
%%     skyline); total = dist + padding * staff_space. side-support-elements is exactly
%%     `stavesFound' (lily/bar-number-engraver.cc:188-190), so a score with no staff has an
%%     EMPTY support set, and aligned_side's `if (dim.is_empty ())' branch replaces it with a
%%     flat skyline at height 0. Measured, to the digit:
%%       SLC  2.05 (StaffSymbol top) + 1.0 (padding) + 0.020473 (own ink bottom) = 3.070473
%%       SLN            0            + 1.0 (padding) + 0.020473                  = 1.020473
%%     So on a staffless system the number's INK BOTTOM lands exactly `padding' above the
%%     row's own reference point. There is no 3.05 and no half-staff anywhere in it.
%%
%%   STAGE TWO — the outside-staff pass. LILYPOND-REF: lily/axis-group-interface.cc
%%     avoid_outside_staff_collisions, reached from Axis_group_interface::skyline_spacing
%%     because BarNumber carries outside-staff-priority 100. It TRANSLATES the grob after the
%%     offset callback has run: move = (my DOWN skyline).distance(row's UP skyline) +
%%     outside-staff-padding, the padding defaulting to 0.46
%%     (Axis_group_interface::default_outside_staff_padding_).
%%       SLP (outside-staff-priority = ##f)  offset 1.020473 — stage one alone, exactly.
%%       SLQ (outside-staff-padding  = 0)    offset 1.125048 — SLN's 1.585048 less 0.46.
%%       SLN                                 offset 1.585048 = 1.020473 + 0.104576 + 0.46
%%     In SLC the pass finds nothing under the number's column and moves it by ZERO, which is
%%     why the staffful case looks like a single closed-form 3.05.
%%
%% ⚠️⚠️ AND THE 0.104576 IS NOT AN EXTENT — IT IS A GLYPH OUTLINE. The lyric row's X-extent
%% starts at 0.000000 and the number lives at x<0, so on extents the two are disjoint and the
%% pass would move nothing. They are not disjoint as SKYLINES: a text grob's vertical-skylines
%% come from the stencil's outline (lily/stencil-integral.cc Grob::vertical_skylines_from_stencil)
%% and are then padded by skyline-horizontal-padding, so the first syllable's UP skyline runs
%% out to x = -0.104739 and stands at 1.104576 by x = -0.004739 — dumped as `upsky' below.
%% The number's ink bottom sits at 1.000000, so the distance is 1.104576 - 1.000000 = 0.104576.
%% ⇒ THE STAFFLESS OFFSET IS NOT A CONSTANT AND NOT A FUNCTION OF EXTENTS. It is a function of
%% the shape of whatever ink is nearest the number's column, and it changes with the syllable.
%%
%% WHAT THIS MEANS FOR A PORT. Stage one and the anchor are pure arithmetic and portable as
%% they stand. Stage two is portable only to the precision of the skylines the renderer has:
%% with box skylines the two are disjoint, the pass moves nothing, and the number lands at
%% row refpoint + 1.020473 instead of 1.585048 — a residual of 0.564576 that is the
%% glyph-outline skyline debt itself, not a mistake in the anchor.
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
                                            'NONE))
                                ;; THE OFFSET QUESTION, SPLIT IN TWO. The number's final
                                ;; position is reached in two stages and only their SUM is
                                ;; visible above: Y-offset (side-position, using the support
                                ;; set and `padding') and then the outside-staff pass, which
                                ;; TRANSLATES the grob afterwards. Re-invoking the offset
                                ;; callback here reads stage one on its own, so the remainder
                                ;; is stage two by subtraction rather than by assumption.
                                (format #t "PROBESL ~a   BarNumber sidepos=~a osprio=~a ospad=~a hpad=~a\n"
                                        tag
                                        (ly:side-position-interface::y-aligned-side g)
                                        (ly:grob-property g 'outside-staff-priority)
                                        (ly:grob-property g 'outside-staff-padding)
                                        (ly:grob-property g 'horizon-padding))
                                ;; Stage two measures the number against the merged UP skyline
                                ;; of the OTHER elements of its row, so the whole element list
                                ;; is printed rather than the five grob names above: whatever
                                ;; reaches under the number's column is what decides.
                                ;; ⚠️ AND THE DECIDING INK IS NOT IN THE EXTENTS. Every element
                                ;; whose UP skyline has a vertex left of the number's right edge
                                ;; is printed with just those vertices, because a text grob's
                                ;; skyline follows the GLYPH OUTLINE and is then padded by
                                ;; skyline-horizontal-padding, so it reaches x<0 while its
                                ;; X-extent starts at 0. Reading extents alone hides the term.
                                (let* ((row (ly:grob-parent g Y))
                                       (els (ly:grob-object row 'elements))
                                       (edge (cdr (ly:grob-extent g sg X))))
                                  (if (ly:grob-array? els)
                                      (for-each
                                       (lambda (e)
                                         (let ((sp (ly:grob-property e 'vertical-skylines)))
                                           (if (and (not (eq? e g)) (pair? sp))
                                               (let* ((x0 (ly:grob-relative-coordinate e sg X))
                                                      (near (filter
                                                             (lambda (pt)
                                                               (and (not (inf? (cdr pt)))
                                                                    (< (+ x0 (cdr pt)) edge)))
                                                             (ly:skyline->points (cdr sp) Y))))
                                                 (if (pair? near)
                                                     (format #t "PROBESL ~a     upsky ~a x0=~a near=~a\n"
                                                             tag (grob-name e) x0 near))))))
                                       (ly:grob-array->list els)))
                                  (if (ly:grob-array? els)
                                      (for-each
                                       (lambda (e)
                                         (format #t "PROBESL ~a     rowelt ~a xext=(~a . ~a) yext=(~a . ~a) y=~a\n"
                                                 tag (grob-name e)
                                                 (car (ly:grob-extent e sg X))
                                                 (cdr (ly:grob-extent e sg X))
                                                 (car (ly:grob-extent e e Y))
                                                 (cdr (ly:grob-extent e e Y))
                                                 (ly:grob-relative-coordinate e row Y)))
                                       (ly:grob-array->list els))))))))
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

%% BOOK SLP — WHICH STAGE MOVES IT. SLN with outside-staff-priority switched off. If the
%% number then sits at exactly its side-position offset, the outside-staff pass is the mover
%% and the side-position arithmetic is complete on its own; if it does not move, the mover is
%% something else and the whole two-stage reading is wrong.
\book {
  \probeSL "SLP"
  \score {
    <<
      \new ChordNames \with { \consists "Bar_engraver" } \theChords
      \new Lyrics \with { \consists "Bar_engraver" } \theWords
    >>
    \layout { indent = 0\mm
              \context { \Score \override BarNumber.outside-staff-priority = ##f } }
  }
}

%% BOOK SLQ — AND BY HOW MUCH. SLN with outside-staff-padding pinned to 0. The default is
%% 0.46 (lily/axis-group-interface.cc default_outside_staff_padding_), so if the pass is the
%% mover this book must land exactly 0.46 lower than SLN, and whatever remains is the skyline
%% distance rather than the padding.
\book {
  \probeSL "SLQ"
  \score {
    <<
      \new ChordNames \with { \consists "Bar_engraver" } \theChords
      \new Lyrics \with { \consists "Bar_engraver" } \theWords
    >>
    \layout { indent = 0\mm
              \context { \Score \override BarNumber.outside-staff-padding = #0 } }
  }
}

%% BOOK SLT — DOES LILY#'S OWN DIVERGENCE REACH THE NUMBERED SYSTEM? A Lily# rows-only score
%% prints a TIME SIGNATURE on its rows (a deliberate divergence, HANDOFF §3 session 226) and
%% SLN has none, so before SLN's figures may be used to judge a Lily# book, the question has
%% to be asked on the system the number actually sits on. Both engravers are consisted and
%% break-visibility is forced to all-visible.
%%
%% MEASURED: the signature lands on system ONE only — system one's rows start at 4.100451 /
%% 3.000000 instead of 1.100452 / 0.000000, and the row VerticalAxisGroups reach down to
%% -1.000000 — while SYSTEM TWO is identical to SLN to every digit, rows at 1.237025 /
%% 0.000000 and the number at 1.585048 on the lyric row. LilyPond does not repeat a time
%% signature at a line break, and no override makes it: the Time_signature_engraver emits a
%% grob where the signature is SET, and break-visibility can only hide one, never conjure one.
%% ⇒ The divergence does not reach the numbered system, so SLN's reading stands. The Lily#
%% half was then read off Lily#'s own picture rather than assumed: the reported book
%% (RowsOnlySystemGapTests' Head, rendered 2026-08-24) prints NO time signature anywhere —
%% the SVG contains no timeSig glyph at all — so the two pictures agree on the system that
%% carries the number, and the divergence §3/226 records is not in this shape.
\book {
  \probeSL "SLT"
  \score {
    <<
      \new ChordNames \with { \consists "Bar_engraver"
                              \consists "Time_signature_engraver"
                              \override TimeSignature.break-visibility = #all-visible }
        { \time 4/4 \theChords }
      \new Lyrics \with { \consists "Bar_engraver"
                          \consists "Time_signature_engraver"
                          \override TimeSignature.break-visibility = #all-visible }
        \theWords
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

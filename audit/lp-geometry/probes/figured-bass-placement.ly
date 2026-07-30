\version "2.26.0"
%% LP FIDELITY PROBE — where FIGURED BASS sits under its staff, and WHICH STAFF that is
%% when the system has two. The corpus has no figured-bass point at all (no probe, no
%% ledger entry), and Lily# drops every figure in a system by ONE number.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe figured-bass-placement.ly (six tiny books).
%%
%% WHY THIS BOOK EXISTS
%%
%% Lily#'s FiguredBassEngraver.ApplySkylineDrop merges EVERY figure of a system into one
%% skyline, measures it against the SYSTEM's down-skyline, and lowers all of them by the
%% single resulting d. There is no staff in that sentence. Measured on the Lily# side alone
%% (session 42, output-invariant instrumentation), the same music with the same figures
%% reads FiguredBassLayout.YUp = -15.045 as a lone staff, -43.640 as the UPPER staff of two,
%% and -12.545 as the LOWER staff of two — the middle one is thrown below the whole system,
%% the same shape as the "lower-staff fermata flies over the top staff" defect of session 40,
%% and no committed fixture has that arrangement.
%%
%% ⚠️ THE PRIOR QUESTION THIS BOOK ANSWERS FIRST: WHICH LILYPOND DEVICE IS `@fig()`?
%% LilyPond has TWO, and Lily# currently spells half of each.
%%
%%   (L) THE FiguredBass CONTEXT — a LOOSE LINE. ly/engraver-init.ly:1108-1123 gives it
%%       \override VerticalAxisGroup.staff-affinity = #UP and
%%       \override VerticalAxisGroup.nonstaff-relatedstaff-spacing.padding = #0.5, and
%%       NOTHING ELSE: no basic-distance, no minimum-distance, no stretchability. Compare
%%       Lyrics (:649-652), which declares basic-distance 5.5. So a figured-bass line has
%%       no ideal to fall back on — the realized distance IS ink + 0.5, always, and the
%%       staff it hangs from is the one page-layout-problem.cc records as
%%       last_spaceable_line.
%%
%%   (S) THE Staff CONTEXT — BassFigureAlignmentPositioning, scm/define-grobs.scm:387-411
%%       (side-position-interface, outside-staff-interface): direction UP, padding 0.5,
%%       staff-padding 1.0, outside-staff-priority 25, add-stem-support #t. Its own
%%       description says it exists "if figured bass is used in the Staff context". This
%%       device is per-staff BY CONSTRUCTION — side_position_interface reads the grob's own
%%       staff symbol and its own note columns.
%%
%% Lily# takes StaffPadding = 1.0 from (S) — that number is BassFigureAlignmentPositioning's
%% staff-padding, not anything BassFigure declares, though the comment cites BassFigure — and
%% the drop machinery from (L), borrowed wholesale from the lyric engraver (SkylineDrop's
%% RelatedStaffPadding 0.5 is engraver-init.ly:1121 read through Lyrics' spelling). The
%% BelowStaffY = 5.0 that separates them is in neither: (L) has no basic-distance and (S)
%% has no fixed offset.
%%
%% ⇒ Both regimes are measured here, on the SAME music, because the port target depends on
%% which one `@fig()` means, and that must be argued from what the construct IS (a per-staff
%% note annotation, with no line-of-its-own in the grammar) rather than from whichever
%% number happens to be closer to today's output (HANDOFF §5.2).
%%
%% THE MUSIC (identical in all six books, on the staff that carries the figures): bass clef,
%% forced-DOWN-stem c, half notes — two ledger lines below the staff plus a stem hanging
%% below THAT, so the staff's down-skyline reaches well past the staff edge and neither
%% regime can sit on a floor (HANDOFF §5.0, "do not sit on the floor").
%%
%% THE THREE ARRANGEMENTS (per regime), which is the whole point:
%%   A — the figure-bearing staff ALONE.
%%   B — the figure-bearing staff is the UPPER of two.
%%   C — the figure-bearing staff is the LOWER of two — the shape of the committed fixture
%%       test/figbass-chordname-lower-staff.
%%
%% ⚠️ B AND C ARE THE SAME SCORE WITH THE FIGURES MOVED FROM ONE STAFF TO THE OTHER: the
%% companion staff carries the IDENTICAL deep-ink music, so the pair is an exact mirror and
%% the only difference between the two books is which staff owns the figures. That is
%% deliberate on both counts. The mirror makes LilyPond's side an IDENTITY by construction
%% (HANDOFF §5.0: the strongest pair shape), and the DEPTH of the companion's ink is what a
%% system-wide reading would wrongly pick up — a quiet companion would leave Lily#'s defect
%% visible but small, and a point whose signal is small leaves its regime on the next texture
%% edit.
%%
%% PREDICTIONS, written before running (HANDOFF §5.0, with signs and a fork):
%%   * REGIME S: FBSA == FBSB == FBSC to the digit. side-position resolves against the
%%     grob's own staff symbol and its own note columns; a second staff is not in that
%%     sentence at any point. If this identity BREAKS, the reading is wrong, not LilyPond
%%     — treat the whole S regime as unmeasured and find out what else moved.
%%   * REGIME L: FBLA == FBLC predicted (the line hangs from the staff above it, and in both
%%     books that staff is the figure-bearing one, sitting at the bottom of its system).
%%     FBLB is THE FORK — the line is now BETWEEN two staves:
%%       - if FBLB == FBLA, LilyPond hangs it from its own staff and the second staff only
%%         moves further away; Lily#'s system-wide drop is a plain defect and the port is
%%         to give the drop a staff.
%%       - if FBLB != FBLA, distribute_loose_lines is solving a chain across the gap
%%         (nonstaff-relatedstaff up, nonstaff-unrelatedstaff 0.5 down,
%%         scm/define-grobs.scm:4240) and the port needs that chain, not an attribution fix.
%%     Either way the number is measured, and either way the next piece of work is decided
%%     the moment it prints.
%%   * MAGNITUDE, both regimes: the figures land at (deep column ink) + 0.5 + (the top
%%     figure's own ink above its baseline). Bass-clef c, is drawn at staff position -6, so
%%     its head bottom is about 3 + 0.545 = 3.545 below the staff centre and the forced-down
%%     stem reaches roughly 3.5 further; the reading should be somewhere near 7 to 8 below
%%     the centre line, NOT the 4.0 Lily# computes before its drop (2.0 - (5.0 + 1.0)) and
%%     nowhere near the 15.045 it computes after.
%%   * SIGN vs Lily#, certain in advance for B: Lily# throws the upper staff's figures below
%%     the WHOLE system, so |FBLB residual| must be the largest of the three whichever
%%     regime turns out to be the counterpart.
%%   * FALSIFIER for the arrangement itself: if the two-staff books read the SAME staff
%%     refpoint pattern as the one-staff book (i.e. only one StaffSymbol prints), the second
%%     staff was removed by hara-kiri and B and C measure nothing.
%%
%% MEASURED (2026-07-30, session 43; the full record is in the ledger `why`s).
%%   * ALL SIX BOOKS read 8.124795235605315 from the figure-bearing staff's centre line down
%%     to the top figure's baseline. Both predictions held, and the FORK resolved to its
%%     FIRST branch: LilyPond hangs the line from its OWN staff, so Lily#'s system-wide drop
%%     is a plain defect and the port is an attribution fix, not a chain.
%%   * THE DECOMPOSITION, every term dumped rather than derived: the NoteColumn's own ext
%%     about the staff refpoint is (-6.500000 . -3.455) and the top figure's ink top sits at
%%     exactly -7.000000 = column ink bottom 6.5 + the 0.5 both devices declare. The baseline
%%     is a further 1.124795235605315 = the BassFigure's own Y-extent.
%%   * THE TWO REGIMES AGREE to fifteen digits, so `@fig()`'s port target can be argued from
%%     what the construct is rather than from which number is closer. They are NOT the same
%%     device: in the Staff-context books each figure column is side-positioned
%%     INDEPENDENTLY (columns 2..4 sit 0.002267 higher, aligning their INK TOPS at a constant
%%     height), where the loose line puts every column on ONE baseline. Lily#, which gives
%%     the whole row one Y, is shaped like the loose line.
%%   * THE STAFF GAP is where the two-staff books differ, and it is a SECOND quantity:
%%     12.174795235605316 in FBLB against 9.550000 in FBLC. The difference is the row itself
%%     — lowest figure baseline 9.624795235605315 below its staff + nonstaff-unrelatedstaff
%%     padding 0.5 (scm/define-grobs.scm:4240, the one member FiguredBass does not override)
%%     + the lower staff's ink 2.05. FBLC's 9.55 is the plain staff-staff spring
%%     (column ink 6.5 + default-staff-staff-spacing padding 1 + ink 2.05, basic-distance 9
%%     losing) and Lily# reads it EXACT.
%%   * Lily# reads 8.500000 / 18.050000 / 8.500000 and 9.550000 / 9.550000: the row is placed
%%     against the SYSTEM's lowest ink and no room is reserved for it between staves.
%%   * THE FACE (book FBLN, added after the port): LilyPond's figures are `\number` markup —
%%     scm/translation-functions.scm:349-362 format-bass-figure builds them with
%%     make-number-markup, and scm/define-markup-commands.scm:3872-3878 says what that is:
%%     "the (music) font for numbers … also contains symbols for figured bass". So the digits
%%     are EMMENTALER NUMBER GLYPHS, where Lily# draws a serif TEXT face at an em of its own
%%     (SharedRenderer.FiguredBassFontSize = 3.0, whose real digit ink is 2.112000 against
%%     LilyPond's 1.124795235605315). NEITHER grob declares a font-size — checked, TimeSignature
%%     and BassFigure both leave it unset — and the numeric time signature dumped alongside it
%%     (ext -2.0 . 2.004019, i.e. ~2.004 per digit) is NOT a second size of the same thing, so
%%     the ratio between them is not a lever. ⚠️ THE RATIO IS THEREFORE NOT A PORT TARGET.
%%
%%   ⚠️ CORRECTED 2026-07-30 (session 44), AND THE CORRECTION IS THE PORT. This book's own
%%     conclusion above — "so the figure is that face at font-size 0" — was wrong twice, and
%%     reading the chain in LilyPond rather than inferring it from the dump is what fixed it:
%%       (a) THE FIGURE IS NOT AT font-size 0. scm/translation-functions.scm:468-470 ends
%%           format-bass-figure with (make-fontsize-markup -5 fig-markup), so the step is
%%           carried by the MARKUP, which is exactly why the grob property dumps as unset.
%%       (b) THE NUMBER FACE IS NOT ON THE TEXT LADDER. lily/font-select.cc:99-117 takes the
%%           base size for fetaText from STAFF-HEIGHT (text-font-size is latin1's branch), so
%%           \number at font-size 0 is the MUSIC em, 4 ss, and a figure is 4 * magstep(-5) =
%%           2.244924096 ss. The handoff's chain guess (the paper's 2.2 ss, from the lyric em)
%%           would have been 2% small.
%%       (c) AND THE DIGITS ARE A DIFFERENT CUT from the time signature's: BassFigure declares
%%           font-features ("tnum" "cv47" "ss01") (scm/define-grobs.scm:354) and those are
%%           substitutions, so the glyph is fattened.fixedwidth.<digit> (.alt for 4 and 7)
%%           where a time signature, declaring no features, draws the base digits. That is the
%%           real reason the ratio is not a lever — not a scaling print routine.
%%     ⇒ PORTED: the figures are drawn from those glyphs at that em and the reservation takes
%%     its cap from the same outline (Svg.Layout.FiguredBassGlyphRun). All four baseline points
%%     landed together at -0.002333187, which is the emmentaler-11-vs-20 optical size (LilyPond
%%     picks the design size nearest 11.2246pt; Lily# bundles only -20) — see the ledger.
%%   * THE QUIET BOOKS (FBLQ / FBSQ, added the same day for the port): 3.674795235605315 =
%%     staff ink 2.05 + padding 0.5 + the digit's 1.124795235605315, i.e. the ink top at
%%     2.550000. So staff-padding 1.0 is include_staff and NOT a refpoint floor — and it can
%%     never be one for this grob, since the top digit's cap exceeds staff-padding − padding.
%%     Lily# reads 4.050000, the SAME cap term again: its placement already agrees with
%%     LilyPond in BOTH regimes on a lone staff, so what the port has to fix is the drop's
%%     FRAME (which staff's ink it reads, and reserving the row's room), not its arithmetic.
%%
%% WHAT IS DUMPED: every StaffSymbol (each staff's own centre line, about the system origin)
%% and every BassFigure / BassFigureLine / BassFigureAlignmentPositioning / NoteColumn / Stem,
%% with rel = Y about the system origin and ext = its own Y-extent about that rel. The
%% distance the ledger wants is StaffSymbol.rel - BassFigure.rel for the figure-bearing staff;
%% dumping the staff symbols rather than staff-refpoint-extent means the two-staff books
%% cannot silently pair a figure with the wrong staff. The NoteColumn rides along so the
%% decomposition reads its ink bottom instead of inferring one from head position and stem
%% length: a sum of three plausible terms can be made to close on the wrong mechanism
%% (HANDOFF §5.0, the TXW decomposition that was corrected term by term).
%%
%% ⚠️ The serif face is pinned for the same reason page-vertical.ly pins it: the svg backend
%% resolves fonts.serif through this machine's fontconfig otherwise, and BassFigure is text.

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
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEV SYS ~a ~a y=~a staff=(~a . ~a)\n"
                           n i
                           (ly:prob-property sys 'Y-offset 0.0)
                           (car staff) (cdr staff))
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(StaffSymbol BassFigure BassFigureLine
                                                   BassFigureAlignmentPositioning
                                                   NoteColumn Stem TimeSignature))
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=~a fs=~a fam=~a\n"
                                                n i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (car (ly:grob-extent g g X)))
                                                (ly:grob-property g 'font-size 'unset)
                                                (ly:grob-property g 'font-family 'unset)))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% The figure-bearing staff's music: two ledger lines below the bass staff, stems forced
%% down so the column's ink reaches past the head as well. The companion staff in the
%% two-staff books plays the SAME music (see the mirror note above), so `figuredMusic` is
%% used twice rather than a second variable being spelled.
figuredMusic = { \clef bass \stemDown c,2 c, | c,2 c, \bar "|." }

%% THE QUIET TEXTURE (books FBLQ / FBSQ, added for the port rather than for the arrangement
%% claim): middle-line d with the stems forced UP, so the column's lowest ink is the
%% notehead's own bottom (0.545 under the middle line) and the STAFF's ink is the deepest
%% thing there is. That is the regime in which staff-padding 1.0 can bind, and without it
%% the port would have to guess how the floor is spelled — the trill island's TRF/TRC pair
%% in its figured-bass form.
quietMusic = { \clef bass \stemUp d2 d | d2 d \bar "|." }

theFigures = \figuremode { <5 3>2 <6> | <7>2 <6 4> }

%% ---------------------------------------------------------------------------------------
%% REGIME L — the FiguredBass CONTEXT (a loose line, staff-affinity UP, padding 0.5 only).
%% ---------------------------------------------------------------------------------------

%% FBLA — the figure-bearing staff ALONE.
\book {
  \probeTag "FBLA"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff \figuredMusic
      \new FiguredBass \theFigures
    >>
  }
}

%% FBLB — THE FORK: the figures belong to the UPPER of two identical staves.
\book {
  \probeTag "FBLB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff \figuredMusic
      \new FiguredBass \theFigures
      \new Staff \figuredMusic
    >>
  }
}

%% FBLC — the mirror of FBLB: the figures belong to the LOWER of the same two staves
%%     (the committed fixture's arrangement).
\book {
  \probeTag "FBLC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff \figuredMusic
      \new Staff \figuredMusic
      \new FiguredBass \theFigures
    >>
  }
}

%% FBLN — THE FACE BOOK (added for the cap debt, not for the arrangement claim). FBLA with
%%     \numericTimeSignature, so the SAME number font that draws the figures also draws a
%%     time signature whose size is a known one. LilyPond's bass figures are `\number`
%%     markup — scm/translation-functions.scm:349-362 format-bass-figure builds them with
%%     make-number-markup — i.e. Emmentaler's NUMBER face, where Lily# draws them with a
%%     serif text face at an em of its own. The two digit inks dumped side by side say what
%%     the figure's size is RELATIVE to a size the corpus already reproduces, which is what
%%     a port needs: the ratio names the font-size step to go looking for, and no constant
%%     may be fitted to the figure's ink directly.
\book {
  \probeTag "FBLN"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { \numericTimeSignature \figuredMusic }
      \new FiguredBass \theFigures
    >>
  }
}

%% FBLQ — THE QUIET CONTROL: the same lone staff with the column's ink pulled back inside
%%     the staff, so whatever floors the row when no note reaches for it is what reads.
\book {
  \probeTag "FBLQ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff \quietMusic
      \new FiguredBass \theFigures
    >>
  }
}

%% ---------------------------------------------------------------------------------------
%% REGIME S — the Staff CONTEXT (BassFigureAlignmentPositioning, side-position). direction
%% is forced DOWN so both regimes place the figures on the same side of the staff and the
%% two numbers are comparable; UP is only the grob's default, not part of what is measured.
%% ---------------------------------------------------------------------------------------

%% FBSA — the figure-bearing staff ALONE.
\book {
  \probeTag "FBSA"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff << \figuredMusic \theFigures >>
    \layout {
      \context {
        \Staff
        \consists "Figured_bass_engraver"
        \override BassFigureAlignmentPositioning.direction = #DOWN
      }
    }
  }
}

%% FBSB — the figures belong to the UPPER of two identical staves.
\book {
  \probeTag "FBSB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff << \figuredMusic \theFigures >>
      \new Staff \figuredMusic
    >>
    \layout {
      \context {
        \Staff
        \consists "Figured_bass_engraver"
        \override BassFigureAlignmentPositioning.direction = #DOWN
      }
    }
  }
}

%% FBSC — the mirror of FBSB: the figures belong to the LOWER of the same two staves.
\book {
  \probeTag "FBSC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff \figuredMusic
      \new Staff << \figuredMusic \theFigures >>
    >>
    \layout {
      \context {
        \Staff
        \consists "Figured_bass_engraver"
        \override BassFigureAlignmentPositioning.direction = #DOWN
      }
    }
  }
}

%% FBSQ — the quiet control in the side-position regime, where staff-padding 1.0 is
%%     declared and can bind. The pair FBSQ/FBSA is what says HOW the floor is spelled:
%%     a refpoint floor at staff ink + staff-padding, or the staff extent entering the
%%     SUPPORT (include_staff) over which the grob then pays its own padding — the two
%%     differ by exactly (staff-padding - padding) = 0.5 here.
\book {
  \probeTag "FBSQ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff << \quietMusic \theFigures >>
    \layout {
      \context {
        \Staff
        \consists "Figured_bass_engraver"
        \override BassFigureAlignmentPositioning.direction = #DOWN
      }
    }
  }
}

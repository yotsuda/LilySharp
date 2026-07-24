\version "2.26.0"
%% LP FIDELITY PROBE — the line-start min_dist (Paper_column::minimum_distance).
%%
%% WHY. Staff_spacing::get_spacing floors the line-start spring's FIXED distance at
%% 0.3 + min_dist (lily/staff-spacing.cc:210-215), and on a notation+tab score that
%% floor is what binds the tab staff's wish (6.0 -> 8.02), hence the merged 8.42 that
%% the tab-key ledger pair is 0.4 short of. So min_dist is the FIRST thing the
%% merge_springs port needs, and it must be measured before any spacing code moves.
%%
%%   min_dist = Paper_column::minimum_distance (left_col, right_col)  paper-column.cc:145-164
%%            = skys[LEFT].distance (skys[RIGHT]), where
%%              skys[LEFT]  = left_col 's  horizontal-skylines[RIGHT]
%%              skys[RIGHT] = right_col's  horizontal-skylines[LEFT]  merged with
%%                            Separation_item::conditional_skyline (right, left)
%%
%% WHAT THIS PROBE ASKS. A PaperColumn's horizontal-skylines is
%% ly:separation-item::calc-skylines (define-grobs.scm:2523), i.e.
%% Separation_item::boxes (separation-item.cc:120-190) — which reads each element's
%% grob EXTENT (il->extent (pc, X_AXIS), il->pure_y_extent) widened by
%% extra-spacing-width / extra-spacing-height. It never touches a glyph outline. If
%% that reading is right, the dumped skyline is a union of RECTANGLES: its point list
%% has only axis-parallel steps and every corner lands on some element's extent.
%% If instead the column skyline followed the clef's real outline (the way
%% Accidental_interface::horizontal_skylines does for a single accidental), the point
%% list would trace the glyph's curve in dozens of segments.
%%
%% The distinction decides a whole session of work: baking outline skylines for the
%% clef / time-signature / TAB clef glyphs is only worth doing if LilyPond uses them
%% HERE. Being more precise than LilyPond is a defect like being less precise.
%%
%% Predictions, written BEFORE the dump (the two oracles the handoff derived from the
%% merge_springs equations, tab wish 8.02 = max (6.0, 0.3 + min_dist)):
%%     TKC  (notation + tab, plain first note)          min_dist = 7.720000
%%     TKA  (same, sharp on the notation staff's first)  min_dist = 9.270000
%%   and the 1.55 between them = the accidental's 1.45 left overhang + the 0.1
%%   extra-spacing-width delta (NoteHead -0.1 vs Accidental -0.2) — a decomposition
%%   only the BOX reading produces, since an outline skyline would let the notehead
%%   nest into the sharp's notches instead of clearing its bounding box.
%%
%% WHAT THE DUMP SAID (2026-07-24, LilyPond 2.26.0).
%%   * TKC MINDIST = 7.720000, the derived oracle confirmed by direct measurement.
%%   * The prefatory column's RIGHT skyline is SEVEN buildings, every one of them at a
%%     CONSTANT x, and each x is exactly some element's extent plus its
%%     extra-spacing-width:
%%         3.465000 = notation Clef  0.800..3.365 + esw 0.1   (default esw -0.1 . 0.1)
%%         3.700000 = TAB      Clef  1.000..3.600 + esw 0.1
%%         7.620000 = notation TimeSignature 5.120..6.820 + esw 0.8
%%     A treble clef's outline would be dozens of buildings tracing its curve. It is
%%     BOXES. Baking outline skylines for the clef / time-signature / TAB clef glyphs
%%     is NOT what LilyPond does here, and doing it would be an invention.
%%   * The binding pair is on the NOTATION staff: TimeSignature right 7.620000 against
%%     NoteHead left 0.000 + esw -0.1 = -0.100000, i.e. 7.720000 exactly.
%%   * The prefatory boxes are stretched vertically to their own staff's extent by
%%     extra-spacing-height (TimeSignature: pure-from-neighbor-interface::extra-
%%     spacing-height-including-staff, dumped as -2.545 . 1.050), which is why the
%%     notation TimeSignature spans y -7.345 .. -1.750 and meets the note at any pitch,
%%     and why the two staves' boxes do not overlap each other.
%%   * TKA MINDIST also prints 7.720000 — as predicted, because conditional_skyline is
%%     not reachable from Scheme. The dumped Accidental box IS: x -1.450..-0.350 with
%%     esw -0.2 . 0.0, so the real min_dist is 7.620 - (-1.650) = 9.270000, the second
%%     oracle, reconstructed from boxes alone. The accidental therefore reaches
%%     min_dist ONLY through the conditional merge, which the port must have.
%%
%% THE MODEL, closed (every number below is dumped by this probe, staff-local with the
%% middle line at 0 — LeftEdge's pure height gives the frame offset, -3.800000 here).
%%
%%   box.X = extent relative to the COLUMN, widened by extra-spacing-width:
%%     Clef          esw default (-0.1 . 0.1)   G clef ink 0.800..3.365, TAB 1.000..3.600
%%     KeySignature  esw (0.0 . 1.0)
%%     TimeSignature esw (0.0 . 0.8)
%%     NoteHead      esw default                ink 0.000..1.304200 from the column origin
%%     Accidental    esw (-0.2 . 0.0)           ink -1.450..-0.350, via conditional_skyline
%%
%%   box.Y = own pure height widened by extra-spacing-height, and for the PREFATORY
%%   grobs that height is stretched to cover the grobs it is measured against:
%%     item::extra-spacing-height-including-staff (output-lib.scm:900-910) stretches the
%%       box to the StaffSymbol's extent, and
%%     pure-from-neighbor-interface::extra-spacing-height (:934-942) stretches it to the
%%       union with its NEIGHBOURS -- the pure-relevant items in the adjacent columns
%%       (pure-from-neighbor-engraver.cc:110-137), i.e. the first note column itself.
%%     Checked exactly: SKC's TimeSignature own -1.000..1.000, neighbours -3.545..2.050,
%%     so esh = (-2.545 . 1.050) as dumped; the Clef's own -3.550..3.800 already covers
%%     them, so its esh is (0 . 0) as dumped.
%%   CONSEQUENCE: at a line start every prefatory box vertically covers its own staff's
%%   first note column, so the skyline distance degenerates to a per-staff difference of
%%   REACHES (max prefatory right+esw, min note-column left+esw) with no Y question. The
%%   esh values are identical in SKC and TKC, which is the measurement that the neighbour
%%   set is per-STAFF and does not reach across staves.
%%   The musical column also carries skyline-vertical-padding 0.08 (define-grobs.scm:2747;
%%   the non-musical one has none), which pads along Y only OUTSIDE the range the
%%   prefatory box already covers, and at 45 degrees inward -- so it cannot raise this
%%   distance. Recorded, not modelled.
%%
%% FOUR NUMBERS a port must reproduce:
%%     SKC  one notation staff              7.485000 = (6.585 + 0.8) - (0.000 - 0.1)
%%     SKD  the same with \key d \major    10.135000 = (9.235 + 0.8) - (0.000 - 0.1)
%%          -- the key is SHADOWED (it sits left of the meter); predicted before the dump
%%     TKC  notation + tab                  7.720000 = (6.820 + 0.8) - (0.000 - 0.1)
%%          -- the meter is 0.235 further right because the TAB clef widens the Clef group
%%     TKA  TKC with a sharp                9.270000 = (6.820 + 0.8) - (-1.450 - 0.2)
%%          -- reconstructed: conditional_skyline is not reachable from Scheme
%%
%% Not ledger points: Lily# has no min_dist to compare against yet. These are model
%% checks, like TM3/TM4 in barline-spacing.ly — how the next session re-derives the
%% numbers instead of trusting them.
%%
%% Dumps go to STDOUT, ONE RECORD PER LINE (a split record gets cut in half by
%% LilyPond's own diagnostics on stderr — see the note in barline-spacing.ly).

\header { tagline = ##f }

#(define probe-done (make-hash-table))

#(define (nf x)
   (cond ((not (real? x)) "?")
         ((inf? x) (if (> x 0) "+inf" "-inf"))
         (else (format #f "~,6f" x))))

#(define (pts->string sky)
   (string-join
    (map (lambda (p) (format #f "~a/~a" (nf (car p)) (nf (cdr p))))
         (ly:skyline->points sky Y))
    " "))

#(define (grobs-of col sym)
   (let ((ga (ly:grob-object col sym #f)))
     (if (ly:grob-array? ga) (ly:grob-array->list ga) '())))

#(define (elements-of col) (grobs-of col 'elements))

%% conditional-elements holds the AccidentalPlacement (and any Arpeggio); the boxes
%% that reach conditional_skyline are the individual Accidental grobs INSIDE it
%% (separation-item.cc:143 get_relevant_accidentals), which is why this walks one
%% level down. The Accidental grobs are deliberately absent from 'elements
%% (paper-column-engraver.cc:259).
#(define (accidentals-of ap)
   (let ((al (ly:grob-object ap 'accidental-grobs #f)))
     (if (list? al)
         (append-map (lambda (entry)
                       (cond ((not (pair? entry)) '())
                             ((ly:grob-array? (cdr entry))
                              (ly:grob-array->list (cdr entry)))
                             ((list? (cdr entry)) (cdr entry))
                             (else '())))
                     al)
         '())))

#(define (conditional-boxes-of col)
   (append-map (lambda (ap) (cons ap (accidentals-of ap)))
               (grobs-of col 'conditional-elements)))

#(define (dump-grobs tag which col grobs)
   (for-each
    (lambda (e)
      (let ((xe (ly:grob-extent e col X))
            (se (ly:grob-extent e e X))
            (sx (ly:grob-relative-coordinate e (ly:grob-system e) X))
            ;; py = the PURE Y extent boxes() actually reads (separation-item.cc:163),
            ;; taken against the system so every staff's boxes land in one frame — the
            ;; same frame the dumped skyline points are in.
            (py (ly:grob-pure-height e (ly:grob-system e) 0 INFINITY-INT))
            (esw (ly:grob-property e 'extra-spacing-width))
            (esh (ly:grob-property e 'extra-spacing-height)))
        ;; x   = extent relative to the COLUMN — what Separation_item::boxes reads.
        ;; sx  = the grob's own reference point in the system. For a Clef that anchor is
        ;;       its ink RIGHT edge (break-align-anchor-alignment . RIGHT), so sx and
        ;;       self are what say where the ink is versus where the column thinks it is.
        (format #t "\nPROBE ~a ELEM ~a name=~a x=~a..~a sx=~a self=~a..~a py=~a..~a esw=~a esh=~a\n"
                tag which (grob::name e) (nf (car xe)) (nf (cdr xe))
                (nf sx) (nf (car se)) (nf (cdr se))
                (nf (car py)) (nf (cdr py))
                (if (pair? esw) (format #f "~a..~a" (nf (car esw)) (nf (cdr esw))) "default")
                (if (pair? esh) (format #f "~a..~a" (nf (car esh)) (nf (cdr esh))) "default"))))
    grobs))

#(define (dump-elements tag which col)
   (dump-grobs tag which col (elements-of col))
   (dump-grobs tag (string-append which "COND") col (conditional-boxes-of col)))

#(define ((dump-mindist tag) g)
   (if (not (hash-ref probe-done tag #f))
       (begin
         (hash-set! probe-done tag #t)
         (let* ((sys (ly:grob-system g))
                (cols (ly:grob-array->list (ly:grob-object sys 'columns)))
                (musical? (lambda (c)
                            (grob::has-interface c 'musical-paper-column-interface)))
                (cmd (find (lambda (c) (not (musical? c))) cols))
                (mus (find musical? cols)))
           (if (and (ly:grob? cmd) (ly:grob? mus))
               (let* ((lp (ly:grob-property cmd 'horizontal-skylines))
                      (rp (ly:grob-property mus 'horizontal-skylines))
                      (l-right (cdr lp))    ; skyline-pair is (LEFT . RIGHT)
                      (r-left  (car rp)))
                 ;; conditional_skyline is NOT reachable from Scheme, so this is
                 ;; minimum_distance WITHOUT the conditional merge. On these scores the
                 ;; only conditional elements are the notation staff's accidentals,
                 ;; which is exactly what TKA adds — so TKA's number tells us whether
                 ;; the accidental reaches min_dist through the merge or not.
                 (format #t "\nPROBE ~a MINDIST d=~a\n"
                         tag (nf (ly:skyline-distance l-right r-left)))
                 (format #t "\nPROBE ~a SKYRIGHT n=~a pts=~a\n"
                         tag (length (ly:skyline->points l-right Y)) (pts->string l-right))
                 (format #t "\nPROBE ~a SKYLEFT n=~a pts=~a\n"
                         tag (length (ly:skyline->points r-left Y)) (pts->string r-left))
                 (dump-elements tag "CMD" cmd)
                 (dump-elements tag "MUS" mus))))))
   '())

%% The staff a grob sits on, so the TAB clef's ink can be compared against the tab staff
%% it is drawn onto rather than against a notation staff.
#(define ((dump-staff tag) g)
   (let ((ss (ly:grob-object g 'staff-symbol #f)))
     (if (ly:grob? ss)
         (let ((ye (ly:grob-extent ss (ly:grob-system g) Y))
               (ce (ly:grob-extent g (ly:grob-system g) Y)))
           (format #t "\nPROBE ~a STAFF space=~a lines=~a staffY=~a..~a clefY=~a..~a\n"
                   tag
                   (nf (ly:staff-symbol-staff-space g))
                   (ly:grob-property ss 'line-count)
                   (nf (car ye)) (nf (cdr ye)) (nf (car ce)) (nf (cdr ce))))))
   '())

lay =
#(define-scheme-function (tag) (string?)
   #{
     \layout {
       ragged-right = ##t
       line-width = 500\mm
       indent = 0
       \context {
         \Score
         %% Both, because a tab-only score has no NoteHead to hang the dump on. The
         %% hash guard makes whichever fires first the one that dumps.
         \override NoteHead.after-line-breaking = #(dump-mindist tag)
         \override Clef.after-line-breaking = #(dump-mindist tag)
       }
     }
   #})

%% TKC — the notation+tab score the tab-key ledger pair is measured on. Its prefatory
%%   column holds the notation staff's Clef/KeySignature/TimeSignature AND the tab
%%   staff's TAB clef (plus its stencil-less TimeSignature); the first musical column
%%   holds one notehead per staff plus the tab staff's fret numbers.
\score { <<
  \new Staff { \key c \major \time 4/4 c'4 d' e' f' | g'2 e' }
  \new TabStaff { \key c \major c4 d e f | g2 e }
>> \lay "TKC" }

%% SKC — the SINGLE notation staff. Not about merge_springs at all: it asks whether the
%%   min_dist floor binds where there is nothing to merge.
%%   PREDICTED 7.720000 (TKC's binding pair is already notation-internal, so dropping the
%%   tab staff should change nothing). MEASURED 7.485000 — the prediction was wrong, and
%%   the miss is the useful part: TKC's TimeSignature right edge is 7.620000 but SKC's is
%%   7.385000, i.e. 0.235000 less, which is exactly the TAB clef's ink 3.600 minus the G
%%   clef's 3.365. The TAB clef is IN the shared Clef break-align group and is WIDER than
%%   the G clef, so it pushes the meter column right (3.600 + 1.52 = 5.120 against 3.365 +
%%   1.52 = 4.885) and min_dist with it. TKC's 7.720000 therefore carries 0.235 of TAB
%%   clef in it, and the MaxClefWidth staff set (handoff section 2A) is not a separate
%%   defect from this one — it is the same number.
%%   The floor still binds here, which was the question: fixed = 6.585 + 2.0/2 = 7.585
%%   against 0.3 + 7.485 = 7.785, so staff-spacing.cc:213 lifts fixed by 0.2. ideal stays
%%   4.885 + 3.700 = 8.585 (the max at :215, and the ledger's KCS 3.700000 confirms it),
%%   which is why force-0 output does not move. What moves is the spring's FIXED end and
%%   hence its compressibility — so this floor is not a multi-staff curiosity. It is
%%   missing in Lily# everywhere, and invisible only because the corpus measures at
%%   force 0.
\score { \new Staff { \key c \major \time 4/4 c'4 d' e' f' | g'2 e' } \lay "SKC" }

%% SKD — SKC with a key signature, so the KeySignature box (esw 0.0 . 1.0) is in the
%%   column too. Prediction: the key is SHADOWED — it sits LEFT of the meter, and the
%%   meter's own right reach is further right still, so min_dist should be the meter's
%%   again, just displaced by the key column's width. It is the negative control for
%%   "which grob binds": if a keyed score's min_dist ever came from the key, the reach
%%   model would be wrong.
\score { \new Staff { \key d \major \time 4/4 d'4 e' fis' g' | a'2 fis' } \lay "SKD" }

%% CGT / CGP — WHERE THE CLEF GROUP SITS when its staves' clefs have DIFFERENT stencil
%%   left edges. TKC showed the notation clef's ink at 0.800 and the TAB clef's at 1.000,
%%   i.e. they are NOT both flush to the LeftEdge->clef 0.8. The rule that explains it is
%%   break-alignment's own: the offset is
%%     extents[LeftEdge][RIGHT] + 0.8 - extents[clef group][LEFT]
%%   (lily/break-alignment-interface.cc:242), where the group extent is the UNION across
%%   staves (:141-142). So the GROUP's left ink lands on 0.8 and each clef keeps its own
%%   stencil offset inside it. LILC stencil lefts: G 0.000, TAB 0.200, percussion 0.670.
%%
%%   Predictions, written BEFORE the dump:
%%     CGT (tab staff ALONE)   group left 0.200 => the grob sits at 0.8 - 0.2 = 0.600
%%                             and the TAB clef's ink is 0.800..3.400 -- NOT TKC's
%%                             1.000..3.600, because with no notation clef in the group
%%                             there is nothing holding the group's left at 0.
%%     CGP (percussion + treble)  group left min(0.670, 0.000) = 0 => the group sits at
%%                             0.800, so the PERCUSSION clef's ink is 0.670 further in, at
%%                             1.470..2.800, and it is NOT flush at 0.800. On a
%%                             percussion-ONLY score it would be (group left 0.670 => grob
%%                             at 0.130, ink 0.800..2.130), which is the 0.13 origin an
%%                             earlier session measured -- so a per-CLEF "put my ink-left
%%                             at 0.8" rule and this per-GROUP rule agree on every score
%%                             with one kind of clef and disagree only here.
%%   Lily# implemented the per-CLEF rule (GlyphMetrics.ClefInkLeft, and MaxClefWidth as a
%%   max of ink WIDTHS), so these two scores are where it must diverge.
%%
%%   MEASURED, both predictions exact:
%%     CGT  Clef sx=0.600000  self=0.200..2.800  => ink 0.800000..3.400000
%%     CGP  percussion Clef sx=0.800000  self=0.670..2.000 => ink 1.470000..2.800000
%%          treble     Clef sx=0.800000  self=0.000..2.565 => ink 0.800000..3.365000
%%   So the anchor is the GROUP's left ink edge, and each clef keeps its own stencil
%%   offset inside it. Ported in SpacingRules.ClefGroupExtent / DrawClef; CGP is the pair
%%   that made the per-clef rule falsifiable, since it is the only shape where the two
%%   rules disagree.
%% CGT also carries the TAB CLEF vs TAB STAFF comparison Lily# has never made. LilyPond's
%% TabStaff sets StaffSymbol.staff-space = 1.5 (ly/engraver-init.ly), clefGlyph
%% "clefs.tab" and clefPosition 0, and does NOT scale the glyph with the staff-space --
%% TKC already showed its ink 2.6 wide, the bare LILC width. Predictions, before the dump:
%%     space = 1.500000, lines = 6, so the staff is 5 * 1.5 = 7.500000 tall
%%     the clef's own ink is 5.760000 tall (LILC -2.88 . 2.88), UNSCALED and centred on
%%     the middle line -- i.e. SMALLER than the staff, not fitted to it.
%% Lily# does neither: TabStringSpace(6) = 1.3 makes its six-string staff 6.5 tall, and
%% SharedRenderer.Tab scales clefs.tab by tabHeight/5.78 so the clef nearly FILLS the
%% staff (width 2.6 * 1.1246 = 2.924) and draws it at systemStartX, not on the
%% LeftEdge->clef 0.8 column.
%%
%%   MEASURED: space=1.500000 lines=6 staffY=-7.600000..0.000000 clefY=-6.680000..-0.920000
%%   The staff spans 7.600000 = 5 * 1.5 + 0.1, the 0.1 being the outer lines' own thickness
%%   (Lily#'s LineThickness is the same 0.1). The clef spans 5.760000 -- the bare LILC
%%   height -- centred on the middle line at -3.8. Both predictions hold.
%%
%%   So Lily#'s tab geometry differs from LilyPond's in four ways at once:
%%     string space   LP 1.5 for every string count   Lily# 1.3 / 1.4 / 1.5 by count
%%     staff height   LP 5 * 1.5 = 7.5 lines          Lily# 5 * 1.3 = 6.5
%%     clef size      LP unscaled 2.6 x 5.76          Lily# scaled by staffHeight/5.78
%%     clef X         LP the Clef break-align column  Lily# systemStartX (no 0.8)
%%   The last two are why the TAB clef cannot join the clef group until they are fixed:
%%   the group would book LilyPond's 2.8 while the renderer drew 2.924 somewhere else.
\score { \new TabStaff \with {
  \override Clef.after-line-breaking = #(dump-staff "CGT")
} { \key c \major c4 d e f | g2 e } \lay "CGT" }
%% CG4 — a FOUR-string tab staff (bass), which is what most of Lily#'s tab corpus is.
%%   The 6-string dump says the clef is unscaled at 5.760000 tall while the staff spans
%%   7.600000, so it fits. A 4-string staff spans only 3 * 1.5 + 0.1 = 4.600000. Does
%%   LilyPond shrink the clef to fit, or does it let a glyph designed for six strings
%%   overhang a four-string staff?
%%   PREDICTION: no shrink — clefs.tab is one glyph and nothing in TabStaff scales it, so
%%   the clef still spans 5.760000 and OVERHANGS by 0.58 above and below.
\score { \new TabStaff \with {
  stringTunings = #bass-tuning
  \override Clef.after-line-breaking = #(dump-staff "CG4")
} { \key c \major c,4 d, e, f, | g,2 e, } \lay "CG4" }

\score { <<
  \new Staff \with { \override Clef.after-line-breaking = #(dump-mindist "CGP") }
    { \clef percussion \time 4/4 c'4 d' e' f' | g'2 e' }
  \new Staff { \time 4/4 c'4 d' e' f' | g'2 e' }
>> \lay "CGP" }

%% TKA — TKC with a sharp on the notation staff's first note. The ONLY difference is
%%   an Accidental, which reaches the column skyline through conditional-elements
%%   (separation-item.cc:128-146), not through elements. Prediction: min_dist grows by
%%   1.45 + 0.1 = 1.55 to 9.270000 — and, because the Scheme dump above cannot merge
%%   the conditional skyline, MINDIST here should stay at TKC's value while the real
%%   min_dist moves. That gap is the measurement of conditional_skyline itself.
\score { <<
  \new Staff { \key c \major \time 4/4 cis'4 d' e' f' | g'2 e' }
  \new TabStaff { \key c \major c4 d e f | g2 e }
>> \lay "TKA" }

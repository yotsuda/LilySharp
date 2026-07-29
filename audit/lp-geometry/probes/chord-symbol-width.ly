\version "2.26.0"
%% LP FIDELITY PROBE — the WIDTH of a chord symbol, read where it becomes geometry.
%%
%% Produces the "chord.symbol-width.*" entries in ../lp-geometry.json. Run with
%%   pwsh audit/lp-geometry/Measure-LilyPondProbe.ps1 -Probe chord-symbol-width.ly
%% (dedicated probe, NOT page-vertical.ly / staffless-system.ly — HANDOFF 5.0, "a new
%% pair belongs in a probe of its own").
%%
%% ⚠️ THE SANS FONT IS PINNED BELOW, and finding that it had to be is this probe's first
%% result. ly/paper-defaults-init.ly:174-177 sets `fonts.sans` to "LilyPond Sans Serif"
%% (the bundled Nimbus Sans) for every backend EXCEPT svg, where it falls back to the
%% bare generic "sans" — i.e. to whatever fontconfig resolves on this machine. The probe
%% runner passes -dbackend=svg, and the first run of this file measured ext("Am") =
%% 4.336200 — fontconfig had resolved generic sans through 60-latin.conf's preference
%% list (Noto Sans, DejaVu Sans, Verdana, ...) to VERDANA metrics, the first installed
%% candidate on a stock Windows box. The default (ps) backend measures 3.926480 for the
%% same grob. page-vertical.ly hit the same trap for serif and pins it (its header,
%% "THE SERIF FONT IS PINNED"); sans had simply never been measured before. With the pin
%% the svg run reproduces the ps run's numbers, so the ledger values are the CANONICAL
%% bundled-font ones, not this machine's.
%%
%% WHY THIS EXISTS. Every chord point in the ledger is an ANCHOR (or a difference of two
%% anchors), in which the symbol's own width cancels — deliberately, because the two
%% engravers' text faces differ. That means NOTHING in the corpus measures the symbol's
%% width, and the width is where Lily#'s remaining chord-text inventions live:
%%
%%   * WEIGHT.  LilyPond's ChordName declares font-family sans and font-size 1.5 and NO
%%     font-series at all (scm/define-grobs.scm:837-855), i.e. the REGULAR series. Lily#
%%     reserves, prices and draws chord symbols in SansBold — its own choice, flagged
%%     LILYSHARP-OWN in EngravingDefaults.ChordNameFontSize's remarks.
%%   * A STALE EM.  The chord em moved to LilyPond's own 2.2 * 2^(1.5/6) = 2.616256
%%     (commit 526b5e69), but six call sites still price the symbol at the old literal
%%     2.6: SpacingRules.ApplyChordRowSpacing / ChordInkRightReachPerColumn (the two that
%%     make geometry), LayoutEngine's inter-system mark box, and MusicMarkEngraver x3.
%%
%% HOW THE WIDTH IS READ WITHOUT A CONVENTION PROBLEM. ChordName's extent is (0 . w) and
%% its reference point IS its ink left (no X-offset, no self-alignment —
%% define-grobs.scm:837-855), so between two ADJACENT symbols of the SAME text the
%% column-to-column gap is convention-free on both sides. When the symbols stand close
%% enough that the spacing rod binds, that gap is
%%     w + 0.5 + 0.5 + 0.1
%% — extra-spacing-width (-0.5 . 0.5) grows the ink 0.5 to each side
%% (define-grobs.scm:840), and the rod between two musical columns carries the spacing
%% spanner's padding 0.1 on top (lily/spacing-spanner.cc:315-316, the same padding the
%% note-to-note rods carry — HANDOFF 2H). Lily#'s ApplyChordRowSpacing prices
%% w + 0.5 + 0.5 and NO 0.1 (SpacingRules.cs Widen), which the binding book below reads
%% directly — the pair's second defect, found by the pair disagreeing with the first
%% arithmetic tried (gap == w + 1.0 was off by exactly 0.100000 in every binding score).
%%
%% Whether the rod binds is SELF-CHECKING from this dump alone: the same record carries
%% the symbol's ext, so "gap == ext width + 1.1" is decidable without a second run.
%%
%% ============ PREDICTIONS, written BEFORE running (HANDOFF 5.0-2) ============
%%
%% From the bundled TeX Gyre Heros faces (the metric twin of LilyPond's Nimbus Sans),
%% advance widths via fontTools, in ss:
%%
%%                       regular @ 2.616256      bold @ 2.6 (= Lily# today)
%%   "C"                 1.888937                1.877200
%%   "Am"                3.924383                4.188600
%%
%% * PREDICTION: ext("Am") lands a little under the bare advance sum, 3.87..3.93.
%%   OUTCOME: 3.926480 with the canonical font — inside the window (Pango's slightly
%%   quantized advances, +0.002097 over the Heros sum, the size the lyric face difference
%%   already sits at). The FIRST run measured 4.336200 and the miss was the probe's
%%   biggest find: that number is Verdana, see the pin note above.
%% * DIRECTION (the falsifiable part): Lily#'s gap on the "Am" pair is WIDER than
%%   LilyPond's by ≈ +0.26 (the bold m; Heros bold "m" advance is 7.4% wider than
%%   regular). OUTCOME: +0.162120 once the missing rod padding 0.1 is netted out
%%   (+0.262120 of width, −0.100000 of rod) — the width half is the predicted +0.26.
%% * The em alone CANNOT explain the measurements: per-glyph, LP's widths are Heros
%%   REGULAR advances (A 1.741309 ≈ 0.6656 em, m 2.185172 ≈ 0.8352 em vs regular's
%%   0.667/0.833, bold's 0.722/0.889) — the bold hypothesis dies per glyph, not by a
%%   scale factor (HANDOFF 5.0: "a face difference varies per glyph; a scalar is a
%%   size").
%%
%% FORK (what each outcome selects, decided before measuring):
%%   * CWA gap != ext + 1.1        -> the rod is not binding; shorten the durations and
%%                                    re-run. No conclusion about the width yet.
%%   * CWC / CWH (slack controls) not exact on the Lily# side
%%                                 -> the chord-row DURATION spring path is wrong; that
%%                                    defect is upstream of any width claim. Fix/point it
%%                                    first, do NOT attribute its size to the weight.
%%   * CWA residual == bold-vs-regular arithmetic (per-glyph, non-scalar)
%%                                 -> port = regular series + the one ChordNameFontSize
%%                                    em everywhere the symbol is measured, and the rod's
%%                                    0.1 into ApplyChordRowSpacing.
%%
%% ============ MEASURED 2026-07-29 on LilyPond 2.26.0 (fonts.sans pinned) ============
%%
%%   score  chord   first gap           ext (= (0 . w))       gap - w - 1.1
%%   CWA    "Am"    5.026480            (0 . 3.926480)        0.000000   <- rod binds
%%   CWC    "C"     3.398045            (0 . 1.877882)        +0.420163  <- slack, spring
%%   CWH    "C"     4.598045            (0 . 1.877882)        +1.620163  <- slack, spring
%%   CWM    "A"/"F" 3.398045            (0 . 1.741309/1.604735)          <- slack, spring
%%
%% * CWA sits ON the rod to six digits (5.026480 = 3.926480 + 1.1) — the regime is the
%%   intended one and the gap IS w + 1.1. The quarter-note spring under it is CWC's
%%   3.398045, well clear.
%% * CWC and CWM read the SAME 3.398045 for quarters under different symbol widths —
%%   the spring carries no text metric, which is what makes them the fork's controls.
%% * PLAIN calibration (score CAL): interpreting bare strings with the grob's own props
%%   reproduces every ChordName ext EXACTLY — "Am" 3.926480 = "A" 1.741309 + "m"
%%   2.185172 (no kerning), and the Ignatzek markup structure (empty sub-markups,
%%   \hspace 0, empty super) contributes ZERO width to a plain major/minor name. So a
%%   plain-text width model is the correct Lily# twin for these names; no markup
%%   assembly is being measured here.
%% * Per glyph at em 2.616256 the canonical numbers are Nimbus Sans REGULAR advances
%%   within Pango's quantization (~±0.005 ss): A 1.741309 (Heros adv 1.745043),
%%   C 1.877882 (1.888937), F 1.604735 (1.598532), m 2.185172 (2.179341),
%%   7 1.468162 (C7 3.346044 - C).
%%
%% ragged-right, indent 0: every spring at force 0, so a gap reads ideal-vs-rod directly.

\header { tagline = ##f }

%% See the header: without this pin the svg backend measures this machine's fontconfig
%% pick for generic "sans" (Verdana here), not LilyPond's bundled chord font.
%% LILYPOND-REF: ly/paper-defaults-init.ly:174-177 property-defaults.fonts.sans.
\paper { property-defaults.fonts.sans = "LilyPond Sans Serif" }

%% One record per ChordName: its anchor (= ink left = its column), its ink extent, and
%% its markup — the ext alone cannot say WHAT text produced it, and the first run's
%% widths matched no advance arithmetic tried beforehand (see the pin note).
#(define ((gd tag) g)
   (let ((sys (ly:grob-system g)))
     (format #t "\nPROBE ~a CHORD x=~a ext=~a text=~s\n" tag
             (ly:grob-relative-coordinate g sys X)
             (ly:grob-extent g g X)
             (ly:grob-property g 'text))))

%% Calibration: PLAIN strings interpreted with the SAME font props the ChordName grob
%% carries (its alist chain), so the raw glyph runs can be told apart from what the
%% Ignatzek markup structure (empty sub-markups, \hspace, super) adds around them.
%% (Measured: it adds nothing for plain major/minor names.)
#(define ((gcal tag) g)
   (for-each
    (lambda (s)
      (format #t "\nPROBE ~a PLAIN ~s ext=~a\n" tag s
              (ly:stencil-extent
               (ly:text-interface::interpret-markup
                (ly:grob-layout g) (ly:grob-alist-chain g) (make-simple-markup s))
               X)))
    '("A" "C" "F" "m" "Am" "C7")))

lay =
#(define-scheme-function (tag) (string?)
   #{
     \layout {
       ragged-right = ##t
       line-width = 500\mm
       indent = 0
       \context {
         \Score
         \override ChordName.after-line-breaking = #(gd tag)
       }
     }
   #})

%% CWA — four adjacent "Am" quarters. The wide text: the bold-vs-regular difference
%% lives almost entirely in the lowercase m. Gap #0 -> #1 is the ledger quantity.
\score { \new ChordNames { \time 4/4 \chordmode { a4:m a:m a:m a:m | a1:m } } \lay "CWA" }

%% CWC — the same shape on "C": w + 1.1 = 2.977882 < the quarter spring, so the rod is
%% SLACK and the gap reads the duration SPRING, no text metric in it. Control #1.
\score { \new ChordNames { \time 4/4 \chordmode { c4 c c c | c1 } } \lay "CWC" }

%% CWH — the same "C" as halves: the half-note spring, still slack. Control #2.
\score { \new ChordNames { \time 4/4 \chordmode { c2 c | c1 } } \lay "CWH" }

%% CWM — single letters "A" and "F", solving the face per glyph: ext("A"), ext("C"),
%% ext("F") pin the advance-vs-ink question, and ext("Am") - ext("A") is the m alone.
\score { \new ChordNames { \time 4/4 \chordmode { a4 f a f | a1 } } \lay "CWM" }

%% CAL — one chord, plus the plain-string calibration rows (see gcal above).
\score {
  \new ChordNames { \time 4/4 \chordmode { a1:m } }
  \layout {
    ragged-right = ##t
    line-width = 500\mm
    indent = 0
    \context {
      \Score
      \override ChordName.after-line-breaking =
        #(lambda (g) ((gd "CAL") g) ((gcal "CAL") g))
    }
  }
}

%% ============ THE SPRING ITSELF (2026-07-29, second visit) ============
%%
%% The controls came out on numbers no first-principles arithmetic hit: quarters 3.398045,
%% halves 4.598045. With the SpacingSpanner defaults (define-grobs.scm:3242-3246:
%% base-shortest-duration 3/16, shortest-duration-space 2.0, spacing-increment 1.2) the
%% no-wishes musical spring (spacing-basic.cc:109-183 note_spacing over
%% spacing-options.cc get_duration_space) works out to 2.898045 and 4.098045 — BOTH
%% measurements sit EXACTLY 0.5 above the formula. The books below decide the formula's
%% remaining degrees of freedom empirically, and the property dump makes LilyPond say
%% which numbers its own spring actually read.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), under the hypothesis
%%   gap = fraction * (2.0 + log2(shortest_playing / min(3/16, most-common-shortest)))
%%             * 1.2  + 0.5:
%%   CW8 ("A" eighths):  gs = min(3/16, 1/8) = 1/8, eighth gap = 2.4 + 0.5 = 2.900000.
%%       The rod under it is 1.741309 + 1.1 = 2.841309 — 0.059 below, so the spring is
%%       still what binds. If the gap reads 2.841309 instead, the rod won and the book
%%       says nothing about the spring (re-run with a narrower symbol… there is none, so
%%       that outcome forces a rest-based redesign).
%%   CWX ("A" quarter + two eighths, twice): gs = 1/8. Eighth gaps 2.900000. QUARTER gap
%%       = fraction 2 of the eighth's len = 2 * 2.4 + 0.5 = 5.300000 if the +0.5 is
%%       per-PAIR, or 2 * (2.4 + 0.5) = 5.800000 if it scales with fraction — the book
%%       exists to tell those apart. (shortest_playing at the quarter's own column is
%%       1/4, giving 3.6 * 1 + 0.5 = 4.100000 — a THIRD hypothesis the same number
%%       decides: 5.3 says the fraction path with a flat 0.5, 4.1 says shortest_playing
%%       is per-column, 5.8 says the 0.5 rides the fraction.)
%%   CAL2 property dump: common-shortest-duration = 3/16 on the quarters book; each
%%       chord column is MUSICAL (shortest-starter-duration 1/4) with
%%       shortest-playing-duration 1/4.
%%
%% ============ MEASURED 2026-07-29 (see the run log / ledger whys) ============
%%
%%   CW8   gaps #0->#1..: all 2.900000            <- prediction HELD (spring, not rod)
%%   CWX   eighth gaps 2.900000, quarter gap 4.100000
%%         <- the THIRD hypothesis: shortest_playing is PER-COLUMN (the quarter column
%%            prices its own 1/4 at fraction 1), and the +0.5 is a FLAT per-pair term.
%%   CAL2  common-shortest-duration = 3/16, shortest-starter/playing = 1/4, musical,
%%         and spacing-wishes = () on every chord column (WISHES rows) — the spring is
%%         musical_column_spacing's WISHLESS branch, bare note_spacing.
%%
%% ============ AND THE +0.5 IS A COLUMN, NOT A TERM (same day, ALLCOL dump) ============
%%
%% The flat 0.5 resisted every per-spring reading because it is not in the musical
%% spring at all: the ALLCOL dump shows a STARTER-LESS (non-musical) column exactly
%% 0.500000 left of every musical column —
%%
%%   x=0.0 (break=1) | mus 0.5 | cmd 3.398045 | mus 3.898045 | cmd 6.796090 | ...
%%   ... | mus 14.092180 (the whole) | cmd 19.390225 (break=-1, = the bar line)
%%
%% LilyPond makes a command + musical column pair at every timestep; on a STAFF the
%% empty command columns are pruned as loose, but is_loose_column
%% (spacing-determine-loose-columns.cc:82-90) wants left-/right-neighbor objects that
%% only note columns provide, and a ChordNames/Lyrics-only score has none — so they
%% SURVIVE, and each beat costs musical->command = note_spacing's duration space plus
%% command->musical = standard_breakable_column_spacing's dt==0 `min_dist + 0.5`
%% (spacing-basic.cc:71-77, min_dist 0 for an empty column). The closing gap has no
%% extra 0.5 because the bar line's own command column IS the right column: the
%% whole-note gap 19.390225 - 14.092180 = 5.298045 = (2 + log2(1 / (3/16))) * 1.2, the
%% bare duration space, to six digits.
%%
%% Lily#'s ports: ApplyLeftHeadWidth no longer prices a SPACER rest's glyph (the row is
%% built of invisible spacer rests; LilyPond's left head is a real grob or nothing),
%% and ApplyRowCommandColumnSprings composes the 0.5 into each inter-column spring.
%% Both spring controls closed to exact on landing.

%% CW8 — "A" eighths: the smallest duration whose spring still clears the "A" rod.
\score { \new ChordNames { \time 4/4 \chordmode { a8 a a a a a a a | a1 } } \lay "CW8" }

%% CWX — mixed quarter + eighths: separates fraction-scaling from per-column pricing.
\score { \new ChordNames { \time 4/4 \chordmode { a4 a8 a a4 a8 a | a1 } } \lay "CWX" }

%% CAL2 — the quarters book again, dumping what the spring actually read: the paper
%% column's musicality and durations, and the SpacingSpanner's common shortest.
#(define ((gcol tag) g)
   (let* ((col (ly:grob-parent g X))
          (spanner (ly:grob-object col 'spacing)))
     (format #t "\nPROBE ~a COL rank=~a when=~a starter=~a playing=~a common=~a\n" tag
             (ly:grob-property col 'rank)
             (ly:grob-property col 'when)
             (ly:grob-property col 'shortest-starter-duration)
             (ly:grob-property col 'shortest-playing-duration)
             (if (ly:grob? spanner)
                 (ly:grob-property spanner 'common-shortest-duration)
                 'no-spanner))
     ;; Whether this column carries Note_spacing WISHES at all — the fork between
     ;; musical_column_spacing's wishless branch (note_spacing + min 0) and
     ;; Note_spacing::get_spacing. Read on both the broken piece and the original.
     (let ((orig (ly:grob-original col)))
       (format #t "\nPROBE ~a WISHES broken=~a original=~a\n" tag
               (ly:grob-object col 'spacing-wishes)
               (ly:grob-object orig 'spacing-wishes)))
     ;; EVERY column of the system, once (from the moment-0 chord): the +0.5-per-pair
     ;; measurement is explained if empty NON-musical command columns survive between
     ;; the chord columns (musical->command = duration space, command->musical = the
     ;; dt==0 breakable `min_dist + 0.5`, spacing-basic.cc:71-83).
     (if (equal? (ly:grob-property col 'when) (ly:make-moment 0))
         (let* ((sys (ly:grob-system g))
                (cols (ly:grob-object sys 'columns)))
           (for-each
            (lambda (c)
              (format #t "\nPROBE ~a ALLCOL x=~a when=~a starter=~a break=~a\n" tag
                      (ly:grob-relative-coordinate c sys X)
                      (ly:grob-property c 'when)
                      (ly:grob-property c 'shortest-starter-duration)
                      (ly:item-break-dir c)))
            (ly:grob-array->list cols))))))
\score {
  \new ChordNames { \time 4/4 \chordmode { c4 c c c | c1 } }
  \layout {
    ragged-right = ##t
    line-width = 500\mm
    indent = 0
    \context {
      \Score
      \override ChordName.after-line-breaking =
        #(lambda (g) ((gd "CAL2") g) ((gcol "CAL2") g))
    }
  }
}

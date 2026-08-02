\version "2.26.0"
%% LP FIDELITY PROBE — HOW WIDE IS A STRING, per string, in the text face the engine draws
%% text scripts with. The book the ottava island asked for: its last open entry
%% (ottava.x.line-start-to-notehead) decomposed into a SPELLING term and a +0.064862992
%% term that "ACCUMULATES ACROSS GLYPHS", about 0.032 per inter-glyph step, and there is no
%% other point in the ledger that reads a text width at all. A width term reached through
%% the ottava reads the ottava; these books read the width.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe text-advance.ly (eight tiny books, seconds).
%%
%% WHAT IS BEING MEASURED — AND WHY IT IS THE LOGICAL BOX, NOT THE INK
%%
%% LILYPOND-REF: lily/pango-font.cc:351-362 Pango_font::pango_item_string_stencil —
%%   pango_glyph_string_extents fills BOTH rectangles, and the stencil's box takes
%%   X from the LOGICAL rect and Y from the INK rect:
%%     Box (Interval (PANGO_LBEARING (logical_rect), PANGO_RBEARING (logical_rect)),
%%          Interval (-PANGO_DESCENT (ink_rect), PANGO_ASCENT (ink_rect)))
%%   So a text grob's X-extent is the SHAPED ADVANCE — left edge exactly the pen origin,
%%   right edge the sum of the glyph advances as Pango (HarfBuzz) shapes them, kerning
%%   included. Its Y-extent, by the same two lines, is the ink; that asymmetry is why
%%   textscript-ink.ly beside this file measures a descender and this one cannot.
%% CONFIRMED IN THE DUMP, not only in the source (textscript-ink.ly, 2026-07-29 and re-run
%%   2026-08-02): book TXP's italic "poco" hangs its p 0.26 ss LEFT of the pen, and its
%%   TextScript x-left is still 21.650925710824165 — the anchor notehead's x-left, to
%%   fifteen digits. An ink box could not do that.
%%
%% ⚠️ THIS IS THE ONE PLACE THE CORPUS MEASURES A METRIC RATHER THAN A DISTANCE BETWEEN
%% ANCHORS (README: "測る量はすべて anchor 間距離にしてある"). Deliberate, and the reason is
%% the rule's own reason: a width term reached through a placement is measured with the
%% table under audit still in the loop, so the reading carries the placement's arithmetic
%% too — which is exactly what happened to the ottava entry, where a 2.63 spelling term and
%% a 0.065 width term arrive added together. Lily# reserves text width with
%% TextFontMetrics.Advance in a dozen places (marks, chord names, lyrics, metronome marks,
%% the ottava's line gap); this book asks LilyPond for the same quantity, per string, so the
%% NEXT port has a number per string instead of one number per placement.
%%
%% THE MUSIC AND THE FACE are textscript-ink.ly's, unchanged and for its reasons: c''4 takes
%% a DOWN stem so the support under the script is flat, and the markup is \italic because
%% Lily#'s `_"text"` draws serif ITALIC (SharedRenderer DrawCustomTexts, FontStyle.Italic).
%% Only the STRING differs between books, so every difference between two readings here is
%% the difference between two strings.
%%
%% THE LADDER, and what each rung can say (the strings are chosen, not collected):
%%
%%   TA1 "n" / TA2 "nn" / TA4 "nnnn"  — the same glyph repeated. If the box is logical,
%%       width(k) = k × advance(n) EXACTLY, a straight line THROUGH THE ORIGIN: the single
%%       glyph is one whole advance, not an ink width. n·n does not kern in a Latin serif.
%%   TB1 "o" / TB4 "oooo"             — a SECOND glyph's ladder. This is what tells a
%%       SCALAR apart from a FACE: if Lily# and LilyPond disagree by one ratio on both
%%       ladders it is a size/scale term (one number to find), and if the two ratios differ
%%       it is the face's own advances (a table, not a constant).
%%       ⚠️ HANDOFF §5: "「フォント量」札は弱い — 全点同値ならスカラー".
%%   TAA "AA" / TAV "AV"              — same glyph count, same first glyph; A·V is the
%%       strongest kern pair in a Latin face and A·A is not one at all. Pango shapes with
%%       kerning on; Lily# sums per-character advances (TextFontMetrics.AdvancePerEm, a
%%       MeasureText per code point) and CANNOT kern. If kerning is the mechanism, TAV
%%       carries it alone and TAA is clean.
%%   T8V "8va"                        — the motivating string itself, in the face this book
%%       can pair. ⚠️ NOT the ottava's face: OttavaBracket declares font-series bold and
%%       font-shape italic (scm/define-grobs.scm), so its label is BOLD italic and the only
%%       Lily# grob drawn in that face is the ottava label, whose string is fixed. This rung
%%       says whether the same three glyphs accumulate the same way one weight over.
%%
%% PREDICTIONS, written before the run (HANDOFF §5.0-2, signs included):
%%
%%   * EVERY reading's x-left equals the anchor notehead's x-left, all eight books. That is
%%     the logical box restated per string; a single book that misses it means the X extent
%%     is NOT what pango-font.cc:359 says and every prediction below is void.
%%   * TA2 - TA1 = TA1 and TA4 = 4 × TA1, to LilyPond's own printed digits. FALSIFIER: TA1
%%     SHORTER than the step by a few hundredths (an n's right side bearing) means the box
%%     is the ink after all and the source reading is wrong.
%%   * Lily# is WIDER than LilyPond on every rung, and by a per-step amount: the ottava
%%     entry measured +0.0649 over two steps in bold italic, and the "dolce"/"poco" dumps
%%     already on file are 0.84% and 0.79% narrower than TeX Gyre Schola's summed advances
%%     at em 2.2. Sign certain, size predicted near +0.008..+0.010 ss per step for "n".
%%   * THE TWO CANDIDATES ARE SEPARABLE, and that separation is this book's whole point:
%%       - a SCALE term  ⇒ TA4/TB4 disagree with Skia by the SAME ratio, TAA and TAV
%%         disagree by that ratio too, and the fix is one number;
%%       - KERNING       ⇒ the n and o ladders are CLEAN (ratio 1.000000) and TAV alone
%%         blows out, by roughly the pair's kern (order −0.02..−0.08 em);
%%       - a FACE difference ⇒ the two ladders disagree by DIFFERENT ratios.
%%     They are not exclusive; the arithmetic says how much of each. ⚠️ What must not
%%     happen is fitting one constant to the total: the ottava entry already showed that a
%%     total is two terms when nobody asks for both (HANDOFF §1, session 73).
%%
%% MEASURED 2026-08-02 (this file, three runs — the ladder, then the single glyphs, then the
%% pair books each run added because the previous one could not be read without them):
%%
%%   EVERY reading is an exact multiple of q = 0.034143307086614 ss, and q is ONE DEVICE
%%   PIXEL at LilyPond's own text resolution: PANGO_RESOLUTION 1200 (lily/pango-font.hh:75,
%%   set on the FT2 font map at lily/all-font-metrics.cc:92-99), scaled by
%%   lily/pango-font.cc:109-111 scale_ = INCH_TO_BP / (PANGO_SCALE * PANGO_RESOLUTION *
%%   output_scale) — one pixel is 1024 pango units, i.e. 72/1200 mm / output_scale, and with
%%   output_scale 1.757299018 (a 20pt staff's space in mm: 20/72.27 in / 4, LilyPond's pt
%%   being the TeX point) that is 0.034143307086614 ss — the measured grid to FIFTEEN
%%   digits, PREDICTED from the two constants rather than fitted to the readings.
%%
%%     tag  string  ss                 px    Lily# (TextFontMetrics.Advance, em 2.2)
%%     TA1  "n"     1.331588976378      39    1.344200000
%%     TA2  "nn"    2.663177952756      78    2.688400000
%%     TA4  "nnnn"  5.326355905512     156    5.376800000
%%     TB1  "o"     1.092585826772      32    1.100000000
%%     TB4  "oooo"  4.370343307087     128    4.400000000
%%     TAA  "AA"    3.209470866142      94    3.097600000
%%     TAV  "AV"    2.902181102362      85    3.097600000
%%     T8V  "8va"   3.585047244094     105    3.627800000
%%     TG1  "A"     1.536448818898      45    1.548800000
%%     TG2  "V"     1.536448818898      45    1.548800000
%%     TG3  "8"     1.229159055118      36    1.223200000
%%     TG4  "v"     1.126729133858      33    1.141800000
%%     TG5  "a"     1.263302362205      37    1.262800000
%%     TK1  "AAA"   4.882492913386     143
%%     TK2  "VV"    3.072897637795      90
%%     TK3  "VA"    2.902181102362      85
%%
%%   ⑴ THE BOX IS LOGICAL, as pango-font.cc:359 says: TA2 = 2 × TA1 and TA4 = 4 × TA1 to
%%     the last printed digit, TB4 = 4 × TB1, and every x-left equals the anchor notehead's
%%     x-left. A single glyph is a whole ADVANCE; the ink falsifier did not fire.
%%   ⑵ THE EM IS 2.2 ss, NOT A SCALE ERROR — the candidate this book was built to kill.
%%     With ppem = 2.2 ss × 1.757299018 mm/ss × 1200/72 = 64.434297 px, EVERY single glyph
%%     is round(advance_em × ppem) on the nose: n .611→39.369→39, o .500→32.217→32,
%%     A .704→45.362→45, 8 .556→35.825→36, v .519→33.441→33, a .574→36.985→37.
%%     ⇒ LilyPond ROUNDS EACH GLYPH'S ADVANCE TO A WHOLE PIXEL. That is the whole of the
%%     ladder's disagreement: Lily# sums the UNROUNDED advances, so it is up to half a
%%     pixel (0.017 ss) out PER GLYPH, in whichever direction that glyph's remainder falls
%%     — Lily# runs 0.369 px WIDE on n and 0.217 wide on o, but 0.175 px NARROW on 8 and
%%     0.015 narrow on a.
%%     ⚠️ SO IT IS NOT "about 0.032 per inter-glyph step" (the ottava entry's reading, and
%%     the reason this book exists): it is per GLYPH, it is signed, and it is bounded.
%%   ⑶ THE FACE IS NOT THE PROBLEM either — the second candidate, also killed. C059-Italic
%%     (what "LilyPond Serif" prefers) and the bundled texgyreschola-italic.otf return the
%%     same advance for every glyph here (n 611, o 500, A 704, V 704, 8 556, v 519, a 574
%%     per 1000 em, measured 2026-08-02), and those are the numbers ⑵ rounds.
%%   ⑷ WHAT IS LEFT IS PAIR POSITIONING, which Lily# does not have at all: TAA is 94 where
%%     "A" twice is 90 (+4 px), TK1 "AAA" is 143 = 3×45 + 2×4 — so the term is PER PAIR,
%%     not per string — TAV and TK3 are both 85 = 90 − 5, and TK2 "VV" is exactly 90.
%%     "8va" is 105 where its three rounded advances sum to 106, i.e. one pixel of kern.
%%     Pango shapes through HarfBuzz with the `kern` feature on; TextFontMetrics.AdvancePerEm
%%     asks SKPaint.MeasureText once per CODE POINT, which cannot see a pair — MEASURED,
%%     not assumed: MeasureText("8va") equals the sum of its three characters to 1e-4 units.
%%   ⇒ TWO mechanisms, both measured, neither a constant: per-glyph pixel rounding and pair
%%     kerning. A single fitted factor would have to be wrong on one of them, and the signs
%%     say so out loud — Lily# is WIDER than LilyPond on "AV" (+0.195) and NARROWER on "AA"
%%     (−0.112), the same two glyphs both times.
%%   ⚠️ THE PREDICTION ABOVE THAT "Lily# IS WIDER ON EVERY RUNG" IS WRONG, and it is left
%%     standing because being wrong is what it bought: the ladder rungs went the predicted
%%     way and TAA went the other, and a term that can change SIGN between two strings made
%%     of the SAME TWO GLYPHS cannot be a size, a face or a scale. The wrong half of the
%%     prediction is what killed those three candidates; the right half only confirmed
%%     arithmetic (HANDOFF §5.0: 予測が外れたときこそ収穫).
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls)))
                   ;; Every TextScript's X extent about the system, and the note heads', so
                   ;; the x-left prediction (extent left == pen origin == the anchor head's
                   ;; left) is checkable in the same rows the width is read from.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(TextScript NoteHead))
                                        (let ((l (+ (ly:grob-relative-coordinate g sg X)
                                                    (car (ly:grob-extent g g X))))
                                              (r (+ (ly:grob-relative-coordinate g sg X)
                                                    (cdr (ly:grob-extent g g X)))))
                                          (format #t "PROBEV GROB ~a ~a name=~a x=(~a . ~a) w=~a\n"
                                                  n i nm l r (- r l))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

%% The serif font is pinned for the reason page-vertical.ly and textscript-ink.ly pin it: on
%% the svg backend LilyPond's fonts.serif falls back to whatever fontconfig resolves on this
%% machine (ly/paper-defaults-init.ly:174-177), and a probe about ADVANCES would then be
%% measuring that machine's font. Here it is not a precaution but the subject.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% The music is spelled out per book rather than built by a function: a probe is read to
%% check that the two sides engrave the same thing, and a \score written out is what a
%% reader can compare with LpGeometryProbes.TextScriptScore without running anything.

%% TA1 / TA2 / TA4 — ONE GLYPH, one / two / four times. The rungs that separate a logical
%%     box from an ink box (TA1 against the step) and read the step itself.
\book {
  \probeTag "TA1"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "n" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TA2"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "nn" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TA4"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "nnnn" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TB1 / TB4 — a SECOND glyph, so the disagreement can be checked for being ONE number.
\book {
  \probeTag "TB1"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "o" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TB4"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "oooo" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TAA / TAV — the kerning pair against its own control: same count, same first glyph.
\book {
  \probeTag "TAA"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "AA" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TAV"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "AV" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% T8V — the ottava's three glyphs, one weight lighter, where a pair can reach them.
\book {
  \probeTag "T8V"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "8va" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TG1..TG5 — SINGLE GLYPHS, added after the first run of this file (2026-08-02) because the
%%     ladder rungs above cannot be read without them. The first run showed the widths land
%%     on an exact grid — every reading is an integer multiple of 0.034143307086614 ss, TA1
%%     is 39 of them and TB1 is 32, ratio 39/32 to the last printed digit — so a single
%%     glyph's reading IS its shaped advance, and these five books turn the multi-glyph
%%     rungs into arithmetic that closes: "AA" against "A" is the A·A kern (predicted zero),
%%     "AV" against "A"+"V" is the A·V kern, and "8va" against "8"+"v"+"a" is the two kerns
%%     the ottava's string carries. Without them, "AA" is one equation in two unknowns and
%%     the same subtraction that named the ottava's residual by difference would be back
%%     (HANDOFF §1: "総計の引き算で名前を付けてはいけない").
\book {
  \probeTag "TG1"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "A" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TG2"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "V" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TG3"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "8" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TG4"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "v" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TG5"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "a" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TK1..TK3 — THE PAIR BOOKS, added after the second run, which showed the one thing the
%%     ladder could not have predicted: "AA" is 94 grid units where "A" twice is 90, i.e.
%%     the A·A pair is placed FOUR units APART, while "n" and "o" repeat with no such term
%%     at all (39/78/156 and 32/128, exactly additive). A positive pair adjustment is not
%%     what a kern normally looks like, so it gets its own three books rather than a
%%     sentence: TK1 "AAA" says whether the term is PER PAIR (two of them ⇒ 143) or once
%%     per string, TK2 "VV" says whether it belongs to the A or to the pairing, and TK3
%%     "VA" is TAV reversed — kerning is directional and a table lookup is not.
\book {
  \probeTag "TK1"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "AAA" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TK2"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "VV" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TK3"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "VA" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TM1..TM4 — THE A·A ANOMALY, added when the kerning port landed (2026-08-02). Shaping the
%%     same strings through HarfBuzz — the shaper Pango itself calls — reproduced A·V and
%%     "8va" to the last digit and gave A·A NO adjustment at all, and the face has no legacy
%%     `kern` table either (its tables are CFF GPOS GSUB OS/2 cmap head hhea hmtx maxp name
%%     post). So the +4 pixels TAA reads over two "A"s are NOT IN THE FONT, and a claim that
%%     strong gets checked before it is believed rather than after (HANDOFF §5.0: 強い言明ほど
%%     先に対を検算する). These four books ask what the term actually attaches to:
%%       TM1 "An" / TM2 "nA" — is it any pair with an A, or the A·A pair?
%%       TM3 "AAAA"          — does it stay +4 per pair at four (predicted 180 + 12 = 192
%%                             if per pair, 4 x 47 = 188 if the A is simply wider in a run)?
%%       TM4 \concat of two \italic "A" — the SAME two glyphs in TWO markup atoms. If the
%%                             term survives that it is not a shaping term at all, because
%%                             concat juxtaposes two independently shaped stencils.
\book {
  \probeTag "TM1"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "An" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TM2"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "nA" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TM3"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "AAAA" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TM4"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \concat { \italic "A" \italic "A" } c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

%% TS1 / TS2 — THE FACE CONTROL, and the book that ended the A·A hunt (2026-08-02).
%%     Every other book here pins fonts.serif to "LilyPond Serif", which is LilyPond's own
%%     alias and resolves to C059-Italic — the stencil expression says so out loud
%%     (`(glyph-string ... C059-Italic 3.865234375 ...)` with the file path in it, which is
%%     how this was finally settled: ASK THE STENCIL WHAT IT DREW). Lily# bundles TeX Gyre
%%     Schola, chosen because the two agree on every ADVANCE and C059 is AGPL. They do NOT
%%     agree on KERNS:
%%       pair   C059    TeX Gyre Schola
%%       A·A    +61      0          <- the whole of ledger text.width.aa
%%       V·A    -84    -95          <- the whole of ledger text.width.va
%%       A·V    -83    -90          <- both land on 40 px, which is why av reads EXACT
%%       v·a    -75    -75          <- identical, which is why 8va reads EXACT
%%     These two books pin the SAME LilyPond to the SAME face Lily# uses, so the comparison
%%     stops carrying a face swap. PREDICTED before the run: TS1 "AA" 90 px (2 × 45, no pair
%%     value in Schola) against "LilyPond Serif"'s 94, and TS2 "VA" 84 against 85.
%%     ⚠️ THE LEDGER ENTRIES STAY ON THE C059 BOOKS. A stock LilyPond resolves C059, so that
%%     is what fidelity means here; these two exist to say WHICH PART of a residual is the
%%     face, not to lower it.
\book {
  \probeTag "TS1"
  \paper { property-defaults.fonts.serif = "TeX Gyre Schola"
           ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "AA" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TS2"
  \paper { property-defaults.fonts.serif = "TeX Gyre Schola"
           ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "VA" c'' c'' c'' |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

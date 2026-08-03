\version "2.26.0"
%
% WHAT SHORTENS THE STEP INTO A CUE — AND IS IT THE CUE AT ALL?
%
% WHY THIS EXISTS. audit/lp-geometry cue.column.main-to-cue has stood open since 2026-08-02
% with its `why` saying, in as many words: "WHAT PRODUCES LilyPond's 0.104200 IS NOT
% IDENTIFIED... Something on the RIGHT (the cue column) shortens the spring by 0.104200 and it
% has not been named. ⚠️ Do not fit 0.104200 to anything -- name it or leave it open."
%
% THE CANDIDATE NAME, and it is not on the right at all. lily/note-spacing.cc:77 is
%
%     ideal = base.ideal_distance () - increment + left_head_end;
%
% so an ordinary step is the duration spring with `spacing-increment` (1.2) taken out and the
% LEFT column's own first-head right edge put back. For a full-size head that trade is worth
% +0.104200 exactly (1.304200 - 1.2). The step INTO the cue is short by exactly that, i.e. it
% is the RAW duration spring, i.e. that line never ran. lily/spacing-spanner.cc:340-393 says
% when it does not: a Note_spacing wish is used only if one of its `right-items` is the column
% being spaced to (:352-358), and if no wish matches, `springs.empty ()` leaves the base spring
% untouched (:380-391). The wish belongs to a VOICE, and the cue notes are in a different one.
%
% ⇒ THE CLAIM IS THEREFORE ABOUT VOICES, NOT ABOUT CUES, and that is what makes it falsifiable
% rather than a fit: an ordinary `\new Voice` with FULL-SIZE heads on both sides must lose the
% same 0.104200. If VB-VOICE reads 3.002245 like the control, the name is wrong and 0.104200
% goes back to being unnamed.
%
% Output: one line per note head.
%   PROBE <name> head x=<column X in the system> width=<head ink width> fontsize=<...>
\paper { indent = 0 ragged-right = ##t }

#(define (dumph name)
   (lambda (g)
     (format #t "PROBE ~a head x=~a width=~a fontsize=~a\n" name
             (ly:grob-relative-coordinate (ly:item-get-column g) (ly:grob-system g) X)
             (cdr (ly:grob-extent g g X))
             (ly:grob-property g 'font-size))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override NoteHead.after-line-breaking = #(dumph name)
      } { \clef treble $music } #})

% The control: one voice, four identical quarters. Every step is base - 1.2 + 1.304200.
\score { \sweep "VB-CTL"   { \time 4/4 g''4 g'' g''4 g'' } }

% THE FALSIFIER. Same four full-size quarters, but the last two are in a second Voice. No cue,
% no font-size, nothing small anywhere. If the step at the boundary is still 3.002245 the
% candidate name is dead.
\score { \sweep "VB-VOICE" { \time 4/4 g''4 g'' \new Voice { g''4 g'' } } }

% The book the ledger point comes from, repeated here so the three sit in one output.
\score { \sweep "VB-CUE"   { \time 4/4 g''4 g'' \new CueVoice { g''4 g'' } } }

% ...and the step OUT of the region, which no existing book reads. THE SHARP PREDICTION: if the
% refinement does not run at a boundary, the boundary step cannot depend on the LEFT head at
% all -- so this must read the SAME 2.898045 as VB-CUE's boundary even though the left head
% here is the small one. A theory in which the cue head merely contributes less would predict
% 2.898045 - 0.488851 instead.
%
% ⚠️ THE TRAILING VOICE IS EXPLICIT AND THAT IS NOT COSMETIC. Written as
% `\new CueVoice { g''4 g'' } g''4 g''` the last two notes are SWALLOWED by the still-live
% CueVoice -- MEASURED: all four heads come out font-size -4 and all three steps 2.513394, so
% the book measures nothing it claims to. LilyPond ends a `\new` context's music, not the
% context.
\score { \sweep "VB-OUT"   { \time 4/4 \new CueVoice { g''4 g'' } \new Voice { g''4 g'' } } }

% ---------------------------------------------------------------------------------------
% B. DOES `\new CueVoice { … }` END? -- WHERE THE FIRST VERSION OF THIS PROBE WAS WRONG.
%
% VB-OUT's first spelling was `\new CueVoice { g''4 g'' } g''4 g''` and it MEASURED four cue
% heads: the trailing notes joined the cue. The obvious generalisation -- "a `\new` context's
% music ends, the context does not, so `lysc ly`'s cue mapping is broken everywhere" -- is
% FALSE, and the twin we already ship is what falsifies it.
%
% VB-TWIN is test/cue-notes' melody staff pasted VERBATIM out of `lysc ly`. Measured: full,
% full, cue, cue, FULL, FULL, cue, cue -- the notes after the first cue block come back to
% full size on their own, and VB-TWINFIX (the same music with those notes given an explicit
% \new Voice) is identical to fifteen digits. So the mapping IS 1:1 here.
%
% ⇒ THE CONDITION IS NOT "after a cue block". It is "the cue block is the staff's FIRST
% music". `c4 d` creates a plain Voice before the cue exists, and afterwards LilyPond returns
% to it; with nothing before, the CueVoice is the only bottom context that accepts a note and
% the music has nowhere else to go. VB-OUT's first spelling hit that; VB-TWIN does not.
\score { \sweep "VB-TWIN" \relative c' { \time 4/4 \key c \major
  \relative c' { c4 d \new CueVoice { e4 f } | g4 a \new CueVoice { b4 c' } | } } }

\score { \sweep "VB-TWINFIX" \relative c' { \time 4/4 \key c \major
  \relative c' { c4 d \new CueVoice { e4 f } | \new Voice { g4 a } \new CueVoice { b4 c' } | } } }

% The condition stated as its own pair, so it is not something read off two other books:
% the SAME music twice, differing only in whether anything precedes the cue block.
\score { \sweep "VB-FIRST" { \time 4/4 \new CueVoice { g''4 g'' } g''4 g'' } }
\score { \sweep "VB-AFTER" { \time 4/4 g''4 g'' \new CueVoice { g''4 g'' } g''4 g'' } }

% ---------------------------------------------------------------------------------------
% C'. IS THE BAR LINE A BOUNDARY? -- the assumption the PORT makes, measured rather than argued.
%
% SpacingRules.CrossesVoiceBoundary treats a null right side as NOT a boundary, on the
% argument that the bar line is one column for both voices so the left voice's wish reaches
% it. That argument was written before it was tested, and it decides a gap the port actually
% moved (test/cue-accidentals lost 0.489 per measure at exactly this spring). So: does the
% last CUE note before a bar line get its spring refined by its own small head, or not?
%
%   VBB-CTL  four full quarters                  last head -> bar line
%   VBB-CUE  the last two in a CueVoice          last (small) head -> bar line
%
% Refined by the cue head, the cue book's last gap is narrower than the control's by exactly
% 1.304200 - 0.815348908 = 0.488851092. Unrefined, the two gaps are equal.
% x AND ext, because audit/lp-geometry reads this gap to the bar line's LEFT EDGE
% (RenderedGeometry.LastGlyphToBarlineLeft), which is x + car(ext) -- the same convention
% barline-spacing.ly's `gd` dump uses for barline.prev.*.
#(define (dumpbar name)
   (lambda (g)
     (format #t "PROBE ~a BAR x=~a ext=~a\n" name
             (ly:grob-relative-coordinate g (ly:grob-system g) X)
             (ly:grob-extent g g X))))
barsweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override NoteHead.after-line-breaking = #(dumph name)
        \override BarLine.after-line-breaking  = #(dumpbar name)
      } { \clef treble $music } #})

% ⚠️ A PLAIN MID-SCORE BAR LINE, not \bar "|." — barline.prev.* are all read at a plain one
% (MidLineBarline), and a final bar line is a different ink. A second measure follows so the
% first bar line is genuinely mid-score.
\score { \barsweep "VBB-CTL" { \time 4/4 g''4 g'' g''4 g'' | g''1 } }
\score { \barsweep "VBB-CUE" { \time 4/4 g''4 g'' \new CueVoice { g''4 g'' } | g''1 } }

% ---------------------------------------------------------------------------------------
% D. WHAT IS THE 1/14? -- naming the leftover the ledger point opened with.
%
% cue.barline.prev.cue-head records -0.071430911, and that is the six-digit rounding of
% design-13's head PLUS 0.071428571428572. With LilyPond's head width at FULL precision
% (1.304200 - 0.815348908003396 = 0.488851091996604) that second term is 1/14 to 6.4e-16 --
% not the "3.4e-12 resemblance" the earlier note recorded, which came from subtracting the
% nine-digit 0.488851092. An identity that exact is a term, not a coincidence.
%
% AND THE SOURCE HAS BOTH ITS FACTORS. note-spacing.cc:139-160
% different_directions_correction is min (|intersect| / 7, 1.0) * left_stem_dir *
% stem-spacing-correction -- the file itself says "Ugh. 7 is hardcoded." -- and NoteSpacing
% declares (stem-spacing-correction . 0.5) at define-grobs.scm:2656. 0.5 / 7 = 1/14.
% At a BAR LINE that correction always runs and is then halved: :281-286 synthesises the
% right-hand stem from the bar (stem_dirs[RIGHT] = -stem_dirs[LEFT], stem_posns[RIGHT] =
% bar_yextent * 2), so the directions are opposite BY CONSTRUCTION, and :299-300 multiplies
% the result by 0.5. So the term at a bar line is min (|intersect| / 7, 1) * dir * 0.25, and
% the two books differ only in |intersect| -- the CUE stem is shorter, so it overlaps the
% bar's y extent by less.
%
% THE PREDICTION, written before running this: with stem-spacing-correction set to 0 in both
% books the correction vanishes from each, so the control's gap minus the cue's gap becomes
% the head-width trade alone, 0.488851091996604 -- and the 1/14 is gone. If instead the two
% gaps still differ by 0.417422520568032, the term is NOT this correction and the name is
% wrong.
%
% ⚠️ THE POSITIVE CONTROL IS IN THE SAME RUN, because "it did not move" and "the override
% never reached the grob" are the same observation until something proves otherwise -- this
% corpus was bitten by exactly that on 2026-08-03, when a \with override of a GraceSpacing
% property never fired because Grace_spacing_engraver lives in Score. VBB-CTL-BIG is the SAME
% book with the correction at 10 instead of 0.5: if the property reaches NoteSpacing at all,
% that gap cannot sit still.
barsweepx =
#(define-music-function (name corr music) (string? number? ly:music?)
   #{ \new Staff \with {
        \override NoteHead.after-line-breaking = #(dumph name)
        \override BarLine.after-line-breaking  = #(dumpbar name)
        \override NoteSpacing.stem-spacing-correction = #corr
      } { \clef treble $music } #})

\score { \barsweepx "VBB-CTL0" #0 { \time 4/4 g''4 g'' g''4 g'' | g''1 } }
\score { \barsweepx "VBB-CUE0" #0 { \time 4/4 g''4 g'' \new CueVoice { g''4 g'' } | g''1 } }

% The instrument, proved alive on the very book the reading is taken from.
\score { \barsweepx "VBB-CTL-BIG" #10 { \time 4/4 g''4 g'' g''4 g'' | g''1 } }

% ...and the quantity the correction reads, dumped directly, so the mechanism is not merely
% consistent with the number but visible: |intersect| is the overlap of the stem's own Y
% extent (in staff POSITIONS -- LilyPond multiplies by 2/ss at :272-273) with the bar's
% +-4. A separate pair, so nothing is overridden on the books above.
#(define (dumpstem name)
   (lambda (g)
     (format #t "PROBE ~a STEM y=~a fontsize=~a\n" name
             (ly:grob-extent g g Y)
             (ly:grob-property g 'font-size))))
stemsweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override Stem.after-line-breaking = #(dumpstem name)
      } { \clef treble $music } #})

\score { \stemsweep "VBBS-CTL" { \time 4/4 g''4 g'' g''4 g'' | g''1 } }
\score { \stemsweep "VBBS-CUE" { \time 4/4 g''4 g'' \new CueVoice { g''4 g'' } | g''1 } }

% MEASURED (2026-08-04, session 84). THE NAME HELD, THREE WAYS.
%
%   gap = bar x - last head x
%   VBB-CTL      2.787959284848899     VBB-CUE      2.370536764280867
%   VBB-CTL0     3.002244999134614     VBB-CUE0     2.513393907138010
%   VBB-CTL-BIG  1.804200000000002
%
% (1) DRIVEN. With stem-spacing-correction at 0 the two gaps differ by
%     3.002244999134614 - 2.513393907138010 = 0.488851091996604, which is the head-width
%     trade 1.304200 - 0.815348908003396 to 4.4e-16. The 1/14 is GONE. It is that correction
%     and nothing else.
%     ★ AND THE TWO ZEROED GAPS ARE THE TWO IDEALS FROM SECTION A -- 3.002244999134612 and
%     2.513393907138009. So with the optical term off, the gap into a bar line IS the ordinary
%     note-spacing ideal; the whole of what a bar line adds is this correction.
%
% (2) DECOMPOSED. Subtracting the zeroed books gives each correction on its own:
%       VBB-CTL  2.787959284848899 - 3.002244999134614 = -0.214285714285715 = -3/14
%       VBB-CUE  2.370536764280867 - 2.513393907138010 = -0.142857142857142 = -2/14
%     Their difference is 1/14 exactly, which is the whole leftover.
%
% (3) FROM THE STEMS THEMSELVES, so the mechanism is seen and not merely fitted:
%       VBBS-CTL stem y=(-1.0 . 2.3138)              * 2 = (-2.0 . 4.6276)
%       VBBS-CUE stem y=(0.0 . 2.4052059400555286)   * 2 = (0.0  . 4.8104)
%     intersected with the bar's +-4 staff positions: |I| = 6.0 and 4.0, and
%       -min (6/7, 1) * 0.25 = -0.214285714285714     -min (4/7, 1) * 0.25 = -0.142857142857143
%     to fifteen digits. The 0.25 is stem-spacing-correction 0.5 halved by :299-300.
%     ⇒ THE CUE STEM IS SHORTER (2.4052 against 3.3138 total), so it overlaps the bar's band
%       by two staff positions less, and 2/7 * 0.25 = 1/14.
%
% ⚠️ THE POSITIVE CONTROL FIRED. VBB-CTL-BIG (same book, correction 10) reads 1.804200 against
% VBB-CTL's 2.787959 -- the override reaches NoteSpacing, so "it did not move" would have meant
% something. Without this the run proves nothing; a \with override that never fires is exactly
% how this corpus was misled on 2026-08-03.
%
% ⇒ LILY#'S SIDE, READ NOT MEASURED: SpacingRules.StemSpacingInfo never consults IsCue --
%   StemBeginPosition / StemEndPosition take a staff position and a note value and nothing
%   else -- so both books get |I| = 6 and the same -3/14, and the cue gap comes out 1/14 too
%   narrow. That is precisely the recorded residual. ⚠️ The DRAWING does not shorten a cue stem
%   either (SharedRenderer.Noteheads.cs calls StemCalculator.CalculateStemEndY with no cue
%   scale), so this is not one wrong reader of a right number -- both spellings are full size.

% ---------------------------------------------------------------------------------------
% E. WHAT IS A CUE STEM'S LENGTH? -- the prerequisite section D named for the port.
%
% Section D showed the cue stem overlaps the bar's band by two staff positions less, and that
% SpacingRules.StemSpacingInfo gives a cue item the full-size range. Before either the spacing
% range or the drawing moves, the law has to be known -- and the ratio in section D's dump
% (2.4052 against 3.3138) is NOT magstep (-4), so the obvious guess is already dead.
%
% LILYPOND DECLARES IT. ly/engraver-init.ly:436, in the CueVoice context definition:
%     \override Stem.length-fraction = #(magstep -4)
% and lily/stem.cc:557 applies it as `length *= length-fraction` -- AFTER the shortening at
% :540-555, not before. So the prediction is that the LENGTH scales exactly and the two ends
% do not: stem-begin-position rides on the (smaller) head's attachment, and a stem is measured
% from there, so the grob's Y extent is length + a begin offset that scales differently. That
% is why the extent ratio is not magstep and the LENGTH ratio should be.
%
% ⚠️ READING THAT OVERRIDE IS NOT MEASURING IT. This corpus exists because attributions read
% off the source survived several handoffs while being wrong. The pair below reads `length`,
% `length-fraction` and both end positions off the grobs themselves.
%
% Three registers in one book, because the shortening term depends on the head's distance from
% the middle line and the "extend to the middle line" rule bites near it -- if the scaling were
% applied BEFORE shortening rather than after, the three registers would disagree by different
% amounts and one register alone could not tell.
%   g'' stem down, well above the staff     b' on the middle line     d' stem up, below it
#(define (dumpstem2 name)
   (lambda (g)
     (format #t "PROBE ~a STEM2 begin=~a end=~a length=~a frac=~a fs=~a\n" name
             (ly:grob-property g 'stem-begin-position)
             (ly:grob-property g 'stem-end-position)
             (ly:grob-property g 'length)
             (ly:grob-property g 'length-fraction)
             (ly:grob-property g 'font-size))))
stemsweepB =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override Stem.after-line-breaking = #(dumpstem2 name)
      } { \clef treble \autoBeamOff $music } #})

% ⚠️ The leading c' is NOT decoration: a cue block that is the staff's FIRST music swallows
% everything after it (section B), so the control note both anchors the comparison and keeps
% the twin honest. Its stem is dumped first in both books and is the same in both.
\score { \stemsweepB "CSL-CTL" { \time 4/4 c'4 g''4 b'4 d'4 } }
\score { \stemsweepB "CSL-CUE" { \time 4/4 c'4 \new CueVoice { g''4 b'4 d'4 } } }

% ...and a FLAGGED pair, because a flag hangs off the stem end and an eighth's stem is where a
% length law would show up as a wrong glyph position rather than a wrong gap.
\score { \stemsweepB "CSL8-CTL" { \time 4/4 c'4 g''8 b'8 d'8 r8 r4 } }
\score { \stemsweepB "CSL8-CUE" { \time 4/4 c'4 \new CueVoice { g''8 b'8 d'8 r8 } r4 } }

% MEASURED (2026-08-04, session 84). `length` is reported in HALF-SPACES and is measured from
% stem-begin-position, not from the head's centre, so the two have to be added before anything
% is compared. The leading c' reads -5.6276 / 6.6276 in BOTH books, so the twin is honest.
%
%                       begin        length      length from the head CENTRE
%   CSL-CTL  g''4      +4.6276      6.6276       7.000000000000000   (end -2.0)
%            b'4       -0.3724      6.2942667    6.666666666666667   (end -6.6667/2; shortened)
%            d'4       -4.6276      6.6276       7.000000000000000   (end +2.0)
%   CSL-CUE  g''4      +4.8104119   4.8104119    5.000000000000000   (end  0.0)
%            b'4       -0.1895881   4.0101487    4.199736832982911   (end -4.1997)
%            d'4       -4.8104119   4.8104119    5.000000000000000   (end  0.0)
%
% (1) THE LAW IS EXACT, AND ONLY THE MIDDLE-LINE NOTE CAN SHOW IT.
%       6.666666666666667 * magstep (-4) = 4.199736832982911
%     against the measured 4.199736832982911 -- equal as doubles, difference 0.0. So
%     length-fraction multiplies the length AFTER the shortening term (b' is on the middle line
%     and pays 1/3 half-space of shortening: 7 - 20/3), exactly as stem.cc:554-557 orders it.
%
% (2) ⚠️ AND THE OTHER TWO NOTES CANNOT SHOW IT, BECAUSE A FLOOR IS BINDING THERE. A scaled
%     g'' stem would run 7 * magstep (-4) = 4.409723674632057 from the head at +5 and stop at
%     +0.590276325367943; it stops at 0.000000000000000 instead, and so does d' from -5. That
%     is the rule that a stem on a note outside the staff reaches the MIDDLE LINE -- inactive
%     at full size (7 half-spaces already carries g'' to -2) and active as soon as the length
%     is scaled. ⇒ A PORT THAT SCALES THE LENGTH AND FORGETS THIS FLOOR MAKES CUE STEMS SHORT
%     BY 0.59 HALF-SPACES on exactly the notes cues are usually written on.
%     ★ This is also why section D's extent ratio (2.4052 / 3.3138) is not magstep: that book's
%     note is g'', where the floor is what sets the length.
%
% (3) ⚠️ THE HEAD ATTACHMENT DOES NOT SCALE BY MAGSTEP. stem-begin-position moves 0.3724 ->
%     0.18958811988894286, a ratio of 0.509098, where magstep (-4) is 0.629961. It is the
%     design-13 glyph's own attachment, the same phenomenon as the head WIDTH (0.815348908 is
%     not 1.304200 * magstep either). So a port needs the cue design's attachment, not a scale.
%
% (4) ⚠️ THE FLAGGED PAIR DOES NOT FOLLOW THE QUARTER LAW AND IS LEFT OPEN. CSL8's b' reads
%     6.750000 from the centre at full size and 4.039985 as a cue, where 6.750000 * magstep is
%     4.252234. The eighth's stem is lengthened to carry its flag, and the flag scales by its
%     own font size, so the two terms are not one product. NOT FITTED, NOT NAMED: this probe
%     does not separate them, and the number 4.039985 must not be written down as if it were a
%     law. ⇒ measure the flag term on its own before porting anything about eighths.
%
% (5) ✔ PORTED (2026-08-04, session 85), and (1) and (2) are BOTH in the port -- the fraction
%     in EngravingDefaults.CueStemDetails, the middle-line rule already in
%     StemCalculator.CalculateStemEndY, and Lily#'s own 2.5 floor underneath it made to ride
%     the fraction so it does not clamp what the fraction just shortened. (3) went to
%     SpacingRules.StemBeginPosition, which now asks EngravingDefaults.CueFont. The ledger
%     point cue.barline.prev.cue-head closed from -0.071430911 to -0.000002340, the metrics
%     table's rounding alone, exactly as it predicted; no other ledger point moved.
%     ⚠️ THE EIGHTH ABOVE IS STILL OPEN -- the port gives it 4.252234 against LilyPond's
%     4.039985, which is nearer than the 6.750000 it had and is not the law.
%     ⚠️ AND ONE THING THIS SECTION GOT WRONG ABOUT ITSELF: (3) says stem-begin-position "does
%     not scale", which is true of magstep and hid a second finding. The full-size number
%     0.3724 in the table above is the FONT's own LILC attachment (0.186200 * 2), and Lily#'s
%     full-size spelling was not that -- it normalised out of a box and gave
%     0.372209268188857. Two spellings of one quantity, disagreeing by 0.000190731811143, and
%     this probe's own dump said the font one was right (section D's full-stem extent 2.3138
%     is exactly (5 - 0.3724)/2, so the two dumps agreed with each other and not with us).
%     ✔ CHASED DOWN AND CLOSED IN THE SAME SESSION, in probe notehead-stem-attachment.ly: the
%     box was never wrong (LilyPond's head extent is +-0.545, ours exactly), the two
%     NORMALISED CONSTANTS were, because they had been dumped on 2.24.4 and 2.26.0 rebuilt
%     Emmentaler. StemBeginPosition now reads the font and the constants are gone. Ledger
%     point barline.next.down-stems-after-clef -- which had recorded 5.449e-06 and written
%     "appeared with the 2.26.0 font" without being able to say which metric carried it --
%     went EXACT.

% ---------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-03, session 83)
%
% A. THE STEP INTO A CUE IS NOT A CUE QUANTITY. It is what a spring measures when
%    note-spacing.cc:77 never runs, and the run is lost at a VOICE boundary.
%
%      base ideal (the duration spring alone)                       2.898044999134612
%      VB-CTL   main -> main    base - 1.2 + 1.304200 (full head)   3.002244999134612
%      VB-VOICE main -> Voice   base                                2.898044999134612
%      VB-CUE   main -> cue     base                                2.898044999134612
%      VB-CUE   cue  -> cue     base - 1.2 + 0.815348908 (13 head)  2.513393907138009
%      VB-OUT   cue  -> Voice   base                                2.898044999134612
%
%    ⇒ VB-VOICE IS THE FALSIFIER AND IT HELD: full-size heads on both sides, no cue anywhere,
%      and the boundary still costs exactly 1.304200 - 1.2 = 0.104200. The ledger's standing
%      instruction on cue.column.main-to-cue was "name it or leave it open"; the name is the
%      voice boundary, and it is not about cues at all.
%    ⇒ VB-OUT IS THE SECOND HALF: the boundary step does NOT depend on the left head's size
%      (small head, same 2.898044999134612 to fifteen digits). A theory in which the cue head
%      merely contributes less would have said 2.409193907. It is the refinement being ABSENT,
%      not being fed a smaller number.
%
% B. `\new CueVoice { … }` DOES END -- EXCEPT WHEN IT IS THE STAFF'S FIRST MUSIC.
%    VB-TWIN (test/cue-notes' melody, verbatim from `lysc ly`) keeps its post-cue notes full
%    size and is identical to VB-TWINFIX, which spells the return explicitly. VB-FIRST /
%    VB-AFTER isolate the condition: with nothing before the block, all four heads come out
%    cue-sized; with two notes before it, only the block's own two do.
%    ⇒ Lily#'s `cue { … }` is a region with an end in every position. The exported twin agrees
%      in every position EXCEPT first-in-staff. No fixture or sample writes it there.
%
% C'. THE BAR LINE IS NOT A BOUNDARY -- BUT THE PORT NARROWS THAT GAP TOO MUCH.
%    SpacingRules.CrossesVoiceBoundary treats a null right side as no boundary, and the
%    direction is right: the cue book's last gap IS narrower.
%
%      VBB-CTL  last head 17.591734997403837  bar 20.379694282252736  gap 2.787959284848899
%      VBB-CUE  last head 16.998683905407233  bar 19.3692206696881    gap 2.370536764280867
%      (bar ext is (0.0 . 0.19) in both, so x IS the left edge -- the convention
%       RenderedGeometry.LastGlyphToBarlineLeft and barline.prev.* already read)
%
%    ⚠️ LilyPond narrows it by 0.417422520568032. Lily# narrows it by the whole head-width
%    term, 0.488851092. So the 2026-08-03 port improved this gap (it used to be wrong by the
%    full 0.417) without closing it, and 0.071428571431968 is left over.
%    ⚠️ SUPERSEDED -- SEE THE NOTE BELOW AND SECTION D. IT IS 1/14 EXACTLY AND IT IS NAMED.
%    ⚠️ THAT NUMBER IS WITHIN 3.4e-12 OF 1/14 AND IS NOT NAMED. Do not fit it. Twice today a
%    resemblance of exactly this kind turned out to be a coincidence (1.6 in cue-grace-spacing)
%    or the wrong quantity entirely.
%    ✔ THE POINT IS OPEN (2026-08-03, session 84), from these two numbers and without
%    re-measuring: cue.barline.prev.cue-head / .full-head-control. The control opened EXACT and
%    the cue at -0.071430911, which is the 0.0714 above PLUS 0.000002340 -- the six-digit
%    rounding of design-13's head in GlyphMetricsGenerated.cs that already stopped
%    cue.column.step. That term arriving here by a second, independent reading is what says
%    this gap really is spending the head-width term of note-spacing.cc:77.
%    ⚠️ THE TWO PARAGRAPHS ABOVE ARE SUPERSEDED BY SECTION D, IN THE SAME SESSION. The 0.0714
%    is not "within 3.4e-12 of 1/14, do not fit it" -- that distance was an artefact of
%    subtracting the NINE-digit 0.488851092. At full precision it IS 1/14, and 1/14 is
%    stem-spacing-correction over the hardcoded 7, halved at a bar line. LilyPond spends
%    -2/14 here and -3/14 on the control because ITS cue stem is shorter. See D.
%
% C. ⚠️ UNEXPLAINED, AND LEFT THAT WAY. VB-AFTER's return step -- last cue note to the first
%    note back in the ORIGINAL Voice -- is 3.631965335709437, which is none of the three forms
%    above (not base, not base - 1.2 + 0.815348908, not the control). That configuration has
%    two voices alive across the column and merge_springs (spacing-spanner.cc:380-393) is in
%    play. It is recorded here as a measurement and NOT fitted to anything; no point is opened
%    on it. VB-OUT, where the cue is followed by a genuinely new Voice, is the clean reading.

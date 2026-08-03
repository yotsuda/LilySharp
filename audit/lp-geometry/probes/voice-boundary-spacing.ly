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
%    ⚠️ THAT NUMBER IS WITHIN 3.4e-12 OF 1/14 AND IS NOT NAMED. Do not fit it. Twice today a
%    resemblance of exactly this kind turned out to be a coincidence (1.6 in cue-grace-spacing)
%    or the wrong quantity entirely.
%    ⚠️ NOTHING OBSERVES IT. No ledger point reads a cue-to-bar-line gap. The two LilyPond
%    numbers above are recorded here in the ledger's own convention so the point can be opened
%    without re-measuring -- that is the next step, before anyone tries to close the 0.0714.
%
% C. ⚠️ UNEXPLAINED, AND LEFT THAT WAY. VB-AFTER's return step -- last cue note to the first
%    note back in the ORIGINAL Voice -- is 3.631965335709437, which is none of the three forms
%    above (not base, not base - 1.2 + 0.815348908, not the control). That configuration has
%    two voices alive across the column and merge_springs (spacing-spanner.cc:380-393) is in
%    play. It is recorded here as a measurement and NOT fitted to anything; no point is opened
%    on it. VB-OUT, where the cue is followed by a genuinely new Voice, is the clean reading.

\version "2.26.0"

%% WHAT ELSE BUT THE NOTE HEADS IS IN A TIE'S CHORD OUTLINE.
%%
%% set_column_chord_outline (tie-formatting-problem.cc:96-287) walks EVERY grob of a bound
%% column into the skyline the tie reads its attachment off: the tied heads, the stem, and
%% then four more boxes that are NOT the tie's own heads --
%%
%%   :123-139  the augmentation DOTS        (LEFT bound only)
%%   :181-190  the FLAG                     (LEFT bound only, normal stem only)
%%   :210-224  the chord's UNTIED heads
%%   :226-236  the ACCIDENTALS              (RIGHT bound only)
%%
%% Lily# builds all four (Svg/Layout/ElementCoordinator.cs:1809-1928) and reads all four
%% (Svg/Layout/TieChordOutline.cs:188-287), and until session 452 NOTHING observed any of
%% them: poisoning each of the four to [] left all 8,774 tests green. These three books are
%% what says the boxes are there, one book per box that a tie can actually meet.
%%
%%   TVDOT  <c e g>4.~ <c e g>8 <c e g>2   the DOTS. The middle tie of a dotted triad runs
%%                                         through the dot column, so its left attachment is
%%                                         the DOT's right edge and not the head's.
%%   TVACC  <c g>2~ <c g aes>2             the ACCIDENTALS. The arriving chord carries an
%%                                         UNTIED aes whose flat stands between the upper
%%                                         tie and the g it is arriving at.
%%   TVOTH  c2~ <b, c>2                    the UNTIED HEAD. The arriving chord's b, is not
%%                                         tied, and the down tie has to clear it, which
%%                                         shows in the chosen POSITION rather than the width.
%%
%% ⚠️ THE FLAG HAS NO BOOK AND THAT IS THE FINDING, not an omission -- see the session 452
%% entry in docs/HANDOFF.md. Lily# builds the flag box for SINGLE NOTES only, and a single
%% note's tie is always on the opposite side of the head from its own flag, so the box it
%% builds can never be met. Measured: emptying it moves 0 of 41 probe books, while making it
%% enormous moves 19 -- so it is built and read, and its real geometry is simply never in
%% the way.
%%
%% ⚠️ THE MUSIC CAME OUT OF `lysc ly` (RULES 6 -- hand-written twins have produced three
%% false divergences in this repo). TWO edits were made by hand, both the ones every tie
%% probe here makes: `\bar "|."`, because LilyPond does not end a score with a final bar
%% line on its own and Lily# always draws one, and the \widths override.
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING: Lily#'s absolute `c` is LilyPond's `c'` (RULES 6).

#(define ((dump-width tag) g)
   (let ((cps (ly:grob-property g 'control-points)))
     (format #t "\nPROBE ~a WIDTH pos=~a dir=~a w=~,6f y=~,6f\n"
             tag
             (ly:grob-property (ly:spanner-bound g LEFT) 'staff-position)
             (ly:grob-property g 'direction)
             (- (car (cadddr cps)) (car (car cps)))
             (cdr (car cps))))
   '())

widths = #(define-music-function (tag) (string?)
            #{ \override Tie.after-line-breaking = #(dump-width tag) #})

\paper {
  indent = 0
  ragged-right = ##t
}

\score { \new Staff { \clef "treble" \time 4/4 \key c \major \widths "TVDOT"
  \fixed c' { <c e g>4. ~ <c e g>8 <c e g>2 \bar "|." } } }

\score { \new Staff { \clef "treble" \time 4/4 \key c \major \widths "TVACC"
  \fixed c' { <c g>2 ~ <c g aes>2 \bar "|." } } }

\score { \new Staff { \clef "treble" \time 4/4 \key c \major \widths "TVOTH"
  \fixed c' { c2 ~ <b, c>2 \bar "|." } } }

\version "2.26.0"

%% THE FLAG BOX OF A TIED CHORD (HANDOFF (x)^5, sessions 452 / 523 / 524).
%%
%% set_column_chord_outline (tie-formatting-problem.cc:181-190) puts the Stem's FLAG into the
%% tie's chord outline whenever the left bound has a normal stem -- it does not ask how many
%% heads the stem carries. Until session 524 Lily# built that box for SINGLE NOTES only
%% (ElementCoordinator.BuildTieColumn, `item is NoteItem`), and a single note's tie always
%% leaves on the side of the head AWAY from its own flag, so the box it built could never be
%% met (probe tie-outline-boxes.ly). A CHORD is where the flag speaks: the stem's tip is next
%% to the tied head at the stem end, so the tie of the head NEAREST the stem runs across the
%% flag (TCFX) or has to duck under it (TCFY).
%%
%% Both books are the owner's corpus shapes cut to one bar -- the only two books of 232 whose
%% pages move when the box is built for chords (24 pages, session 523; the twins are what
%% session 524 measured them against, Lab sessions/p524).
%%
%%   TCFX  <e gis>4. <e a>8 ~ <e a>2      bass, A major. Stem DOWN, flag below. The tie of the
%%                                        lower head (e, position 8) leaves DOWN, under the
%%                                        flag: it starts to the RIGHT of the flag box and is
%%                                        the narrower for it. Read as the WIDTH.
%%   TCFY  d,4. <d, fis>8 ~ <d, fis>4 ... bass, F major. Same shape one octave down; the
%%                                        lower head (d,, position 0) sits on the middle line
%%                                        and its tie is pushed a little further DOWN by the
%%                                        flag rather than narrowed. Read as the HEIGHT.
%%
%% x0 (the tie's left end, relative to the tied column's head left edge) is printed as well,
%% for the record: on TCFX it is where the remaining 0.04 lives (Lily# boxes the flag from
%% StemX with the glyph's bbox width; LilyPond takes the Flag grob's X extent), with the
%% opposite sign of the width's residual and the same cause.
%%
%% ⚠️ THE MUSIC CAME OUT OF `lysc ly` on the two .lys books (RULES 6). Edits by hand, the ones
%% every tie probe here makes: `\bar "|."` and the \widths override.
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING: Lily#'s absolute `c` is LilyPond's `c'` (RULES 6).

#(define ((dump-width tag) g)
   (let ((cps (ly:grob-property g 'control-points)))
     (format #t "\nPROBE ~a WIDTH pos=~a dir=~a x0=~,6f w=~,6f y=~,6f\n"
             tag
             (ly:grob-property (ly:spanner-bound g LEFT) 'staff-position)
             (ly:grob-property g 'direction)
             (car (car cps))
             (- (car (cadddr cps)) (car (car cps)))
             (cdr (car cps))))
   '())

widths = #(define-music-function (tag) (string?)
            #{ \override Tie.after-line-breaking = #(dump-width tag) #})

\paper {
  indent = 0
  ragged-right = ##t
}

\score { \new Staff { \clef "bass" \key a \major \time 4/4 \widths "TCFX"
  \fixed c' { <e gis>4. <e a>8 ~ <e a>2 \bar "|." } } }

\score { \new Staff { \clef "bass" \key f \major \time 4/4 \widths "TCFY"
  \fixed c' { d,4. <d, fis>8 ~ <d, fis>4 e,,8 f,, \bar "|." } } }

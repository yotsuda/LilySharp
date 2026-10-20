\version "2.26.0"

%% A TIE IN GRACE TIME (session 916, HANDOFF §2 U8 ⒞).
%%
%% LilyPond's grace notes are ordinary Voice events in grace time, so the ordinary
%% Tie_engraver ties them: a grace column to its main note (GTA), and one grace column to the
%% next (GTB). Until session 916 Lily# dropped the `~` in a grace body (LYS4020).
%%
%%   GTA  c'4 \grace { g'16~ } g'4 c'2       the grace's tie into its main note
%%   GTB  c'4 \grace { d''16~ d''16 } c''4 c'2  a tie between two grace columns
%%   GTC  c'4 g'16~ g'8. c'2                 the full-size control of GTA
%%
%% Read as the ledger's other ties are: the WIDTH (control point 3 x - control point 0 x)
%% and the left end's Y (control point 0 y).
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING: Lily#'s absolute `c` is LilyPond's `c'` (RULES 6).

#(define ((dump-width tag) g)
   (let ((cps (ly:grob-property g 'control-points)))
     (format #t "\nPROBE ~a WIDTH dir=~a x0=~,6f w=~,6f y=~,6f\n"
             tag
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

\score { \new Staff { \time 4/4 \widths "GTA"
  \fixed c' { c4 \grace { g16~ } g4 c2 \bar "|." } } }

\score { \new Staff { \time 4/4 \widths "GTB"
  \fixed c' { c4 \grace { d'16~ d'16 } c'4 c2 \bar "|." } } }

\score { \new Staff { \time 4/4 \widths "GTC"
  \fixed c' { c4 g16~ g8. c2 \bar "|." } } }

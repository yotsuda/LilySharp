\version "2.26.0"

%% A SCRIPT ON A GRACE NOTE (session 916, HANDOFF §2 U8 ⒟).
%%
%% LilyPond's grace notes are ordinary Voice events, so the ordinary Script_engraver puts a
%% staccato or an accent on one — at general-grace-settings' Script font-size (-3), centred on
%% the grace head, clear of it by the script's ordinary padding. Until session 916 Lily#
%% dropped every script in a grace body (LYS4020).
%%
%%   GSA  c'4 \grace { d'16-. } e'4 c'2       staccato below a grace (stem up)
%%   GSB  c'4 \grace { d'16-> } e'4 c'2       accent below a grace
%%   GSC  c'4 \grace { d'''16-. } e'4 c'2     staccato below a grace above the staff
%%   GSD  c'4 d'16-. e'8. c'2                 the full-size control of GSA
%%
%% Read as the ledger's other scripts are: the script's ORIGIN above the staff's middle line.
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING: Lily#'s absolute `c` is LilyPond's `c'` (RULES 6).

#(define ((dump-script tag) g)
   (let* ((sys (ly:grob-system g))
          (staff (ly:grob-object g 'staff-symbol))
          (y (- (ly:grob-relative-coordinate g sys Y)
                (ly:grob-relative-coordinate staff sys Y))))
     (format #t "\nPROBE ~a SCRIPT ~a y=~,6f fs=~a\n" tag
             (cdr (ly:grob-property g 'script-stencil)) y (ly:grob-property g 'font-size)))
   '())

scripts = #(define-music-function (tag) (string?)
             #{ \override Script.after-line-breaking = #(dump-script tag) #})

\paper { indent = 0 ragged-right = ##t }

\score { \new Staff { \time 4/4 \scripts "GSA" \fixed c' { c4 \grace { d16-. } e4 c2 \bar "|." } } }
\score { \new Staff { \time 4/4 \scripts "GSB" \fixed c' { c4 \grace { d16-> } e4 c2 \bar "|." } } }
\score { \new Staff { \time 4/4 \scripts "GSC" \fixed c' { c4 \grace { d''16-. } e4 c2 \bar "|." } } }
\score { \new Staff { \time 4/4 \scripts "GSD" \fixed c' { c4 d16-. e8. c2 \bar "|." } } }

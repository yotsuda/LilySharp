\version "2.26.0"

%% The LilyPond twin of LilySharp.Tests/Fixtures/test/ties-slurs.lys, asked ONE
%% question: how many systems does LilyPond take for it on the same paper?
%%
%% Lily#'s snapshot is A4 at staff-size 20 (the SVG viewBox is 119.50 staff spaces
%% wide, and 210mm / 1.75mm = 120), so the default a4 paper is the match. Wiring the
%% line-start spring into the break gate moved this fixture from one system to two;
%% this decides which of the two LilyPond agrees with.
%%
%% ⚠️ THE \bar "|." IS LOAD-BEARING, and leaving it out is what made this probe answer
%% the wrong question on 2026-07-25. LilyPond does NOT end a score with a final bar line
%% on its own -- measured: a plain \score { \new Staff { c'1 c'1 } } dumps glyph-name "|"
%% with a 0.190000 stencil, and its SVG carries two 0.19-wide rects and nothing thicker.
%% Lily# always draws one (0.19 + kern 0.3 + thick 0.6 = 1.09). Without this line the
%% twin is 0.900000 narrower than the .lys purely in ink that Lily# draws and LilyPond
%% was never asked to, and that 0.9 was misread as Lily# spacing its eight bars too wide.
%% The ragged control measures them column by column: they agree to 2e-5 everywhere.

\paper {
  indent = 0
  ragged-right = ##f
}

\header {
  title = "Ties and Slurs"
  composer = "Lily#"
}

%% One record per SYSTEM: the bar it opens on and how far it reaches. Reading the answer
%% off the PDF is how the wrong one gets read.
#(define probe-done (make-hash-table))

#(define ((dump-system tag) g)
   (let ((sys (ly:grob-system g)))
     (if (not (hash-ref probe-done (cons tag sys) #f))
         (begin
           (hash-set! probe-done (cons tag sys) #t)
           (let* ((cols (ly:grob-array->list (ly:grob-object sys 'columns)))
                  (rl (ly:grob-property (car cols) 'rhythmic-location)))
             (format #t "\nPROBE ~a SYSTEM bar=~a ncols=~a width=~,6f\n"
                     tag
                     (if (pair? rl) (car rl) "?")
                     (length cols)
                     (apply max (cons 0.0
                                      (map (lambda (c)
                                             (ly:grob-relative-coordinate c sys X))
                                           cols))))))))
   '())

\score {
  \new Staff \with {
    \override NoteHead.after-line-breaking = #(dump-system "TSJ")
  } {
    \tempo 4 = 120
    \time 4/4
    \key c \major
    c'4~ c'4 d'2 |
    d'2 e'2~ | e'4 f' g' a' |
    c'4( d' e' f') |
    g'4( f' e' d') | c'2 r2 |
    c'4~ c'4 r2 |
    b'4~ b'4 r2 \bar "|."
  }
}

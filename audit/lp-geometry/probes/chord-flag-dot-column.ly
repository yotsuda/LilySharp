\version "2.26.0"

%% A FLAG STANDS IN A CHORD'S DOT COLUMN, TOO.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe chord-flag-dot-column.ly -Prefix PROBEX
%%
%% Dot_column builds a rightward skyline over the column's supports and the FLAG is one of
%% them (lily/dot-column.cc:100-141), so the augmentation dot of an unbeamed flagged note is
%% pushed right of the flag whenever the flag's ink reaches the row the dot ended up on.
%% audit's probes and Fixtures/test/dotted-flag-dot-column.lys hold that for a SINGLE note.
%% This probe holds it for a CHORD, because Lily# assembles the supports TWICE -- once in
%% SharedRenderer.DrawNote and once in SharedRenderer.DrawChord -- and until session 452 only
%% the first of the two was observed: passing DrawChord an empty support span left all 8,774
%% tests green while the same poison on DrawNote turned five red (docs/HANDOFF.md, the (v)
%% ticket opened by session 450).
%%
%% ⚠️ A CHORD'S STEM IS NOT THE SINGLE NOTE'S. It runs from the far head, so the flag hangs
%% roughly a stem-length above the TOP head rather than above the only head, and whether it
%% reaches that head's dot row is a different question with the same shape. The four columns
%% are the two that answer it each way, twice over:
%%
%%   <g b>8.   top head ON A LINE, its dot lifted one position
%%   <f a>8.   top head IN A SPACE, dot not lifted
%%   <g b>16.  the sixteenth's flag is deeper (bottom -3.5502 against -3.0502)
%%   <f a>16.  and reaches even an unlifted dot
%%
%% ⚠️ THE RESTS ARE BEAM BREAKERS, not decoration: Lily# auto-beams `<g b>8. <f a>8.` where
%% LilyPond does not, and a beamed column has no flag at all. The single-note fixture's
%% header records the probe that measured nothing for exactly that reason.
%%
%% ⚠️ THE MUSIC CAME OUT OF `lysc ly` (RULES 6 -- hand-written twins have produced three false
%% divergences in this repo), from the .lys the Lily# side renders.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEX PAPER line-width=~a indent=~a\n"
           (ly:output-def-lookup layout 'line-width)
           (ly:output-def-lookup layout 'indent))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEX PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (sg (ly:prob-property sys 'system-grob)))
                   (if (ly:grob? sg)
                       (let ((all (ly:grob-object sg 'all-elements)))
                         (if (ly:grob-array? all)
                             (for-each
                              (lambda (g)
                                (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                  (if (memq nm '(NoteHead Dots Flag))
                                      (format #t "PROBEX GROB ~a ~a name=~a anchor=~a x=(~a . ~a) y=~a\n"
                                              n i nm
                                              (ly:grob-relative-coordinate g sg X)
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (car (ly:grob-extent g g X)))
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (cdr (ly:grob-extent g g X)))
                                              (ly:grob-relative-coordinate g sg Y)))))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

v = \fixed c' {
  \time 4/4
  <g b>8. r16 <f a>8. r16 r2 |
  <g b>16. r32 <f a>16. r32 r2 r4 |
}

\book {
  \probeTag "CFD"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \v } }
}

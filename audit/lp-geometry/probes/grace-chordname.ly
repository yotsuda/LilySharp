\version "2.26.0"

%% A CHORD SYMBOL ON A GRACE NOTE (session 964).
%%
%% LilyPond's ChordNames context makes a ChordName for a note event in grace time, on the
%% grace's paper column — at the grace head's X, full size — and the ChordName rods (name to
%% name, w + 0.5 + 0.5 + 0.1) run through that column like any other.
%%
%%   GCA  c4 \grace { d8 } e4 c2   with names C D E C          a name on the grace
%%   GCB  the same staff, names C E C                           the control: no name on the grace
%%   GCD  c4 \grace { d8 e8 } f4 c2, names C (s8 d8) E C         two graces, the second named
%%   GCG  c4 \grace { d8 } e4 c2, names C D s C                 a name on the grace, none on the main
%%
%% Read where the page put it (page-post-process): ChordName and NoteHead X extents.
%%
%% Output (LilyPond 2.26.0): see the PROBE lines; the numbers are in lp-geometry.json
%% (chordname.grace.*). Measured first in Lab sessions/p964/lp/probe.ly and probe2.ly.
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING: Lily#'s absolute `c` is LilyPond's `c'` (RULES 6).
%% ⚠️ fonts.sans IS PINNED, as chord-symbol-width.ly pins it: with -dbackend=svg LilyPond
%% resolves the sans to the machine's generic face and the symbol widths move.

\paper { property-defaults.fonts.sans = "LilyPond Sans Serif" }

#(use-modules (srfi srfi-1))
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let ((sg (ly:prob-property sys 'system-grob)))
             (if (ly:grob? sg)
                 (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
                   (for-each
                    (lambda (g)
                      (let ((n (assq-ref (ly:grob-property g 'meta) 'name)))
                        (if (memq n '(ChordName NoteHead))
                            (let ((ext (ly:grob-extent g sg X)))
                              (format #t "\nPROBE ~a ~a x=~,6f..~,6f\n" tag n (car ext) (cdr ext))))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GCA") }
  \score { << \new ChordNames \chordmode { c4 \grace { d8 } e4 c2 }
              \new Staff \fixed c' { \time 4/4 c4 \grace { d8 } e4 c2 \bar "|." } >> } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GCB") }
  \score { << \new ChordNames \chordmode { c4 e4 c2 }
              \new Staff \fixed c' { \time 4/4 c4 \grace { d8 } e4 c2 \bar "|." } >> } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GCD") }
  \score { << \new ChordNames \chordmode { c4 \grace { s8 d8 } e4 c2 }
              \new Staff \fixed c' { \time 4/4 c4 \grace { d8 e8 } f4 c2 \bar "|." } >> } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GCG") }
  \score { << \new ChordNames \chordmode { c4 \grace { d8 } s4 c2 }
              \new Staff \fixed c' { \time 4/4 c4 \grace { d8 } e4 c2 \bar "|." } >> } }

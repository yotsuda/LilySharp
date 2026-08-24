\version "2.26.0"

%% HOW MANY ACCIDENTALS DOES A KEY SIGNATURE HAVE, AND WHICH OF THEM ARE DOUBLE?
%%
%% LilyPond does not look a key up. `\key` is a music function that TRANSPOSES a
%% C-based scale (ly/music-functions-init.ly, `key =`):
%%
%%     (ly:music-transpose
%%       (make-music 'KeyChangeEvent 'tonic (ly:make-pitch 0 0 0)
%%                                  'pitch-alist pitch-alist)
%%       tonic)
%%
%% so `keyAlterations` is ALWAYS seven pairs — one per letter — and each pair's
%% alteration is whatever transposing that degree by the tonic produced. Nothing
%% caps it: an alteration of 1 is a whole tone, i.e. a DOUBLE sharp, and one of
%% -1 a double flat. The Key_engraver hands that alist to KeySignature, and
%% scm/output-lib.scm key-signature-interface::alteration-positions places each
%% NON-ZERO entry; a zero entry is simply not drawn.
%%
%% ⚠️ SO THE COUNT OF DRAWN GLYPHS IS AT MOST SEVEN, AND "EIGHT SHARPS" IS NOT
%% EIGHT GLYPHS. C-sharp lydian is eight sharps' worth and prints SEVEN symbols,
%% the first of which is a double sharp on F. A port that keeps a tonic->count
%% table and then draws `min(|count|, 7)` single accidentals gets the COUNT right
%% and the GLYPH wrong, which is why these points count the doubles separately.
%%
%% ⚠️ WHY THE PAIR IS `cis \major` AGAINST `cis \lydian`. The two books hold the
%% TONIC fixed and change one word. LilyPond answers them differently (seven
%% singles against a double plus six singles); any implementation that reads a
%% tonic->sharps table capped at seven must answer them THE SAME. Stating the
%% claim as a pair rather than as one number is HANDOFF 5.0's rule, and the
%% control is the half that must NOT move when the port lands.
%%
%%   KSIG7    \key cis \major     7 fifths up      0 doubles, 7 singles
%%   KSIG8    \key cis \lydian    8 fifths up      1 double  (F), 6 singles
%%   KSIG12   \key ces \locrian  12 fifths down    5 doubles, 2 singles
%%   KSIGT    \key gis \major     8 fifths up      1 double  (F), 6 singles
%%
%% ⚠️ KSIGT IS THE SAME SIGNATURE AS KSIG8, REACHED BY A DIFFERENT ROUTE, and that
%% is the whole point of keeping both: KSIG8 spells the tonic with a word Lily#'s
%% table HAS (cis) and moves off it with a mode, KSIGT spells a tonic the table
%% LACKS (gis). One quantity, two ways of failing to reach it — so a port that
%% only widened the table would close KSIGT and leave KSIG8 open, and one that
%% only fixed the drawing would close KSIG8 and leave KSIGT open.
%%
%% ⚠️ `alteration` IS A RATIONAL IN WHOLE TONES, not a count of semitones: 1/2 is
%% one sharp and 1 is a double. Counting `(= 1 (abs alt))` therefore counts the
%% doubles, and `(not (zero? alt))` counts the drawn glyphs. Both are read off
%% the GROB's alteration-alist rather than off the context property, because that
%% is the list the stencil walks.
%%
%% ⚠️ `\bar "|."` because LilyPond does not end a score with a final bar line on
%% its own and Lily# always draws one (HANDOFF 6).

#(define ((dump-key tag) g)
   (let* ((alist (ly:grob-property g 'alteration-alist))
          (drawn (length (filter (lambda (p) (not (zero? (cdr p)))) alist)))
          (doubles (length (filter (lambda (p) (= 1 (abs (cdr p)))) alist))))
     (format #t "\nPROBE ~a KEYSIG glyphs=~a doubles=~a alist=~a\n"
             tag drawn doubles alist))
   '())

%% ⚠️ THE HOOK GOES IN `\layout`, NOT IN THE MUSIC — and it is written out per
%% score rather than wrapped in a music function. Two things were measured getting
%% here (2026-08-24, this probe's first two drafts):
%%   - an `\override` written AMONG THE NOTES takes effect where it stands, and the
%%     line-start KeySignature grob is already made by then. That draft compiled,
%%     said "Success", and printed NOTHING.
%%   - `#(define-music-function …)` cannot return a `\layout`: "music function
%%     cannot return #<Output_def>". So the block is spelled in each score, with
%%     `dump-key` curried on the tag.
%% It goes on \Score rather than \Voice for the reason HANDOFF 6 records for the
%% beam dump.

\paper {
  indent = 0
  ragged-right = ##t
}

\score { \new Staff { \time 4/4 \key cis \major
  \fixed c' { c4 d e f \bar "|." } }
  \layout { \context { \Score
    \override KeySignature.after-line-breaking = #(dump-key "KSIG7") } } }

\score { \new Staff { \time 4/4 \key cis \lydian
  \fixed c' { c4 d e f \bar "|." } }
  \layout { \context { \Score
    \override KeySignature.after-line-breaking = #(dump-key "KSIG8") } } }

\score { \new Staff { \time 4/4 \key ces \locrian
  \fixed c' { c4 d e f \bar "|." } }
  \layout { \context { \Score
    \override KeySignature.after-line-breaking = #(dump-key "KSIG12") } } }

\score { \new Staff { \time 4/4 \key gis \major
  \fixed c' { c4 d e f \bar "|." } }
  \layout { \context { \Score
    \override KeySignature.after-line-breaking = #(dump-key "KSIGT") } } }

\version "2.26.0"

%% WHERE DOES AN UP STEM STAND ON ITS NOTE HEAD?
%%
%% LilyPond puts it at the SUPPORT HEAD'S OWN ink right edge, less half the stem thickness:
%%
%%   lily/stem.cc:889-906 Stem::width      a stem's own X-extent is (-1,1)*thickness/2, so its
%%                                         ORIGIN is the middle of the drawn line.
%%   scm/define-grobs.scm Stem X-offset    ly:stem::offset-callback, which resolves to the
%%                                         support head's attachment on the X axis.
%%
%% The attachment is a FONT METRIC AND IT IS PER HEAD SHAPE: Emmentaler's black head is
%% 1.304200 wide and its half head 1.377400, and the stem stands at each head's own edge.
%%
%% ⚠️ LILY# TAKES THE BLACK HEAD'S ATTACHMENT FOR EVERY HEAD
%% (LayoutUtilities.StemAttachX reads GlyphMetrics.NoteheadBlackStemAttachment.X and nothing
%% else), so a HALF note's up stem stands 0.073200 left of LilyPond's while a QUARTER's is
%% exact. That is the pair: same book, same bar, same pitch, same stem direction, and the ONLY
%% thing that changes between the two readings is the head shape.
%%
%% ⚠️ ONE HEAD PER COLUMN, DELIBERATELY. In a CHORD of seconds the stem's origin lands within a
%% head-width of the displaced head, so "which head does this stem stand on" stops being
%% decidable from the drawing alone — and it stops being decidable exactly BECAUSE of the
%% quantity under test, which would make the instrument depend on the answer (HANDOFF 5.0:
%% suspect the instrument before the engine). A bare note has no such ambiguity.
%%
%% ⚠️ `\fixed c'` is what `lysc ly` emits: Lily#'s absolute `c` is LilyPond's `c'` (HANDOFF 6).
%% At c' the head sits at position -6, so every stem here points UP without being told to.
%%
%% ⚠️ `\bar "|."` because LilyPond does not end a score with a final bar line on its own and
%% Lily# always draws one (HANDOFF 6).
%%
%%   SX   c2 c4 c4    head 0 is a HALF note (divergent), heads 1-2 are QUARTERS (control)

#(define ((dump-head tag) g)
   (let* ((sys (ly:grob-system g))
          (ext (ly:grob-extent g sys X)))
     (format #t "\nPROBE ~a HEAD pos=~a x=(~,6f . ~,6f)\n"
             tag
             (ly:grob-property g 'staff-position)
             (car ext) (cdr ext)))
   '())

#(define ((dump-stem tag) g)
   (let* ((sys (ly:grob-system g))
          (ext (ly:grob-extent g sys X)))
     (format #t "PROBE ~a STEM dir=~a x=(~,6f . ~,6f) thickness=~,6f\n"
             tag
             (ly:grob-property g 'direction)
             (car ext) (cdr ext)
             (ly:grob-property g 'thickness)))
   '())

probe = #(define-music-function (tag) (string?)
           #{ \override NoteHead.after-line-breaking = #(dump-head tag)
              \override Stem.after-line-breaking = #(dump-stem tag) #})

\paper {
  indent = 0
  ragged-right = ##t
}

\score { \new Staff { \clef treble \time 4/4 \key c \major \probe "SX"
  \fixed c' { c2 c4 c4 \bar "|." } } }

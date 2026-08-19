\version "2.26.0"
%% LP FIDELITY PROBE - WHERE LYRICS SIT WHEN PEDAL OR DYNAMIC INK HANGS UNDER THE SAME STAFF.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe pedal-lyric-stack.ly -Prefix PROBEL
%%
%% THE DEFECT THIS MEASURES (session 216, re-measured at HEAD): on a Lily# staff with
%% lyrics, a pedal bracket is drawn THROUGH the syllables and an @f lands ON one. LilyPond
%% stacks staff -> pedal/dynamic -> lyrics and the LYRICS move down. PedalEngraver and
%% DynamicEngraver read no lyrics (measured, 0 occurrences); the lyric skyline-drop reads
%% the staff's down profile; the question is what LilyPond feeds that profile.
%%
%% THE QUANTITY: one system, ragged. For every book: the Lyrics VAG's rel-Y to the system
%% (staff refpoint is 0), plus the pedal / dynamic grob rows. staff-to-lyric = -(lyric rel).
%%
%% THE PAIR (HANDOFF 5.0-1): PLC is the control - THE SAME MUSIC with the pedal/dynamic
%% deleted. The difference PLB-PLC / PLD-PLC is by construction the term the pedal/dynamic
%% ink adds to the lyric floor, with both faces' ink cancelling.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), mechanism first:
%%  * PLC: the lyric spring's floor is staff down-ink (c' head, about 3.545) + lyric
%%    ascender + padding 0.5, which beats basic 5.5 (the LYRC first-draft number was
%%    5.865115 on this shape). Expect about 5.87, NOT 5.5 - if 5.5 comes back the floor is
%%    not binding and the whole probe is in the wrong regime (redesign, do not port).
%%  * PLB: the bracket hangs under the notes' ink; the lyric floor gains the bracket's row
%%    plus its edge height. Expect staff-to-lyric to GROW by roughly the bracket's depth
%%    (order 2-4 ss). The FORK that decides the port: if the Lyrics rel in PLB equals PLC's
%%    (lyrics do NOT move), then LilyPond keeps lyrics above pedals on this shape and the
%%    Lily# defect is elsewhere (the pedal's own row, not the lyric floor).
%%  * PLD: same fork with the f glyph (ink 2.588 tall). Expect growth of order 1-2 ss.
%%  * PLT: attribution only - the default TEXT style sustain, to see the Ped. glyph row on
%%    the same shape (Lily#'s default is BRACKET, so PLB is the porting pair; PLT guards
%%    against reading a style difference as a stacking difference).
%%
%% ⚠️ pedalSustainStyle lives on Staff; bracket style needs it set BEFORE the first note.

#(define (probe-dump tag layout pages)
   (for-each
    (lambda (page)
      (let ((lines (ly:prob-property page 'lines)))
        (for-each
         (lambda (sys)
           (let ((sg (ly:prob-property sys 'system-grob)))
             (if (ly:grob? sg)
                 (begin
                   ;; every vertical axis group: rel to system, affinity, own extent
                   (let ((align (ly:grob-object sg 'vertical-alignment)))
                     (if (ly:grob? align)
                         (for-each
                          (lambda (g)
                            (format #t "PROBEL ~a VAG rel=~a aff=~a ext=(~a . ~a)\n"
                                    tag
                                    (ly:grob-relative-coordinate g sg Y)
                                    (ly:grob-property g 'staff-affinity)
                                    (car (ly:grob-extent g g Y))
                                    (cdr (ly:grob-extent g g Y))))
                          (ly:grob-array->list (ly:grob-object align 'elements)))))
                   ;; the rows of interest, by grob name
                   (let ((all (ly:grob-object sg 'all-elements)))
                     (if (ly:grob-array? all)
                         (for-each
                          (lambda (g)
                            (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                              (if (memq nm '(PianoPedalBracket SustainPedal
                                             SustainPedalLineSpanner DynamicText
                                             DynamicLineSpanner LyricText))
                                  (format #t "PROBEL ~a GROB name=~a rel=~a ext=(~a . ~a)\n"
                                          tag nm
                                          (ly:grob-relative-coordinate g sg Y)
                                          (car (ly:grob-extent g g Y))
                                          (cdr (ly:grob-extent g g Y))))))
                          (ly:grob-array->list all))))))))
         lines)))
    pages))

probeL =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               indent = 0
               ragged-right = ##t
               ragged-bottom = ##t
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEL BOOK ~a\n" tag)
                                      (probe-dump tag layout pages)) } #})

%% PLC - CONTROL. The exact music of PLB/PLD with the pedal/dynamic deleted.
\book {
  \probeL "PLC"
  \score {
    <<
      \new Staff { \new Voice = "mel" { \time 4/4 c'4 d' e' f' | g'2 g' | } }
      \new Lyrics \lyricsto "mel" { la la la la ho ho }
    >>
  }
}

%% PLB - BRACKET-STYLE SUSTAIN under the sung staff (Lily#'s default style).
\book {
  \probeL "PLB"
  \score {
    <<
      \new Staff \with { pedalSustainStyle = #'bracket }
        { \new Voice = "mel" { \time 4/4 c'4 d'\sustainOn e' f'\sustainOff | g'2 g' | } }
      \new Lyrics \lyricsto "mel" { la la la la ho ho }
    >>
  }
}

%% PLT - TEXT-STYLE SUSTAIN (LilyPond default), attribution only.
\book {
  \probeL "PLT"
  \score {
    <<
      \new Staff
        { \new Voice = "mel" { \time 4/4 c'4 d'\sustainOn e' f'\sustainOff | g'2 g' | } }
      \new Lyrics \lyricsto "mel" { la la la la ho ho }
    >>
  }
}

%% PLD - A DYNAMIC on the sung staff.
\book {
  \probeL "PLD"
  \score {
    <<
      \new Staff { \new Voice = "mel" { \time 4/4 c'4 d' e' f' | g'2\f g' | } }
      \new Lyrics \lyricsto "mel" { la la la la ho ho }
    >>
  }
}

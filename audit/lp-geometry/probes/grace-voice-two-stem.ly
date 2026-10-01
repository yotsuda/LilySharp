\version "2.26.0"
%
% A GRACE IN A LOWER VOICE: WHICH WAY DOES ITS STEM POINT?
%
% score-grace-settings pushes (Voice Stem direction ,UP) for a grace body, but only as the
% DEFAULT graceSettings: \voiceOne ... \voiceFour (make-voice-props-set,
% scm/music-functions.scm:666-674) set graceSettings to general-grace-settings, which names
% no direction - so a lower voice's grace keeps the voice's Stem direction, DOWN. Lily# drew
% every grace stem UP until session 726.
%
% GDB: a beamed pair.  GDF: one flagged eighth.  GDA: an acciaccatura (flag + stroke).
% GDC: a flagged chord.  The upper voice is a spacer, so the only heads are the lower voice's.
%
% Bodies are what `lysc ly` emitted for the .lys books GDB/GDF/GDA/GDC (LpGeometryProbes.cs).
%
% Output: PROBEGV2 <tag> STEM dir=<direction> tip=<stem tip above the staff's middle line>
%                    x=<stem x> fs=<font-size>
%         PROBEGV2 <tag> BEAM positions=<positions>
%         PROBEGV2 <tag> FLAG y=<flag refpoint above the staff's middle line>
%         PROBEGV2 <tag> HEAD x=<notehead refpoint x> fs=<font-size>

\paper { indent = 0 ragged-right = ##t }

#(define (staff-y grob sys)
   (let ((st (ly:grob-object grob 'staff-symbol)))
     (if (ly:grob? st) (ly:grob-relative-coordinate st sys Y) 0)))

#(define (dump-stem tag)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (dir (ly:grob-property grob 'direction))
            (ext (ly:grob-extent grob sys Y))
            (tip (if (> dir 0) (cdr ext) (car ext))))
       (format #t "\nPROBEGV2 ~a STEM dir=~a tip=~a x=~a fs=~a\n" tag dir
               (- tip (staff-y grob sys))
               (ly:grob-relative-coordinate grob sys X)
               (ly:grob-property grob 'font-size)))))

#(define (dump-beam tag)
   (lambda (grob)
     (format #t "\nPROBEGV2 ~a BEAM positions=~a\n" tag (ly:grob-property grob 'positions))))

#(define (dump-flag tag)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (stem (ly:grob-parent grob X)))
       (format #t "\nPROBEGV2 ~a FLAG y=~a\n" tag
               (- (ly:grob-relative-coordinate grob sys Y) (staff-y stem sys))))))

#(define (dump-head tag)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBEGV2 ~a HEAD x=~a fs=~a\n" tag
               (ly:grob-relative-coordinate grob sys X)
               (ly:grob-property grob 'font-size)))))

probe =
#(define-music-function (tag music) (string? ly:music?)
   #{ \new Staff \with {
        \override Stem.after-line-breaking = #(dump-stem tag)
        \override Beam.after-line-breaking = #(dump-beam tag)
        \override Flag.after-line-breaking = #(dump-flag tag)
        \override NoteHead.after-line-breaking = #(dump-head tag)
      } { \clef "treble" $music } #})

\score { \probe "GDB" \fixed c' { \time 4/4 << { s1 } \\ { c4 \grace { d16 e16 } c4 e4 f4 } >> | } }
\score { \probe "GDF" \fixed c' { \time 4/4 << { s1 } \\ { c4 \grace { d8 } c4 e4 f4 } >> | } }
\score { \probe "GDA" \fixed c' { \time 4/4 << { s1 } \\ { c4 \acciaccatura { b8 } c4 e4 f4 } >> | } }
\score { \probe "GDC" \fixed c' { \time 4/4 << { s1 } \\ { c4 \grace { <d f>8 } c4 e4 f4 } >> | } }

% GDO: the lower voice's grace OPENS a bar. The bar line's optical correction reads the stems of
% the column its spring stops at - the grace column, whose stem is DOWN here, so the correction
% fires (lily/staff-spacing.cc:95-110 next_notes_correction).
% Output: PROBEGV2 GDO BAR right=<bar line ink right>   PROBEGV2 GDO INK left=<notehead ink left>
#(define (dump-bar tag)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBEGV2 ~a BAR right=~a\n" tag (cdr (ly:grob-extent grob sys X))))))
#(define (dump-ink tag)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBEGV2 ~a INK left=~a fs=~a\n" tag (car (ly:grob-extent grob sys X))
               (ly:grob-property grob 'font-size)))))
\score { \new Staff \with {
           \override BarLine.after-line-breaking = #(dump-bar "GDO")
           \override NoteHead.after-line-breaking = #(dump-ink "GDO")
         } { \clef "treble" \fixed c' { \time 4/4 c1 | << { s1 } \\ { \grace { d'8 } c'4 e4 f4 g4 } >> | } } }

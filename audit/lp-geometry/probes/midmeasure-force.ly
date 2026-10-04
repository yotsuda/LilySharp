\version "2.26.0"
%
% A MID-MEASURE CHANGE ON A LINE THAT IS NOT AT FORCE 0 (session 810). Every other mid-measure
% point (barline-spacing.ly MC, MK, ...) is ragged, which sits each spring at its ideal and so
% cannot tell ONE spring from TWO. LilyPond gives the change its own NonMusicalPaperColumn: the
% note before it reaches it through a Note_spacing spring (lily/note-spacing.cc:42-115, merged
% by lily/spacing-spanner.cc:322-393) and the change reaches the next note through a
% Staff_spacing spring (lily/staff-spacing.cc:117-221, merged by :478-536). The two stretch and
% compress on their own strengths: the right one's stretch is ideal - fixed for a clef
% (extra-space) and 0 for a key (shrink-space, is_stretchable = false, :188-192); its compress
% strength is ideal - fixed. Lily# carried both gaps on ONE spring until session 810 and hung
% the change glyph back from the next note by the force-0 right gap.
%
% MFCJ / MFKJ: MC's and MK's music on a justified 100mm line (two systems; the second is a
% whole-bar rest, so every head is the first system's). MFCC / MFKC: the same music on a 36mm
% line, narrower than its natural width. (33mm overflows the key bar.)
% The Lily# books are MFCJ, MFKJ, MFCC, MFKC (LpGeometryProbes.cs).
%
% Output: PROBE <score> <grob> x=<system-relative x> ...
#(define ((gd tag name) g)
   (let ((sys (ly:grob-system g)))
     (format #t "\nPROBE ~a ~a x=~a ext=~a\n" tag name
             (ly:grob-relative-coordinate g sys X)
             (ly:grob-extent g g X))))
lay =
#(define-scheme-function (tag width) (string? number?)
   #{
     \layout {
       indent = 0
       line-width = $width \mm
       \context {
         \Score
         \override NoteHead.after-line-breaking        = #(gd tag "HEAD")
         \override Clef.after-line-breaking            = #(gd tag "CLEF")
         \override KeySignature.after-line-breaking    = #(gd tag "KEY")
         \override KeyCancellation.after-line-breaking = #(gd tag "KEY")
         \override BarLine.after-line-breaking         = #(gd tag "BAR")
       }
     }
   #})
\header { tagline = ##f }
\score { \new Staff { \time 4/4 c'4 d' \clef bass e4 f4 | \break R1 | } \lay "MFCJ" #100 }
\score { \new Staff { \time 4/4 c'4 d' \key a \major e'4 f'4 | \break R1 | } \lay "MFKJ" #100 }
\score { \new Staff { \time 4/4 c'4 d' \clef bass e4 f4 | \break R1 | } \lay "MFCC" #36 }
\score { \new Staff { \time 4/4 c'4 d' \key a \major e'4 f'4 | \break R1 | } \lay "MFKC" #36 }

\version "2.26.0"

%% Every TERM of system-start-text::calc-x-offset, asked of LilyPond rather than
%% derived, because the port is of that function and its first term
%% (ly:side-position-interface::x-aligned-side) depends on a support set no
%% reading of the source pins down.
%%
%%   X-offset = (ly:side-position-interface::x-aligned-side grob)
%%              + right-padding
%%              + (- (- indent total-left))
%%
%%   padding       = (min 0 (- name-width indent))     ; NEGATIVE while the name is
%%                                                     ; NARROWER than the indent, 0 when wider
%%   right-padding = padding - padding * (1 + align-x) / 2
%%                 = padding / 2                       ; align-x = CENTER = 0
%%   total-left    = leftmost edge of any system-start-delimiter in the system
%%
%% Four names of different widths so the port can be checked against a SPREAD and
%% not one book: with indent at its 15\mm default only Contrabassoon is wider than
%% the indent, which is the branch where `padding` goes to zero.
%%
%% ⚠️⚠️ RUN IT WITH -dbackend=null FOR LILYPOND'S WIDTHS. Under -dbackend=svg LilyPond 2.26
%% drops fonts.serif to the generic "serif" and fontconfig picks a machine face
%% (ly/paper-defaults-init.ly:169-181), so every name comes out a different width: Soprano
%% 7.306668 (svg) against 8.365110 (null), Contrabassoon 13.042743 against 15.057198
%% (re-run 2026-09-13, session 376, scratch/p377/probes). The rows in
%% LilySharp.Tests/InstrumentNamePlacementTests are the SVG widths. That does not weaken
%% them — the rule is linear in the width, and the null run's right edges move by exactly
%% half of each width change (Soprano +0.529221 for +1.058442; Contrabassoon, past the
%% indent, not at all) — but the widths are one machine's fallback face, not LilyPond Serif.

#(define (dump-name grob)
   (let* ((sys (ly:grob-system grob))
          (sysx (ly:grob-extent grob sys X))
          (ownx (ly:grob-extent grob grob X))
          (layout (ly:grob-layout grob))
          (indent (ly:output-def-lookup layout 'indent 0.0))
          (elements (ly:grob-object sys 'elements))
          (total-left
            (let loop ((l (ly:grob-array-length elements)) (acc 1e9))
              (if (= l 0)
                  acc
                  (let ((elt (ly:grob-array-ref elements (1- l))))
                    (loop (1- l)
                          (if (grob::has-interface elt 'system-start-delimiter-interface)
                              (min acc (car (ly:grob-extent elt sys X)))
                              acc))))))
          (xas (ly:side-position-interface::x-aligned-side grob)))
     (format (current-error-port)
             "PROBE name sys=~a..~a own=~a..~a xaligned=~a indent=~a totalleft=~a\n"
             (car sysx) (cdr sysx) (car ownx) (cdr ownx) xas indent total-left))
   (ly:grob-set-property! grob 'after-line-breaking #f)
   '())

#(define (dump-delim grob)
   (let ((x (ly:grob-extent grob (ly:grob-system grob) X)))
     (format (current-error-port) "PROBE delim sys=~a..~a\n" (car x) (cdr x)))
   (ly:grob-set-property! grob 'after-line-breaking #f)
   '())

%% WHERE THE STAFF ACTUALLY STARTS — and the answer to the residual this probe
%% was opened with. Lily# puts the brace's right edge at `indent - 0.3`, while
%% LilyPond's is at indent - 0.36, and the 0.36 was not derivable from anything
%% declared. Measured here, book 1 (which carries BOTH delimiters, because
%% LilyPond adds a Score-level SystemStartBar to any multi-staff system):
%%
%%     StaffSymbol       8.585826771653544 ..            = indent + 0.05
%%     SystemStartBar    8.475826771653542 .. 8.635826771653543
%%     SystemStartBrace  6.8024267716535425 .. 8.175826771653544
%%
%%     8.475826771653542 - 0.3 = 8.175826771653542       = the brace's right edge
%%
%% ⇒ THE BRACE IS SIDE-POSITIONED AGAINST THE BAR, not against the staff and not
%% against the indent. The delimiters CHAIN: the bar sits on the staff, the brace
%% clears the bar by its own 0.3 padding. That is why no arithmetic on the indent
%% produced 0.36 — the 0.06 is where the BAR is, and the brace only ever saw the
%% bar. Fifteen digits, so it is not a coincidence.
%%
%% ⚠️ NOT PORTED. Lily# anchors the brace on the indent, so its brace sits about
%% 0.08 right of LilyPond's, and moving it also moves every instrument name (the
%% name is placed against the leftmost delimiter, which is the brace). Both are
%% drawn-output changes and belong with their own approval.
%% ⚠️ STILL NOT PORTED on 2026-09-13 (session 376): MultiStaffLayouter places
%% braceX = CurrentIndent - SystemStartBracePadding. No ledger point holds the 0.06
%% either — it is named in HANDOFF §2 E so it is not lost with this header.
#(define (dump-staff grob)
   (let ((x (ly:grob-extent grob (ly:grob-system grob) X)))
     (format (current-error-port) "PROBE staffsym sys=~a..~a\n" (car x) (cdr x)))
   (ly:grob-set-property! grob 'after-line-breaking #f)
   '())

nameDump = \layout {
  \context {
    \Score
    \override InstrumentName.after-line-breaking = #dump-name
    \override SystemStartBrace.after-line-breaking = #dump-delim
    \override SystemStartBar.after-line-breaking = #dump-delim
    \override SystemStartBracket.after-line-breaking = #dump-delim
    \override StaffSymbol.after-line-breaking = #dump-staff
  }
}

%% BOOK 1 — a braced group. The delimiter PRINTS, so total-left is its left edge.
\score {
  \new GrandStaff <<
    \new Staff \with { instrumentName = "I" }             { c'1 }
    \new Staff \with { instrumentName = "Alto" }          { c'1 }
    \new Staff \with { instrumentName = "Soprano" }       { c'1 }
    \new Staff \with { instrumentName = "Contrabassoon" } { \clef bass c1 }
  >>
  \layout { \nameDump }
}

%% BOOK 2 — two plain staves. The delimiter is a SystemStartBar, and whether it
%% prints at this height is exactly the question: collapse-height is 5.0.
\score {
  <<
    \new Staff \with { instrumentName = "Soprano" } { c'1 }
    \new Staff \with { instrumentName = "Bass" }    { \clef bass c1 }
  >>
  \layout { \nameDump }
}

%% BOOK 3 — ONE staff, so the delimiter certainly collapses and there may be no
%% delimiter extent to take a minimum over. total-left stays at the +inf.0 the
%% callback seeds it with, and what LilyPond does then is not derivable from the
%% source — it is measured here instead.
\score {
  \new Staff \with { instrumentName = "Soprano" } { c'1 }
  \layout { \nameDump }
}

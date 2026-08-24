\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A REHEARSAL MARK SITS WHEN A CHORDNAMES LINE LEADS THE
%% SYSTEM.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe mark-chord-row.ly -Prefix PROBEM
%%
%% THE DEFECT THIS MEASURES (user report, session 243): on a lead sheet written
%% `chords / staff / lyrics`, Lily#'s section marks ride ABOVE the whole chord band —
%% 7.060 over the staff's top line, where every other row order puts them at 2.660.
%% MusicMarkLayout carries StaffIndex = -1 for "the top staff", and the sentinel was
%% resolved in two places that disagreed: OutsideStaffStacker's tracker resolved it to
%% the topmost PLACED element (so the mark was PRICED against the melody staff once
%% TopStaffIndex learned to skip rows), while StaffOffsetInSystemUp never resolved it at
%% all — its `staffIndex >= 0` guard returns 0, the SYSTEM TOP, which is the chord row's
%% band top. Priced on one line, drawn from another.
%%
%% ⚠️ THIS IS THE SECOND HALF OF AN ISLAND THAT WAS HALF-CLOSED. barnumber-chord-row.ly
%% settled the identical question for the BAR NUMBER on 2026-08-20, and that entry's
%% `why` already stated the general fact — "the two differ exactly when a chords/lyrics
%% row leads the system" — and described its own spelling as "one spelling shared with
%% the stacker's tracker choice". It was not shared. The mark kept its own, and its own
%% defect, for four more sessions.
%%
%% LILYPOND'S MECHANISM: a RehearsalMark is a Score-context grob whose Y-parent is a
%% STAFF's VerticalAxisGroup — a ChordNames context is not one, so the mark is side-
%% positioned against the staff and lands in the chord row's own band rather than above
%% it. (Unlike the bar number it carries no move-to-extremal-staff re-parenting; there is
%% nothing to re-parent it ONTO, which is why this probe reads the staff refpoint
%% directly.)
%%
%% PREDICTION, written before running (HANDOFF 5.0-2), mechanism first: the mark's ink
%% bottom should sit at its ordinary above-staff distance over the staff refpoint — the
%% same number book MKP (no chord row) reads — UNMOVED by the chord line, exactly as the
%% bar number was. FALSIFIER, and it is the whole probe: a reading on MKC that is HIGHER
%% than MKP's by about the chord band means LilyPond DOES lift the mark over the row, the
%% port's old behaviour was right, and what needs fixing is only the two-spellings split.
%%
%% THE PAIR (HANDOFF 5.0-1): MKC and MKP are ONE VARIABLE apart — whether a ChordNames
%% context is present — and nothing else. Same staff, same music, same marks, same paper.

#(define (dump tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (memq nm '(RehearsalMark SectionLabel StaffSymbol ChordName))
                              (format #t "PROBEM ~a ~a rel=~a ext=(~a . ~a) X=~a xext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-relative-coordinate g sg X)
                                      (car (ly:grob-extent g g X))
                                      (cdr (ly:grob-extent g g X))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeM =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEM BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})
\book {
  \probeM "MKC"
  \score {
    <<
      \new ChordNames \chordmode { c1 g a:m f c g a:m f }
      \new Staff \relative c'' {
        \time 4/4
        \mark "A" c4 d e f | g a b c | c4 b a g | f e d c | \break
        \mark "B" c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

\book {
  \probeM "MKP"
  \score {
    <<
      \new Staff \relative c'' {
        \time 4/4
        \mark "A" c4 d e f | g a b c | c4 b a g | f e d c | \break
        \mark "B" c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

\book {
  \probeM "MKL"
  \score {
    <<
      \new ChordNames \chordmode { c1 g a:m f c g a:m f }
      \new Staff \relative c' {
        \time 4/4
        \mark "A" c4 d e f | g a b c | c4 b a g | f e d c | \break
        \mark "B" c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

\book {
  \probeM "MKQ"
  \score {
    <<
      \new Staff \relative c' {
        \time 4/4
        \mark "A" c4 d e f | g a b c | c4 b a g | f e d c | \break
        \mark "B" c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

%% MKS -- the ROWS-ONLY sheet: a ChordNames line and a Lyrics line and NO Staff at all,
%% which is what the reporting user actually writes. The question is where a \mark goes
%% when side-position-interface finds no staff to be positioned against.
\book {
  \probeM "MKS"
  \score {
    <<
      \new ChordNames \chordmode { \mark "A" c1 g a:m f \break \mark "B" c1 g a:m f }
      \new Lyrics \lyricmode {
        \set stanza = "" one4 two three four five six sev -- en
        eight nine ten e -- le -- ven twelve thir -- teen
      }
    >>
    \layout { \context { \Score \consists "Mark_engraver" } }
  }
}

%% MKK -- the SAME book as MKP with ONE variable added: a KEY SIGNATURE. RehearsalMark
%% declares break-align-symbols = (staff-bar key-signature clef) (define-grobs.scm:2881),
%% so if the mark is anchored on a break-align column its X must MOVE when a key signature
%% appears between the clef and the meter. If it does not move, the anchor is the staff-bar
%% and the clef/key are not what places it.
\book {
  \probeM "MKK"
  \score {
    <<
      \new Staff \relative c'' {
        \key d \major
        \time 4/4
        \mark "A" c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

%% MKB -- the SECTION LABEL, which is a DIFFERENT GROB from the rehearsal mark and the one
%% Lily# actually draws for a `form` section name. scm/define-grobs.scm SectionLabel:
%%   (break-align-symbols . (left-edge staff-bar))   <- NOT (staff-bar key-signature clef)
%%   (self-alignment-X . LEFT)  (padding . 0.8)  (outside-staff-priority . 1450)
%% so it anchors on the LEFT EDGE at a line start, where the rehearsal mark anchors on the
%% clef/key. Same book as MKP otherwise, so the two are one variable apart: which grob.
\book {
  \probeM "MKB"
  \score {
    <<
      \new Staff \relative c'' {
        \time 4/4
        \sectionLabel "A" c4 d e f | g a b c | c4 b a g | f e d c | \break
        \sectionLabel "B" c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}

%% MKT -- the ROWS-ONLY sheet with a SECTION LABEL, which is the exact shape the reporting
%% user writes (chords row + lyrics row, no staff, `form` section names). MKS is the same
%% book with a REHEARSAL mark; the two differ only in which grob, and they have different
%% anchors and different outside-staff priorities (1450 vs 1500), so the pair says whether
%% that difference reaches the drawn Y on a staffless sheet.
\book {
  \probeM "MKT"
  \score {
    <<
      \new ChordNames \chordmode { \sectionLabel "A" c1 g a:m f \break \sectionLabel "B" c1 g a:m f }
      \new Lyrics \lyricmode {
        \set stanza = "" one4 two three four five six sev -- en
        eight nine ten e -- le -- ven twelve thir -- teen
      }
    >>
    \layout { \context { \Score \consists "Mark_engraver" } }
  }
}

%% MKV -- MKT WITH ONE VARIABLE CHANGED: the chord symbols are TALLER. `cis' prints a raised
%% accidental, so every symbol's ink top rises while nothing else about the book moves --
%% same lyrics, same labels, same paper, same break, same columns.
%%
%% WHY THE PAIR EXISTS (session 244, HANDOFF 5.0-1). MKT alone reads an absolute, and an
%% absolute here carries a FONT TERM: the two engines' chord faces differ, so `the symbol's
%% ink top' is not the same number on both sides and a residual taken from one book cannot
%% say how much of itself is the defect. MKT/MKV is the identity form instead -- LilyPond
%% places the label against the ROW'S OWN INK, so raising the ink raises the label and the
%% GAP is the same on both books, while an engine whose label hangs off the row's BAND TOP
%% instead reads a gap that SHRINKS by exactly the ink it grew. LilyPond's difference is
%% zero by construction; whatever difference Lily# shows between the two is its own.
%%
%% PREDICTION, written before running (HANDOFF 5.0-2): both books read the same gap,
%% outside-staff-padding 0.460000, because side-position-interface places the mark against
%% the accumulated skyline of the ChordNames axis group and that skyline IS the symbols.
%% FALSIFIER: a gap that GROWS on MKV means the label is placed against something other than
%% the symbols' ink -- a context extent, or a fixed step -- and the port would then have a
%% different quantity to copy.
\book {
  \probeM "MKV"
  \score {
    <<
      \new ChordNames \chordmode { \sectionLabel "A" cis1 gis ais:m fis \break \sectionLabel "B" cis1 gis ais:m fis }
      \new Lyrics \lyricmode {
        \set stanza = "" one4 two three four five six sev -- en
        eight nine ten e -- le -- ven twelve thir -- teen
      }
    >>
    \layout { \context { \Score \consists "Mark_engraver" } }
  }
}

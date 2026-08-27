\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A REHEARSAL MARK SITS WHEN A CHORDNAMES LINE LEADS THE
%% SYSTEM.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe mark-chord-row.ly -Prefix PROBEM
%%
%% ⚠️⚠️ THE SERIF FACE IS NOT PINNED HERE, AND THE LEDGER'S FOUR mark.* VALUES ARE
%% MINTED UNDER THE MACHINE'S FALLBACK RESOLUTION, NOT UNDER "LilyPond Serif"
%% (audited 2026-08-26, session 258, by diffing a pinned run against a bare one on the
%% same binary). The staff-to-baseline differences of the two-system books (MKC/MKP/
%% MKL/MKQ: 2.850000/5.845000/3.860184...) are face-INVARIANT - mark and staff shift
%% together - but the four the ledger records are not:
%%   mark.chord-row.staff-to-baseline  4.117029  = MKB bare (pinned reads 4.197549, +0.080520)
%%   mark.plain.staff-to-baseline      4.117029  = same family, same shift
%%   mark.over-chord.staff-to-baseline 7.381627  = MKW bare (pinned 7.412291, +0.030664)
%%   mark.over-chord.tall...           8.652725  = MKX bare (pinned 8.683389, +0.030664)
%% ⚠️ chord-lyric-run.ly's values are minted under the OTHER face (the canonical pin) -
%% the ledger currently mixes two serif faces across probes, stamped by whichever face
%% the machine resolved on each probe's measuring day.
%% ⇒ WHOEVER NEXT TOUCHES THE +0.7706 ISLAND (ChordClearancePadding, guarded by the two
%% over-chord points): PIN fonts.serif HERE FIRST, re-measure, re-record the four values
%% and their Lily#-side residuals (they shift by the deltas above), and only then port.
%% Re-minting was deliberately NOT done in session 258 so the guard and the port move in
%% one commit rather than the guard moving alone.
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
                          ;; Clef and KeySignature joined 2026-08-27 (session 270): the X
                          ;; half of the rehearsal-mark story break-aligns on them
                          ;; (break-align-symbols (staff-bar key-signature clef), anchor =
                          ;; the grob's ink RIGHT edge), so the pair "mark box left =
                          ;; anchor right" needs both edges in the same dump.
                          (if (memq nm '(RehearsalMark SectionLabel StaffSymbol ChordName
                                         Clef KeySignature))
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

%% MKW / MKX -- THE SHAPE NOTHING IN THE LEDGER WATCHED: a section label that stands OVER a
%% chord symbol ON A STAFFED SHEET.  Opened 2026-08-25 (session 253) to decide one question
%% the owner asked outright: should Lily# port LilyPond's lift (move_to_extremal_staff +
%% get_extremal_staff + the row's own inside_staff_skylines)?
%%
%% WHY IT WAS MISSING.  Every existing entry puts the label at a LINE START, where a
%% SectionLabel anchors on `left-edge' and the chords begin after the clef -- so the two never
%% overlap in X and no lift can fire.  MKR/MKN measure exactly that (and their +0.542971 is a
%% label-alone difference with no row in it); MKT/MKS/MKV measure the overlap on a sheet with
%% NO STAFF, where LilyPond reads the row's ink top + 0.460000 and Lily# reads 0.900000 --
%% and that gap is deliberately open by the owner's decision of 2026-08-24.  Between them the
%% STAFFED overlap was never asked.  MEASURED IN LILY# 2026-08-25, a section label's baseline
%% above the staff top with one variable moved (the chord standing under it):
%%     no chord row               2.40
%%     row, short chord under it  4.69
%%     row, TALL chord under it   5.72
%% -- so Lily# already lifts, X-aware and by the ink (a label clear of every chord in X reads
%% 2.66 in all three of those books).  The arm is MusicMarkEngraver's own, not a port.
%% => THE PORT IS THEREFORE A REPLACEMENT, NOT AN ADDITION, and nothing can say which of the
%% two answers is LilyPond's until this pair exists.
%%
%% THE BOOKS.  ONE system, four bars, chords on every bar, and exactly ONE label -- at bar 3,
%% MID-LINE, where a SectionLabel anchors on `staff-bar' and that bar's chord sits at the same
%% barline.  One label so the ledger's existing FirstMusicMarkBaselineAboveStaff measures the
%% one that overlaps; mid-line so that it overlaps at all.
%% MKX is MKW with `cis' chords: every symbol's ink top rises and nothing else moves.
%%
%% WHY A PAIR AND NOT AN ABSOLUTE (the same reason MKT/MKV are a pair -- see MKV below).
%% Lily# BOXES its label where LilyPond draws bare text, so an absolute baseline carries a box
%% term that is not part of this question; MKR's own entry says as much.  The IDENTITY has no
%% box term in it: LilyPond places the label against the ChordNames axis group's accumulated
%% skyline, which IS the symbols, so raising the ink raises the label by exactly the ink and
%% the GAP is unchanged.  LilyPond's difference between MKW and MKX is the ink growth exactly;
%% whatever difference Lily# shows is its own.
%%
%% ★ PREDICTION, written before running (HANDOFF 5.0-2): LilyPond's MKX baseline = MKW's plus
%% the chords' ink growth, gap held at outside-staff-padding 0.460000.  Lily#'s arm clears
%% "the highest chord top its own ink overlaps", which is also ink-based, so it should track
%% the growth too and THE TWO DIFFERENCES SHOULD MATCH -- i.e. both books carry the SAME
%% residual, and that residual is the older label-alone term and not a lift error.
%% ⚠️ FALSIFIER, and it IS the decision: if the two differences do NOT match, Lily#'s arm is
%% not placing against the ink the way LilyPond does, the size of the mismatch is the size of
%% the defect, and the port finally has an observer and a target.  If they DO match, the arm
%% is already doing LilyPond's job and porting move_to_extremal_staff would buy nothing while
%% moving output the owner has approved -- which is the answer to the question that opened
%% these books.
%%
%% *** RESULT (session 253): FALSIFIED.  Lily# reads 8.145000 on BOTH books -- difference
%% 0.000000 against LilyPond's 1.271099 -- so the arm does not track the chord's ink.
%% *** AND THE CAUSE IS NOT WHERE SESSION 253 PUT IT (session 254, measured 2026-08-25).
%% That session read the flat difference as `the arm uses a nominal ascent where LilyPond
%% reads the outline' and prescribed: seed a text row's inside_staff_skylines, then make the
%% arm measure against that profile.  THE ARM IS INDEED NOMINAL -- MusicMarkEngraver's
%% ChordTextAscent = 1.9 against a real 1.907250371 -- BUT THAT IS WORTH 0.007, NOT 1.271099,
%% AND FIXING IT CANNOT MOVE THIS PAIR AT ALL: in Lily# `A#m' and `Am' have THE SAME INK BOX,
%% (0.0 . 1.907250371) both, so there is no ink difference to read.
%% => THE DEFECT IS THE CHORD SYMBOL'S OWN MARKUP, upstream of every mark.  Lily# spells an
%% altered root with the CHARACTERS U+266F / U+266D, and TeX Gyre Heros -- the face that
%% measures a ChordName -- has neither glyph: both read ink (0,0) and the .notdef advance
%% 1.297445669 (U+FFFD reads the same).  So a chord accidental is in no skyline, and the
%% glyph that appears in the picture comes from the platform's font fallback.
%% LilyPond puts no accidental CHARACTER in a chord name at all -- scm/chord-name.scm:80-95
%% accidental->text-markup / accidental->markup draws the Emmentaler accidental GLYPH one
%% step \smaller, translate-scaled up by 0.6 (0.3 for the flat family), kerned 0.094725
%% before the narrow glyphs.  MEASURED HERE IN 2.26.0 (ChordName stencil extents):
%%     Am    Y (0.0                 . 1.907290480437992)   X (0.0 . 3.9264803149606298)
%%     A#m   Y (-0.9535167849233657 . 2.22487249815452)    X (0.0 . 5.091889718755855)
%% -- +0.317582 above the baseline and +0.953517 below it = 1.271098802639894 of INK HEIGHT,
%% which is this pair's whole difference to fifteen digits.
%% ⚠️ IT IS THE HEIGHT, NOT THE TOP: three quarters of it is ink BELOW the baseline and
%% reaches the mark by pushing the ChordNames ROW up off the staff, not by raising the
%% symbol's top.  An ink-based arm alone would collect 0.317582 of the 1.271099 at most.
\book {
  \probeM "MKW"
  \score {
    <<
      \new ChordNames \chordmode { c1 g a:m f }
      \new Staff \relative c'' {
        \time 4/4
        c4 d e f | g a b c | \sectionLabel "B" c4 b a g | f e d c |
      }
    >>
  }
}

%% MKX -- MKW with TALLER chord symbols and nothing else changed.  See MKW's header.
\book {
  \probeM "MKX"
  \score {
    <<
      \new ChordNames \chordmode { cis1 gis ais:m fis }
      \new Staff \relative c'' {
        \time 4/4
        c4 d e f | g a b c | \sectionLabel "B" c4 b a g | f e d c |
      }
    >>
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

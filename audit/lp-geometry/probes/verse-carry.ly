\version "2.26.0"
%% LP FIDELITY PROBE - VERSE rows in the loose run, and what an ABSENT verse reserves.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe verse-carry.ly -Prefix PROBEVR
%%
%% THE PORT THIS MEASURES FOR (HANDOFF session 257 (7)/(13)): Lily#'s staff-pair distance on
%% a system with rows between the staves is still the BAND stack — LayoutStaffGroups
%% advances its running Y by each element's own height, and a lyrics row's height is
%% StaffHeight + (TextRowVerses - 1) * 3.2 — against LilyPond's one walk of the run
%% (Align_interface::internal_get_minimum_translations). Two facts have to be measured
%% before that band can be replaced by the walk, and this probe is both:
%%
%%   1. WHAT THE WALK READS BETWEEN TWO VERSES. In LilyPond every verse is a Lyrics
%%      line of its own, so a "row with N verses" is N elements of the run and the
%%      verse step is a get_spacing_spec branch like any other (loose/loose, both
%%      affinity UP -> the upper line's nonstaff-nonstaff-spacing, whose
%%      minimum-distance 2.8 floors it). Lily# prices it as a flat 3.2
%%      (MultiStaffLayouter.TextRowVerseSpacing).
%%
%%   2. WHAT AN ABSENT VERSE RESERVES: NOTHING. Staff.TextRowVerses is a SCORE-WIDE
%%      maximum in Lily#, so a system whose section carries one verse still reserves
%%      the band for the score's deepest section (+3.2 on the reported book). LilyPond
%%      has no such carry — a Lyrics context with nothing on a system is removed
%%      (remove-empty) and reserves nothing. VRC1's first system must therefore read
%%      EXACTLY like VRS1's only system, and its second like VRS2's.
%%
%% The books share chord-lyric-run.ly's melody, so the numbers are comparable with the
%% CHL family: CHL3 (staff / lyrics / staff) read 4.650841258 / 5.045000000 there.
%%
%% MEASURED (2026-08-26, 2.26.0, fonts pinned), refpoint to refpoint:
%%   VRS1  staff / v1 / staff        4.650841258  5.045000000              =  9.695841258
%%   VRS2  staff / v1 / v2 / staff   4.650841258  2.800000000  5.082044154 = 12.532885412
%%   VRS3  staff / v1 / v2 / v3 / staff
%%                       4.650841258  2.800000000  2.800000000  5.082044154 = 15.332885412
%%   VRC1  system 1                  4.650841258  5.045000000              =  9.695841258
%%         system 2                  4.650841258  2.800000000  5.082044154 = 12.532885412
%%
%% ★★★ THE VERSE STEP IS 2.800000000 EXACTLY, and adding a third verse adds another
%% 2.800000000 and NOTHING ELSE: the Lyrics context's nonstaff-nonstaff-spacing
%% minimum-distance 2.8 binds (this verse ink is 0.509 descender + 0.2 padding + 1.820
%% ascender = 2.529 < 2.8, so the floor is the minimum, not the ink). Lily#'s
%% TextRowVerseSpacing prices the same step at a flat 3.2.
%%
%% ★★★ THE CARRY IS ZERO: VRC1's system 1 reads DIGIT FOR DIGIT like VRS1's only
%% system, and its system 2 like VRS2's — the verse that does not sing on a system
%% reserves nothing there (remove-empty), while Lily#'s Staff.TextRowVerses is a
%% score-wide maximum and reserves the deepest section's band on every system.
%%
%% ★★ AND THE CLOSING STEP READS THE INK IT CLOSES OVER: v1-to-staff is 5.045000000
%% when v1 is the last line (VRS1) but v2-to-staff is 5.082044154 (VRS2/VRS3, both
%% verse choices) — the last line's own spring, priced from that line's ink.
%%
%% ⚠️ THE SERIF FONT IS PINNED: under the svg backend LilyPond's fonts.serif falls back
%% to whatever fontconfig resolves on this machine, and this session watched exactly that
%% move chord-lyric-run.ly's lyric steps between two same-day runs of one binary (see the
%% note there). Every lyric ink in this probe is C059's, deterministically.
%%
%% ⚠️ THE LYRICS NEED A NAMED **Voice** (chord-lyric-run.ly's trap): \lyricsto takes a
%% Voice, and with the name missing the Lyrics context stays empty, remove-empty kills
%% it, and the book silently measures the staff-only arrangement. The per-system
%% Lyrics COUNT in the dump is this probe's own guard against that.
%%
%% ⚠️ VRC1's verse 2 skips its first system with \skip, not with silence spelled some
%% other way: a \skip in \lyricsto consumes one melody note and engraves nothing, so
%% system 1 has a LIVE Lyrics context with no grobs — exactly the state remove-empty
%% judges. That the kill actually happened is read off the dump (system 1 must count
%% ONE Lyrics group, system 2 TWO).

#(define (dump tag layout pages)
   (for-each
    (lambda (page)
      (let ((sysno 0))
        (for-each
         (lambda (sys)
           (let ((sg (ly:prob-property sys 'system-grob)))
             (if (ly:grob? sg)
                 (begin
                   (set! sysno (1+ sysno))
                   (let ((all (ly:grob-object sg 'all-elements)))
                     (if (ly:grob-array? all)
                         (for-each
                          (lambda (g)
                            (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                              (if (memq nm '(VerticalAxisGroup StaffSymbol LyricText))
                                  (format #t "PROBEVR ~a sys~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                          tag sysno nm
                                          (ly:grob-relative-coordinate g sg Y)
                                          (car (ly:grob-extent g g Y))
                                          (cdr (ly:grob-extent g g Y))
                                          (ly:grob-property g 'staff-affinity 'none)))))
                          (ly:grob-array->list all))))))))
         (ly:prob-property page 'lines))))
    pages))
probeVR =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEVR BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

mel = \relative c'' { \time 4/4 c4 c g' g | a a g2 | }
melB = \relative c'' { \time 4/4 c4 c g' g | a a g2 \break c,4 c g' g | a a g2 | }
versA = \lyricmode { Twin -- kle twin -- kle lit -- tle star }
versB = \lyricmode { How I won -- der what you are }
versC = \lyricmode { Up a -- bove the world so high }

%% One verse between two staves — CHL3's shape, restated here so every comparison in
%% this probe is intra-probe.
\book { \probeVR "VRS1"
  \score { <<
    \new Staff \new Voice = "one" \mel
    \new Lyrics \lyricsto "one" \versA
    \new Staff \mel
  >> } }

%% Two verses: the verse step becomes a run step.
\book { \probeVR "VRS2"
  \score { <<
    \new Staff \new Voice = "one" \mel
    \new Lyrics \lyricsto "one" \versA
    \new Lyrics \lyricsto "one" \versB
    \new Staff \mel
  >> } }

%% Three verses: is the verse step a constant, or does ink move it?
\book { \probeVR "VRS3"
  \score { <<
    \new Staff \new Voice = "one" \mel
    \new Lyrics \lyricsto "one" \versA
    \new Lyrics \lyricsto "one" \versB
    \new Lyrics \lyricsto "one" \versC
    \new Staff \mel
  >> } }

%% THE CARRY BOOK: two systems, verse 2 sings only on the second. System 1 must dump
%% ONE Lyrics group and read like VRS1; system 2 must dump TWO and read like VRS2.
\book { \probeVR "VRC1"
  \score { <<
    \new Staff \new Voice = "one" \melB
    \new Lyrics \lyricsto "one" { \versA \versA }
    \new Lyrics \lyricsto "one" { \repeat unfold 7 { \skip 1 } \versB }
    \new Staff \melB
  >> } }

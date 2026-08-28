\version "2.26.0"
%% LP FIDELITY PROBE — DOES A ROW STANDING ABOVE A STAFF MAKE ROOM FOR THAT STAFF'S
%% TEXT SPANNER (accel./rit.)?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe textspanner-under-row.ly -Prefix PROBETS
%%
%% WHAT THIS OPENS (2026-08-28). Reported against
%% scratch/ベースタブLy/Untitled-6.lys: `@rit` prints ON TOP OF the roman chord row above
%% its staff (same x, baselines 0.01 apart) and on the second staff it prints on top of
%% the lyric row above that one. Lily# places the spanner in the outside-staff pass
%% (OutsideStaffStacker.PlaceTextSpanners, priority 350) so it clears its OWN staff's ink,
%% and then the ROW above is spaced against a staff silhouette
%% (MultiStaffLayouter.BuildAllStaffSkylines) that the spanner is not in. The two passes
%% never meet — the same shape the volta-over-chord repair closed two acts ago, on the
%% other side of the staff.
%%
%% ⚠️ WHAT IS NOT IN DOUBT AND WHAT IS. Where the spanner sits over its own staff IS
%% measured and exact: books TSF/TSC in spanner-floors.ly pin the floor (2.85 = staff ink
%% 2.05 + TextSpanner staff-padding 0.8) and the note-column support (8.555), and the ext
%% dump there gives the drawn ink about the line as (-0.05 . 1.570859) for the "rit."
%% piece. NOTHING measures whether a LINE ABOVE the staff is spaced against that ink. That
%% is the whole question here, and it is a question about the LOOSE-LINE chain, not about
%% the spanner.
%%
%% THE PAIRS. Two rows, because they are two different specs in LilyPond and two
%% different routes in Lily#, and the report shows both:
%%   TSCR/TSCN — a ChordNames row ABOVE a staff carrying the spanner (staff-affinity DOWN,
%%             nonstaff-relatedstaff-spacing read off the ChordNames context).
%%   TSLR/TSLN — Staff / Lyrics(\lyricsto the staff above) / Staff, with the spanner on the
%%             LOWER staff: the row belongs to the staff above it and is UNRELATED to the
%%             staff below (nonstaff-unrelatedstaff-spacing). This is exactly the
%%             `staff melody / lyrics verse sings melody / staff melody` of the report.
%% In each pair the second book is the first with \startTextSpan/\stopTextSpan and the
%% bound-details override REMOVED and nothing else changed, so the difference is the
%% spanner's and only the spanner's.
%%
%% ⚠️ THE CHORDS ARE BARE TRIADS ON PURPOSE. A `maj7` here would put the reading inside
%% the chord-VOCABULARY island (LilyPond composes maj7 raised, Lily# prints it flat with a
%% j descender — the CHL family carries +0.570 of exactly that), and this pair is not about
%% the symbols. `D` and `E` print the same ink in both engines.
%%
%% THE MUSIC IS QUIET ON PURPOSE: drawn third-space c'' throughout, so the spanner rests
%% on its staff-padding FLOOR (the TSF regime) and the reading is not entangled with a
%% note-column support term that moves when the pitches move.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs and an arithmetic).
%% The spanner's ink reaches 2.85 + 1.570859 = 4.420859 above the staff's refpoint, i.e.
%% 4.420859 − 2.05 = 2.370859 ABOVE THE STAFF'S OWN INK. So:
%%   (a) If the loose line's placement is skyline-driven and the staff's top ink was
%%       already what bound it in the control, then
%%           TSCR − TSCN = TSLR − TSLN = +2.370859 exactly.
%%   (b) If the control's distance is set instead by the spring's basic-distance (the
%%       skyline slack being larger than the ink), the increase is SMALLER than 2.370859
%%       — it is whatever the skyline term now exceeds the spring by — and the two pairs
%%       need not agree, since ChordNames and Lyrics declare different basic-distances.
%%   Either way the sign is asserted: STRICTLY POSITIVE, both pairs.
%% ⚠️ FALSIFIER, and it is a real one: TSCR == TSCN (and TSLR == TSLN) to every digit would
%% mean LilyPond does NOT let a text spanner push the line above it — that the grob is
%% outside-staff for its own staff's collision pass but not in the skyline the page
%% distributes loose lines against. Lily#'s current behaviour would then be FAITHFUL and
%% the report would have to be answered somewhere else entirely (or not at all). Do not
%% port anything before reading these four numbers.
%%
%% ⚠️ THE SERIF PIN IS LOAD-BEARING for the "rit." ink, exactly as in spanner-floors.ly:
%% without it the svg backend resolves fonts.serif through this machine's fontconfig and
%% the text's extent stops being reproducible.
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).

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
                          (if (memq nm '(VerticalAxisGroup StaffSymbol TextSpanner))
                              (format #t "PROBETS ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

probeTS =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBETS BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

quiet = { c''4 c'' c'' c'' | c''4 c'' c'' c'' \bar "|." }

spanned = {
  \override TextSpanner.bound-details.left.text = \markup \italic "rit."
  c''4\startTextSpan c'' c'' c'' | c''4 c'' c'' c''\stopTextSpan \bar "|."
}

%% TSCR — a CHORD ROW above a staff whose music carries the rit. spanner.
\book {
  \probeTS "TSCR"
  \score {
    <<
      \new ChordNames \chordmode { d1 | e1 }
      \new Staff \spanned
    >>
  }
}

%% TSCN — THE CONTROL: TSCR with the spanner removed and nothing else changed.
\book {
  \probeTS "TSCN"
  \score {
    <<
      \new ChordNames \chordmode { d1 | e1 }
      \new Staff \quiet
    >>
  }
}

%% TSLR — Staff / Lyrics / Staff, the spanner on the LOWER staff, so the row above it
%%     belongs to the OTHER staff and is unrelated to the one it has to clear.
\book {
  \probeTS "TSLR"
  \score {
    <<
      \new Staff \new Voice = "mel" \quiet
      \new Lyrics \lyricsto "mel" { Twin -- kle twin -- kle lit -- tle star }
      \new Staff \spanned
    >>
  }
}

%% TSLN — THE CONTROL: TSLR with the spanner removed and nothing else changed.
\book {
  \probeTS "TSLN"
  \score {
    <<
      \new Staff \new Voice = "mel" \quiet
      \new Lyrics \lyricsto "mel" { Twin -- kle twin -- kle lit -- tle star }
      \new Staff \quiet
    >>
  }
}

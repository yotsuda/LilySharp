\version "2.26.0"
%% LP FIDELITY PROBE — WHAT A VOLTA BRACKET DOES ABOUT A CHORD SYMBOL UNDER IT.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe volta-over-chord.ly -Prefix PROBEVO
%%
%% THE DEFECT THIS OPENS (2026-08-28, session 275 — reported by the owner against a lead
%% sheet whose `@chord(Fm)' inside a first ending is drawn WITH THE BRACKET'S LINE THROUGH
%% IT). Lily# places the two independently: ChordNameEngraver.Calculate puts the symbol at
%% StaffPadding plus the note protrusion read off the system up-skyline, and
%% OutsideStaffStacker.PlaceVoltas places the bracket against trackers that were never told
%% the symbol exists. Neither reads the other, so on a system whose notes push both to the
%% same height they land on top of each other.
%%
%% WHAT LILYPOND DOES, AND WHY IT CANNOT HAPPEN THERE. A ChordName declares no
%% outside-staff-priority, so its skyline goes into inside_staff_skylines — the support
%% EVERY outside-staff grob is placed against — while VoltaBracketSpanner declares 600 and
%% is a mover:
%%   LILYPOND-REF: lily/axis-group-interface.cc:914-935 skyline_spacing — the grobs whose
%%     priority is unset are exactly the ones collected into inside_staff_skylines;
%%     :952-972 add_grobs_of_one_priority places the rest in ascending priority order.
%%   LILYPOND-REF: scm/define-grobs.scm VoltaBracketSpanner — (outside-staff-priority . 600).
%%   LILYPOND-REF: scm/define-grobs.scm ChordName and TextScript — ChordName declares none,
%%     TextScript declares 450, and 450 is also BELOW 600.
%% So the bracket steps over whatever stands under it, by its outside-staff-padding.
%% MusicMarkEngraver already ports exactly this reading for the mark family (its
%% ChordBandUp arm, and the same 0.46); the volta is the member that was left out.
%%
%% THE PAIR (HANDOFF 5.0-1), ONE VARIABLE APART: VOC carries a symbol under the first
%% ending, VOCV is the same book with the symbol deleted and NOTHING else changed. The
%% load-bearing reading is that the bracket MOVES between them; the control also says the
%% bracket's own height is right, so a residual on VOC cannot be blamed on it.
%%
%% ⚠️ THE SYMBOL IS SPELLED AS A TEXTSCRIPT, NOT AS A ChordNames CONTEXT, and that is the
%% counterpart choice (HANDOFF 5.0, the session-179 trap — a translation substitution hides
%% underneath an agreement). Lily#'s `@chord' is a symbol drawn above the staff AT A NOTE'S
%% X; LilyPond's ChordNames is a CONTEXT, i.e. a loose line of its own, and the two are
%% placed by different machinery (a loose line by the page's spacing solve, a script by the
%% outside-staff pass). inline-chord-page.ly's CIBM opened that same question for the page
%% gap; here VOCA answers it for this quantity and is NOT a ledger point: it is read only to
%% show that BOTH LilyPond constructs put the bracket ABOVE the symbol, so the port does not
%% turn on which of the two Lily#'s drawing is.
%%
%% ⚠️ THE FACE IS NOT CONTROLLED and does not need to be: both entries are measured to the
%% bracket line's OWN BOTTOM EDGE, so each engine's line thickness (LilyPond 1.6 x
%% line-thickness = 0.16, Lily# a hard 0.13 in SharedRenderer.DrawVoltaBrackets — the same
%% shadowing EngravingDefaults.TupletBracketThickness was fixed for, still open here) falls
%% out of both readings, and so does the ink height of the symbol itself (Nimbus Sans against
%% TeX Gyre Heros — ledger page.chord-row.staff-to-chord-baseline's island).
%%
%% ⚠️⚠️ THE STAFF ANCHOR IS THE MIDDLE LINE, AND IT WAS THE OTHER ONE FOR ONE COMMIT
%% (corrected 2026-08-28, same session). This probe first printed its staff reading against
%% `StaffSymbol rel + (cdr Y-extent)' — the top line's OUTER EDGE, half a staff-line
%% thickness (0.05) above the line's centre — while the Lily# side read the drawn line's
%% CENTRE. The two sides were half a rule apart and the entry carried the difference as if it
%% were engine divergence. The MIDDLE LINE is the anchor every other page entry in this
%% ledger uses, it is the StaffSymbol's own reference point, and NO thickness enters it on
%% either side. ⇒ when a reading names "the staff", say WHICH edge of WHICH line, and check
%% that the other side names the same one before trusting the number (HANDOFF 5.0 — a pair
%% has to be checked for being a pair, and this one was checked one session late).
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).
%% ⚠️ THE OCTAVE WRAPPER IS THE EXPORTER'S OWN: inside \fixed c' a written c' is Lily#'s c'
%% (HANDOFF 5.5 — a hand-converted probe gets this wrong and does not look wrong).

#(define (probe-vo tag layout pages)
   ;; Collect the three grobs this probe reads, then print the two DERIVED quantities, so
   ;; that nothing is computed by hand on the way into the ledger (HANDOFF 5.3).
   (let ((staff-mid #f) (volta-mid #f) (sym-top #f))
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
                          (let* ((nm (assq-ref (ly:grob-property g 'meta) 'name))
                                 (rel (ly:grob-relative-coordinate g sg Y))
                                 (lo (car (ly:grob-extent g g Y)))
                                 (hi (cdr (ly:grob-extent g g Y))))
                            (cond
                             ;; The StaffSymbol's OWN reference point IS the middle line —
                             ;; no extent, and so no line thickness, enters the anchor.
                             ((eq? nm 'StaffSymbol) (set! staff-mid rel))
                             ((eq? nm 'VoltaBracket) (set! volta-mid rel))
                             ((memq nm '(TextScript ChordName)) (set! sym-top (+ rel hi))))
                            (if (memq nm '(StaffSymbol VoltaBracket TextScript ChordName))
                                (format #t "PROBEVO ~a GROB ~a rel=~a ext=(~a . ~a)\n"
                                        tag nm rel lo hi))))
                        (ly:grob-array->list all)))))))
         (ly:prob-property page 'lines)))
      pages)
     ;; The bracket line's own BOTTOM EDGE: the grob's reference is the line's centre and
     ;; its extent reaches half a thickness above it, so bottom = rel - 0.08, and 0.08 is
     ;; half of 1.6 x line-thickness. The GROB lines above print the extent it comes from.
     (if (and staff-mid volta-mid)
         (format #t "PROBEVO ~a STAFFMIDDLE-TO-LINE-BOTTOM ~a\n"
                 tag (- (- volta-mid 0.08) staff-mid)))
     (if (and sym-top volta-mid)
         (format #t "PROBEVO ~a SYMBOL-INK-TOP-TO-LINE-BOTTOM ~a\n"
                 tag (- (- volta-mid 0.08) sym-top)))))

probeVO =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-bottom = ##t
               ragged-right = ##t
               indent = 0
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEVO BOOK ~a\n" tag)
                                      (probe-vo tag layout pages)) } #})

%% VOC — the symbol under the first ending. Lily#: section A { c'1 | d'1 }
%% section B { e'1 | g'2 a'4 bes'4@chord(Fm) } section C { g'1 | a'1 },
%% form main { |: A [1. ~B] :| [2. ~C }.
\book {
  \probeVO "VOC"
  \score {
    \new Staff { \fixed c' { \key f \major \time 4/4
      \repeat volta 2 { c'1 | d'1 }
      \alternative { { e'1 | g'2 a'4 bes'4^\markup "Fm" } { g'1 | a'1 } } } }
  }
}

%% VOCV — VOC with the SYMBOL taken out and nothing else changed: the control the move is
%% read against, and the reading that says the bracket's own height is right.
\book {
  \probeVO "VOCV"
  \score {
    \new Staff { \fixed c' { \key f \major \time 4/4
      \repeat volta 2 { c'1 | d'1 }
      \alternative { { e'1 | g'2 a'4 bes'4 } { g'1 | a'1 } } } }
  }
}

%% VOCF — THE FLOOR. Nothing pokes above the staff at all (whole notes, all below the top
%% line), so the outside-staff pass has nothing to clear and the reading is where the
%% side-position step alone puts the bracket: its lowest ink one `padding' above the staff's
%% own ink, and nothing else.
%%   LILYPOND-REF: scm/define-grobs.scm:4327 VoltaBracketSpanner (padding . 1) — with
%%     Y-offset = side-position-interface::y-aligned-side and no staff-padding of its own.
%% This is the third entry, and it exists because the other two CANNOT see that number: in
%% both of them the bracket is pushed clear of ink and the floor is slack. A change to the
%% floor moves this reading one for one and leaves the other two alone.
\book {
  \probeVO "VOCF"
  \score {
    \new Staff { \fixed c' { \key f \major \time 4/4
      \repeat volta 2 { c1 | d1 }
      \alternative { { e1 | g1 } { g1 | a1 } } } }
  }
}

%% VOCA — NOT A LEDGER POINT: the counterpart check (see the header). The same music with
%% the symbol spelled as LilyPond's OWN lead-sheet construct, a ChordNames context. Read
%% only to show that this construct also ends up UNDER the bracket, by a larger clearance
%% (a loose line's spring, not outside-staff-padding).
\book {
  \probeVO "VOCA"
  \score {
    <<
      \chords { \repeat volta 2 { s1 s1 }
                \alternative { { s1 s2 f4:m s4 } { s1 s1 } } }
      \new Staff { \fixed c' { \key f \major \time 4/4
        \repeat volta 2 { c'1 | d'1 }
        \alternative { { e'1 | g'2 a'4 bes'4 } { g'1 | a'1 } } } }
    >>
  }
}

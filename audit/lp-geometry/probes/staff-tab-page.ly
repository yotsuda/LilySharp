\version "2.26.0"
%% LP FIDELITY PROBE — A STAFF-PLUS-TAB SYSTEM ON THE PAGE, natural and compressed
%% (session 335, leg 5 → 336).
%%
%% WHY THIS PROBE EXISTS. The corpus reads a tab staff ALONE on a page (page-vertical.ly
%% TABL/NTL) and a tab staff under a notation staff INSIDE one system (staff.tab-pair.*),
%% but nothing reads the PAGE over a system made of one notation staff and one tab staff —
%% the frame every book in the user's bass corpus is written in (`staff X  tab X`). Express
%% Yourself (title + staff + tab) put 8 such systems on LilyPond's first page and 7 on
%% Lily#'s with identical line breaking; the sign of the page force decided it, and the page
%% force is made of exactly the springs measured here plus the title band (titled-page.ly).
%%
%% THE SPRINGS, read from the source:
%%   * inside a system, Staff → TabStaff: Align_interface takes the UPPER staff's
%%     VerticalAxisGroup.default-staff-staff-spacing — basic 9, minimum 8, padding 1
%%     (scm/define-grobs.scm, Staff's VerticalAxisGroup) — floored by the two skylines +
%%     padding. There is no StaffGrouper here (a bare << Staff TabStaff >>), so the spring
%%     is 9 / 8, compress strength 9 − 8 = 1 (lily/spring.cc:205-211
%%     set_default_compress_strength). TabStaff sets only StaffSymbol.staff-space = 1.5
%%     (ly/engraver-init.ly:1207) and inherits the rest of Staff.
%%   * between systems, tab refpoint → next staff refpoint: system-system-spacing 12 / 8 /
%%     padding 1 / stretchability 60, compress strength 4.
%%   * page head: top-system-spacing 6 / 0 / padding 1 (compress strength 6);
%%     page foot: last-bottom-spacing 1 / 0 / padding 1 / stretchability 30.
%%
%% THE MUSIC keeps every floor asleep so the readings are the SPEC's: g, a, b, (G2 A2 B2)
%% sit on and just above the bass staff's bottom line with up stems reaching no further
%% than position +3, so nothing hangs below the staff (its down-ink is the line at 2.05) and
%% nothing rises above it but the clef and meter at line start; the tab's frets are single
%% digits inside its strings. Skyline floors: 2.05 + 3.075 + 1 = 6.125 < 9 inside;
%% 3.075 + 2.05 + 1 = 6.125 < 12 between systems (the tab's outer line is 3.0 from its
%% refpoint, ink 3.0 + half a line thickness scaled by 1.5).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2):
%%   STBN (natural, ragged-bottom, 24 bars = 3 systems): first-ref 11.690551; inside
%%       9.000000; tab → next staff 12.000000; 6 staves on the page. Lily# should be exact on
%%       all four — staff.tab-pair.staff-staff-inside already closed the inside distance.
%%   STBK (justified, max-systems-per-page = 8, 120 bars = 15 systems): page 1 holds 8
%%       systems and is COMPRESSED. Natural stack = 6 + 8×9 + 7×12 + 4.075 = 166.075 against
%%       the band 157.628268, slack −8.45; total compress strength = 6 + 8×1 + 7×4 + 1 = 43
%%       (the foot's 1 is on its rod and contributes nothing once it binds, so the force is
%%       a little larger in magnitude); ONE force f ≈ −0.20 for the page, so
%%       inside ≈ 8.80, system ≈ 11.2, top ≈ 4.8, and no spring on its rod (inside > 8,
%%       system > 8). FALSIFIER, in the same dump: (9 − inside) must equal (12 − system)/4
%%       and (6 − top)/6 to six digits — one force. A page whose three readings do not agree
%%       is on a rod somewhere, and the entry is then a reading of that rod.
%%   The compressed book is the regime Express Yourself's first page is in (its force was
%%   −0.49: staff→tab 8.51 = 9 − 0.49×1, and its title line at 2.03 = 4 − 0.49×4). The
%%   natural book is its control.
%%
%% ⚠️ THE COUNT IS CARRIED (HANDOFF 5.0 trap 8): every distance here is read by index and
%% means what it should only while page 1 holds the staves the probe assumes. On STBK the
%% count is ALSO the page breaker's answer under the cap, and if the two engravers split 15
%% systems differently under max 8 the distances are of two different pages.
%%
%% ⚠️ Octaves: LilyPond `g,` is Lily# `g,,` (Lily# `c` is LilyPond `c'`). Tuning
%% bass-five-string-tuning = Lily# `tuning bass5`, five strings, so the tab has FIVE lines
%% 1.5 apart (span 6.0, refpoint = the middle line, 3.0 from either outer line). The tab is
%% default TabStaff = frets only, the frame Lily# gives a tab paired with its staff (U4).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe staff-tab-page.ly -Prefix PROBEST
%%
%% PROBEST PAPER top-margin=… paper-height=…
%% PROBEST PAGE <page> systems=<n>
%% PROBEST SYS <page> <i> y=<Y-offset> staff=(lo . hi) first-ref=<edge → staff refpoint>
%%             last-ref=<edge → tab refpoint> inside=<staff → tab> ink-bottom=<edge → lowest ink>
%% PROBEST GAP <page> <i> system=<tab refpoint of line i → staff refpoint of line i+1>

#(define (probe-dump-staff-tab layout pages)
   (let ((top (ly:output-def-lookup layout 'top-margin)))
     (format #t "PROBEST PAPER top-margin=~a paper-height=~a\n"
             top (ly:output-def-lookup layout 'paper-height))
     (let loop ((ps pages) (n 1))
       (if (pair? ps)
           (let* ((page (car ps))
                  (lines (ly:prob-property page 'lines)))
             (format #t "PROBEST PAGE ~a systems=~a\n" n (length lines))
             (let inner ((ls lines) (i 0) (prev-last #f))
               (if (pair? ls)
                   (let* ((sys (car ls))
                          (y (ly:prob-property sys 'Y-offset 0.0))
                          (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                          (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0)))
                          (first-ref (- (+ top y) (cdr staff)))
                          (last-ref (- (+ top y) (car staff))))
                     (format #t "PROBEST SYS ~a ~a y=~a staff=(~a . ~a) first-ref=~a last-ref=~a inside=~a ink-bottom=~a\n"
                             n i y (car staff) (cdr staff)
                             first-ref last-ref
                             (- last-ref first-ref)
                             (- (+ top y) (car ext)))
                     (if prev-last
                         (format #t "PROBEST GAP ~a ~a system=~a\n"
                                 n (1- i) (- first-ref prev-last)))
                     (inner (cdr ls) (1+ i) last-ref))))
             (loop (cdr ps) (1+ n)))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               indent = 0
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEST BOOK ~a\n" tag)
                                      (probe-dump-staff-tab layout pages)) } #})

cell = { g,4 a, b, a, }

%% ⚠️ THE LINES ARE FORCED, eight bars each, on BOTH sides. Measured 2026-09-05 without the
%% \break: LilyPond breaks this music 8 bars to a line and Lily# 7 (its bars are wider — the
%% T7 spacing family, HANDOFF §2), so the two engravers were paging DIFFERENT systems and no
%% vertical reading could be compared. Express Yourself itself is written with a `break`
%% after every phrase, which is why its two engravings shared one line breaking; the probe
%% does the same so the page chain is the only thing left to differ.
line = { \repeat unfold 8 \cell \break }

%% STBN — NATURAL. ragged-bottom and three systems on one page: every spring at its own
%%        length.
\book {
  \probeTag "STBN"
  \paper { ragged-bottom = ##t }
  \header { tagline = ##f }
  \score {
    <<
      \new Staff { \clef bass \repeat unfold 3 \line }
      \new TabStaff \with { stringTunings = #bass-five-string-tuning } { \repeat unfold 3 \line }
    >>
  }
}

%% STBK — COMPRESSED. LilyPond's justified default with eight systems pinned to the page,
%%        the shape of book JSK (page-vertical.ly) for this frame.
%%
%%        MEASURED 2026-09-05: LilyPond 8 + 7 with page 1 at force −0.200517 (the three
%%        springs agree to six digits — the falsifier did not fire); Lily# 7 on page 1 under
%%        the same cap, STRETCHED. So the cap does not pin the two engravers to the same page
%%        here, and the four distance readings of this book are of two different pages — the
%%        count entry is the finding, exactly as page.compressed.two-staff.staves-on-first-page
%%        once was for JSK. Book STB8 below asks the narrower question.
\book {
  \probeTag "STBK"
  \paper { max-systems-per-page = #8 }
  \header { tagline = ##f }
  \score {
    <<
      \new Staff { \clef bass \repeat unfold 15 \line }
      \new TabStaff \with { stringTunings = #bass-five-string-tuning } { \repeat unfold 15 \line }
    >>
  }
}

%% STB8 — EXACTLY EIGHT SYSTEMS (64 bars), no cap. The question is no longer which split the
%%        breaker prefers but whether eight staff-plus-tab systems FIT one page at all: their
%%        natural stack is 166.075 against a band of 157.628268, so they fit only by
%%        compressing every spring by one force (LilyPond: f ≈ −0.20, no spring on its rod).
%%        A breaker whose rods are longer than LilyPond's, or whose compress strengths
%%        differ, either turns the page (count 14 or fewer on page 1) or lands the
%%        distances off LilyPond's by the difference in force.
%%
%%        PREDICTION, written before running: LilyPond one page, 16 staves, the same three
%%        numbers as STBK page 1 (10.487447 / 8.799483 / 11.197930) because the page holds the
%%        same eight systems — a two-page 4 + 4 would cost a stretched first page
%%        (f ≈ +0.30, f² 0.09) against one compressed page (f² 0.04).
\book {
  \probeTag "STB8"
  \header { tagline = ##f }
  \score {
    <<
      \new Staff { \clef bass \repeat unfold 8 \line }
      \new TabStaff \with { stringTunings = #bass-five-string-tuning } { \repeat unfold 8 \line }
    >>
  }
}

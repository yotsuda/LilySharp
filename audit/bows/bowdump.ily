%% Every bow of a book, in one frame per staff — the LilyPond side of the bow twin net
%% (LilySharp.Tests/LpFidelity/BowTwinTests.cs; HANDOFF §2 S). Included right after \version
%% by audit/bows/regen-lp.ps1. For each page / system it prints
%%   BOWSTAFF page sys y=<staff refpoint about the system, up+> left=<staff lines' left X>
%%   HEAD page sys vy=<its staff's refpoint> x=<a notehead's left edge / a fret number's centre>
%%   BOW page sys name=<grob> vy=<its staff's refpoint> dir=<d> bl=<l break> br=<r break>
%%       cps=x0 y0 x1 y1 x2 y2 x3 y3            (control-points, SYSTEM frame, up+)
%% so the reader can put the control points in the staff's frame (x from the staff lines'
%% left end, y from the middle line) — the frame the Lily# dump (TwinBowSweep.Dump) prints too.
%% Built in session 647 (Lab sessions/p647/bows), moved here in session 659.

#(define bowdump-names '(Tie Slur PhrasingSlur LaissezVibrerTie RepeatTie))

#(define (bowdump-staff-y g sg)
   ;; the VerticalAxisGroup the grob hangs under — its staff
   (let loop ((p (ly:grob-parent g Y)))
     (cond ((not (ly:grob? p)) #f)
           ((memq 'axis-group-interface (assq-ref (ly:grob-property p 'meta) 'interfaces))
            (if (eq? (assq-ref (ly:grob-property p 'meta) 'name) 'VerticalAxisGroup)
                (ly:grob-relative-coordinate p sg Y)
                (loop (ly:grob-parent p Y))))
           (else (loop (ly:grob-parent p Y))))))

#(define (bowdump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (sg (ly:prob-property sys 'system-grob)))
                   (if (ly:grob? sg)
                       (let ((all (ly:grob-object sg 'all-elements)))
                         (if (ly:grob-array? all)
                             (for-each
                              (lambda (g)
                                (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                  (cond
                                   ((eq? nm 'StaffSymbol)
                                    (format #t "BOWSTAFF ~a ~a y=~,6f left=~,6f lines=~a\n"
                                            n i
                                            (ly:grob-relative-coordinate g sg Y)
                                            (+ (ly:grob-relative-coordinate g sg X)
                                               (car (ly:grob-extent g g X)))
                                            (length (ly:grob-property g 'line-positions
                                                                      (iota (ly:grob-property g 'line-count 5))))))
                                   ((memq nm '(NoteHead TabNoteHead))
                                    ;; a notehead by its left edge, a fret number by its centre
                                    ;; (the Lily# side draws the digits centred)
                                    (let ((vy (bowdump-staff-y g sg))
                                          (ext (ly:grob-extent g g X)))
                                      (if (and vy (ly:stencil? (ly:grob-property g 'stencil))
                                               (not (interval-empty? ext)))
                                          (format #t "HEAD ~a ~a vy=~,6f x=~,6f\n" n i vy
                                                  (+ (ly:grob-relative-coordinate g sg X)
                                                     (if (eq? nm 'NoteHead) (car ext)
                                                         (/ (+ (car ext) (cdr ext)) 2)))))))
                                   ;; a bow that prints nothing (a default TabStaff's Tie) is
                                   ;; not a bow on the page
                                   ((and (memq nm bowdump-names)
                                         (ly:stencil? (ly:grob-property g 'stencil))
                                         (not (ly:stencil-empty? (ly:grob-property g 'stencil)))
                                         (not (eq? #t (ly:grob-property g 'transparent))))
                                    (let* ((cps (ly:grob-property g 'control-points))
                                           (gx (ly:grob-relative-coordinate g sg X))
                                           (gy (ly:grob-relative-coordinate g sg Y))
                                           (vy (bowdump-staff-y g sg))
                                           ;; a LaissezVibrerTie / RepeatTie is an Item
                                           (sp (ly:spanner? g))
                                           (lb (and sp (ly:spanner-bound g LEFT)))
                                           (rb (and sp (ly:spanner-bound g RIGHT))))
                                      (format #t "BOW ~a ~a name=~a vy=~a dir=~a bl=~a br=~a cps=~{~,6f ~}\n"
                                              n i nm
                                              (if vy (format #f "~,6f" vy) "none")
                                              (ly:grob-property g 'direction)
                                              (if lb (ly:item-break-dir lb) 0) (if rb (ly:item-break-dir rb) 0)
                                              (append-map (lambda (p) (list (+ gx (car p)) (+ gy (cdr p))))
                                                          cps)))))))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

\paper {
  page-post-process = #(lambda (layout pages)
                         (format #t "\nBOWBOOK\n")
                         (bowdump-pages layout pages))
}

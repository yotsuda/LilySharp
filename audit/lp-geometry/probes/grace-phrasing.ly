\version "2.26.0"
\paper { indent = 0 ragged-right = ##t }
#(define (dump tag)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (st (ly:grob-object grob 'staff-symbol))
            (cps (ly:grob-property grob 'control-points))
            (ref (ly:grob-relative-coordinate grob sys Y))
            (sy (if (ly:grob? st) (ly:grob-relative-coordinate st sys Y) 0)))
       (format #t "\nPROBE ~a p0=~a p3=~a span=~a x0=~a\n" tag
               (- (+ ref (cdr (car cps))) sy)
               (- (+ ref (cdr (list-ref cps 3))) sy)
               (- (car (list-ref cps 3)) (car (car cps)))
               (+ (ly:grob-relative-coordinate grob sys X) (car (car cps)))))))
#(define (heads tag)
   (lambda (grob)
     (format #t "\nPROBE ~a head x=~a\n" tag (ly:grob-relative-coordinate grob (ly:grob-system grob) X))))
\score { \new Staff { \override PhrasingSlur.after-line-breaking = #(dump "PHA") \override NoteHead.after-line-breaking = #(heads "PHA")
  \time 4/4 \fixed c' { c4 \grace { d16\( } e4 f4 g4\) \bar "|." } } }
\score { \new Staff { \override PhrasingSlur.after-line-breaking = #(dump "PHB") \override NoteHead.after-line-breaking = #(heads "PHB")
  \time 4/4 \fixed c' { c4 d16\( e8. f4 g4\) \bar "|." } } }
\score { \new Staff { \override PhrasingSlur.after-line-breaking = #(dump "PHC") \override NoteHead.after-line-breaking = #(heads "PHC")
  \time 4/4 \fixed c' { c4\( e4 \grace { f16\) } g4 a4 \bar "|." } } }
\score { \new Staff { \override PhrasingSlur.after-line-breaking = #(dump "PHD") \override NoteHead.after-line-breaking = #(heads "PHD")
  \time 4/4 \fixed c' { c4 \grace { d16\( e16\) } f4 g4 a4 \bar "|." } } }

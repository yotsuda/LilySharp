\version "2.26.0"
#(use-modules (srfi srfi-1))
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each (lambda (page) (for-each (lambda (sys)
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
           (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
             (for-each (lambda (g)
               (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                 (if (memq nm '(NoteHead Accidental AccidentalCautionary))
                     (format #t "\nPROBE ~a ~a x=~a\n" tag nm (ly:grob-extent g sg X)))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "CTA") }
  \score { \new Staff { \time 4/4 \key g \major \fixed c' { c4 \grace { fis?16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "CTB") }
  \score { \new Staff { \time 4/4 \key g \major \fixed c' { c4 \grace { fis16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "CTC") }
  \score { \new Staff { \time 4/4 \key g \major \fixed c' { c4 fis?16 e8. f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "CTD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d?16 } e4 f4 g4 \bar "|." } } } }

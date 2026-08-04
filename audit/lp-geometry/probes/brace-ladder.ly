\version "2.26.0"

%% THE fetaBraces LADDER, asked of LilyPond itself.
%%
%% \left-brace (scm/define-markup-commands.scm:5072-5086) picks a brace by
%% BINARY-SEARCHING the font's real glyph Y-extents for the one nearest the
%% wanted size, and returns that glyph UNSCALED — there is no font-size fitting
%% and no correction factor. So the ladder IS the model, and this dumps it.
%%
%% System_start_delimiter::staff_brace (lily/system-start-delimiter.cc:150-160)
%% asks for `y * output_scale / point_constant` points, which \left-brace turns
%% back into `(ly:pt size) / scale` — the two cancel, so the wanted size is the
%% span in STAFF SPACES and these numbers are directly comparable to it.
%%
%% Writes brace-ladder.txt beside itself: "index  yLength  yDown  yUp  xLength".
%% A file port rather than stderr on purpose — LilyPond's own progress output
%% interleaves with stderr and truncated one line when this was first run.
%%
%% Regenerate LilySharp.Core/Svg/Layout/BraceLadderGenerated.cs from the output.

#(define-markup-command (dumpbraces layout props) ()
   (let* ((font (ly:paper-get-font layout
                  (cons '((font-encoding . fetaBraces)
                          (font-name . #f))
                        props)))
          (count (ly:otf-glyph-count font)))
     (call-with-output-file "brace-ladder.txt"
       (lambda (port)
         (format port "# glyph-count ~a\n" count)
         (do ((n 0 (1+ n))) ((>= n count))
           (let* ((g (ly:font-get-glyph font (string-append "brace" (number->string n))))
                  (ey (ly:stencil-extent g Y))
                  (ex (ly:stencil-extent g X)))
             (format port "~a ~a ~a ~a ~a\n"
                     n (interval-length ey) (car ey) (cdr ey) (interval-length ex))))))
     empty-stencil))

\markup \dumpbraces

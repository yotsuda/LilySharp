# LilyPond attribution

Lily# is an independent project. It is **not** affiliated with, endorsed by, or a
release of the LilyPond project.

Parts of Lily# are ported from LilyPond, the GNU music typesetter, which is free
software under the GNU General Public License version 3 or later. Lily# is released
under the same licence (see [LICENSE](LICENSE)). Each source file below carries the
copyright notice of the LilyPond file it was ported from, transcribed from that
file's own header; the C# is a modified translation, not a copy.

This table is generated from the `LILYPOND-REF` citations in the source and the
headers of the cited LilyPond files. Regenerate it when the ports change.

| Lily# file | ported from | copyright holders of the LilyPond file |
|---|---|---|
| `LilySharp.Core/Svg/Collector/AutoBeamCheck.cs` | `scm/auto-beam.scm` | Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Collector/BeamingPattern.cs` | `lily/beaming-pattern.cc` | Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Collector/BeamingPattern.cs` | `scm/time-signature-settings.scm` | Copyright (C) 2009--2026 Carl Sorensen <c_sorensen@byu.edu> |
| `LilySharp.Core/Svg/Collector/PartCombiner.cs` | `scm/part-combiner.scm` | Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/AccidentalPlacement.cs` | `lily/accidental-placement.cc` | Copyright (C) 2002--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/AccidentalPlacement.cs` | `lily/accidental.cc` | Copyright (C) 2001--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/AlignmentWalk.cs` | `lily/align-interface.cc` | Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/ArpeggioEngraver.cs` | `lily/arpeggio.cc` | Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/ArpeggioEngraver.cs` | `scm/define-grobs.scm` | Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/BeamScoringProblem.cs` | `lily/beam-quanting.cc` | Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/BeamSubdivision.cs` | `lily/beam.cc` | Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/Bezier.cs` | `lily/bezier.cc` | Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/BreakAlignSpacing.cs` | `scm/define-grobs.scm` | Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/BreakAlignSpacing.cs` | `lily/break-alignment-interface.cc` | Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/BreakAlignSpacing.cs` | `lily/staff-spacing.cc` | Copyright (C) 2001--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/DotConfiguration.cs` | `lily/dot-configuration.cc` | Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/DotConfiguration.cs` | `lily/dot-column.cc` | Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/GlissandoEngraver.cs` | `lily/line-spanner.cc` | Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/GlissandoEngraver.cs` | `scm/define-grobs.scm` | Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/GlissandoEngraver.cs` | `lily/spanner.cc` | Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/GlissandoEngraver.cs` | `scm/scheme-engravers.scm` | Copyright (C) 2012--2026 David Nalesnik <david.nalesnik@gmail.com>; Thomas Morley <thomasmorley65@gmail.com>; Dan Eble <nine.fierce.ballads@gmail.com>; Jonas Hahnfeld <hahnjo@hahnjo.de>; Jean Abou Samra <jean@abou-samra.fr> |
| `LilySharp.Core/Svg/Layout/HairpinEngraver.cs` | `lily/hairpin.cc` | Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/HairpinEngraver.cs` | `scm/define-grobs.scm` | Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/HorizontalSkyline.cs` | `lily/skyline.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/KnuthPlassBreaker.cs` | `lily/constrained-breaking.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/KnuthPlassBreaker.cs` | `lily/simple-spacer.cc` | Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/LooseLineSpacer.cs` | `lily/page-layout-problem.cc` | Copyright (C) 2009--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/LyricHyphen.cs` | `lily/lyric-hyphen.cc` | Copyright (C) 2003--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/LyricHyphen.cs` | `lily/extender-engraver.cc` | Copyright (C) 1999--2026 Glen Prideaux <glenprideaux@iname.com>; Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/LyricHyphen.cs` | `lily/lyric-extender.cc` | Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>; Han-Wen Nienhuys |
| `LilySharp.Core/Svg/Layout/LyricHyphen.cs` | `scm/define-grobs.scm` | Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/NoteCollision.cs` | `lily/note-collision.cc` | Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/PageBreaker.cs` | `lily/page-spacing.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/PageBreaker.cs` | `lily/include/constrained-breaking.hh` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/PageBreaker.cs` | `lily/page-breaking.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/PageBreaker.cs` | `lily/page-layout-problem.cc` | Copyright (C) 2009--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/PageBreaker.cs` | `lily/constrained-breaking.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/PageLayouter.cs` | `lily/page-layout-problem.cc` | Copyright (C) 2009--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/PageLayouter.cs` | `lily/page-breaking.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/Skyline.cs` | `lily/skyline.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/SkylineBuilding.cs` | `lily/skyline.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/SlurScoringProblem.cs` | `lily/slur-scoring.cc` | Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/SlurScoringProblem.cs` | `lily/slur-configuration.cc` | Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/SpannerBreakSubstitution.cs` | `lily/spanner.cc` | Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/SpannerBreakSubstitution.cs` | `lily/system.cc` | Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/Spring.cs` | `lily/spring.cc` | Copyright (C) 2007--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/SpringSolver.cs` | `lily/simple-spacer.cc` | Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/StemCalculator.cs` | `lily/stem.cc` | Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/TieChordOutline.cs` | `lily/tie-formatting-problem.cc` | Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/TieFormattingProblem.cs` | `lily/tie-formatting-problem.cc` | Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/TieFormattingProblem.cs` | `lily/tie-configuration.cc` | Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/TupletBracketEngraver.cs` | `lily/tuplet-bracket.cc` | Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>; Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/TupletBracketEngraver.cs` | `scm/define-grobs.scm` | Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>; Jan Nieuwenhuizen <janneke@gnu.org> |
| `LilySharp.Core/Svg/Layout/TupletBracketEngraver.cs` | `lily/tuplet-number.cc` | Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |
| `LilySharp.Core/Svg/Layout/VerticalSkyline.cs` | `lily/skyline.cc` | Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com> |
| `LilySharp.Core/Svg/Layout/VerticalSkyline.cs` | `lily/freetype.cc` | Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl> |

## Everything else

Files outside this table cite LilyPond without being ported from it: a citation
records where LilyPond decides something so that Lily#'s own code can be checked
against it. Reading a program to learn what it does is not copying it, and those
files carry no LilyPond copyright notice because they contain no LilyPond expression.

The Emmentaler music font is redistributed from LilyPond under the GPL; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

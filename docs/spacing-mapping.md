# Spacing Algorithm Mapping

## Cross-Reference Format

See [cross-reference-spec.md](cross-reference-spec.md) for detailed format specification.

### Search Commands

Find all Lilypond references in LilySharp:
```powershell
Show-TextFile LilySharp.Core/Svg/Layout/*.cs -Contains "LILYPOND-REF:"
```

Find references to specific function:
```powershell
Show-TextFile LilySharp.Core/Svg/Layout/*.cs -Pattern "LILYPOND-REF:.*Spring"
```

Find all LilySharp references in Lilypond (specific files):
```powershell
Show-TextFile C:\MyProj\lilypond-src\lily\spacing-*.cc, C:\MyProj\lilypond-src\lily\spring.cc -Contains "LILYSHARP-REF:"
```

## Implementation Reference Table

| LilySharp | Lilypond |
|-----------|----------|
| SpacingRules.cs CreateSpring() | lily/spacing-basic.cc note_spacing() |
| SpacingRules.cs CalculateDurationSpace() | lily/spacing-options.cc get_duration_space() |
| Spring.cs Length() | lily/spring.cc Spring::length() |
| SpringSolver.cs | lily/simple-spacer.cc Simple_spacer class |
| Skyline.cs | lily/skyline.cc Skyline class |
| BeamScoringProblem.cs | lily/beam-quanting.cc Beam_scoring_problem class |
| BeamEngraver.cs | lily/beam.cc Beam class |
| NoteCollision.cs | lily/note-collision.cc Note_collision_interface |
| AccidentalPlacement.cs | lily/accidental-placement.cc Accidental_placement |
| StemDirection.cs | lily/stem.cc Stem::calc_default_direction() |
| TieEngraver.cs | lily/tie-formatting-problem.cc, lily/bezier-bow.cc |
| SlurEngraver.cs | lily/slur-scoring.cc, lily/bezier-bow.cc |
| LayoutEngine.cs | lily/spacing-spanner.cc, lily/paper-column.cc |

## Current Issue

25.8px spacing issue - Spring calculations are correct (36px), problem is in LayoutEngine → SvgRenderer pipeline.

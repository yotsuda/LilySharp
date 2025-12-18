# LilyPond Algorithm Differences - TODO

## Implemented (Same as LilyPond)

1. **Duration Space Calculation** - `SpacingRules.CalculateDurationSpace()`
   - LILYPOND-REF: lily/spacing-options.cc:68-104 get_duration_space()
   - Formula: (shortest_duration_space + log2(ratio)) * increment
   - ✅ Fully implemented

2. **Spring Length** - `Spring.Length()`
   - LILYPOND-REF: lily/spring.cc:220-240 Spring::length()
   - Formula: max(min_distance, ideal_distance + force * inv_k)
   - ✅ Fully implemented (including +Inf check)

3. **Spring Solver** - `SpringSolver.Solve()`
   - LILYPOND-REF: lily/simple-spacer.cc:175-288 solve(), expand_line(), compress_line()
   - Algorithm: Analytical solution (not binary search)
   - ✅ Fully implemented

## Simplifications (Need Future Work)

4. **Spring Creation (note_spacing)** - `SpacingRules.CreateSpring()`
   - LILYPOND-REF: lily/spacing-basic.cc:107-161 note_spacing()
   - **DIFFERENCE**: LilyPond uses `fraction = delta_t / shortest_playing_duration`
   - **CURRENT**: Uses `prevDuration` directly (fraction = 1.0)
   - **IMPACT**: Correct for single-voice; may differ for multi-voice
   - **TODO**: Track shortest_playing_duration across voices

5. **Rod (Min Distance) Calculation**
   - LILYPOND-REF: lily/separation-item.cc:49-75 set_distance()
   - **DIFFERENCE**: LilyPond separates rod calculation from spring creation
   - **CURRENT**: Rod calculated inline in CreateSpring()
   - **IMPACT**: Structural difference, results should be similar
   - **TODO**: Consider separating for cleaner architecture

## Cross-Reference Files

### LilySharp -> LilyPond
- EngravingDefaults.cs -> spacing-options.cc, define-grobs.scm
- SpacingRules.cs -> spacing-options.cc, spacing-basic.cc
- Spring.cs -> spring.cc
- SpringSolver.cs -> simple-spacer.cc
- LayoutEngine.cs -> spacing-spanner.cc, paper-column.cc

### LilyPond -> LilySharp
- spacing-options.cc:69 -> SpacingRules.cs:301-326
- spring.cc:221 -> Spring.cs:82-97
- simple-spacer.cc:175 -> SpringSolver.cs:66-104
- spacing-basic.cc:109 -> SpacingRules.cs:269-304
- define-grobs.scm:810 -> EngravingDefaults.cs:94-120
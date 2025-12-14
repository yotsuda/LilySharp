# Lilypond-LilySharp Cross-Reference Comment Specification

## Purpose
Enable bidirectional grep between Lilypond source and LilySharp implementation.

## Format

### In LilySharp (C#) - Public
```
// LILYPOND-REF: <file>:<start-line>[-<end-line>] <function>
```

### In Lilypond (C++) - Private work notes
```
// LILYSHARP-REF: <file>:<start-line>[-<end-line>] <function>
```

## Rules
1. Tag must be exact: `LILYPOND-REF:` or `LILYSHARP-REF:` (with colon, no space before colon)
2. Project-relative path: `lily/spacing-options.cc`
3. Line numbers are mandatory
4. Function name is mandatory
5. One reference per line
6. Place immediately before the implementing code

## Examples

### LilySharp
```csharp
// LILYPOND-REF: lily/spacing-options.cc:58-73 get_duration_space()
public static double CalculateDurationSpace(Fraction duration)
```

### Lilypond (work notes)
```cpp
// LILYSHARP-REF: LilySharp.Core/Svg/Layout/SpacingRules.cs:294-319 CalculateDurationSpace()
Real
Spacing_options::get_duration_space (Rational d) const
```

## Search Commands

Find all Lilypond references in LilySharp:
```powershell
Show-TextFile LilySharp.Core/Svg/Layout/*.cs -Contains "LILYPOND-REF:"
```

Find all LilySharp references in Lilypond:
```powershell
Show-TextFile C:\MyProj\lilypond-src\lily\spacing-*.cc, C:\MyProj\lilypond-src\lily\spring.cc -Contains "LILYSHARP-REF:"
```

Find references to a specific Lilypond file:
```powershell
Show-TextFile LilySharp.Core/Svg/Layout/*.cs -Pattern "LILYPOND-REF:.*spacing-options"
```

Find references to a specific function:
```powershell
Show-TextFile LilySharp.Core/Svg/Layout/*.cs -Pattern "LILYPOND-REF:.*get_duration_space"
```

## Validation
Before commit, run:
```bash
# Check format consistency
grep -rn "LILYPOND-REF:" LilySharp.Core/ | grep -v "LILYPOND-REF: [a-z-]*\.cc:[0-9]"
```
Should return nothing if all references are properly formatted.

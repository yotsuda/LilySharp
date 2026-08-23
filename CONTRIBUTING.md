# Contributing to Lily#

Thanks for looking. A few things about this project are unusual, and they decide
whether a patch can be merged — please read the two short sections at the end
before writing engraving code.

## Licence

Lily# is **GPL-3.0-or-later**, and parts of the engraving engine are ported from
LilyPond and carry its authors' copyright (see
[LILYPOND-ATTRIBUTION.md](LILYPOND-ATTRIBUTION.md)). By contributing you agree that
your contribution is licensed under the same terms.

Do not paste code from a source whose licence is not GPL-compatible. That includes
code produced by a tool that reproduces training data verbatim.

## Reporting a bug

Engraving bugs are far easier to fix with a **minimal `.lys` that reproduces it**.
Please include:

- the `.lys` source, cut down as far as it still misbehaves
- what you expected and what you got (a PNG or SVG helps)
- `lysc --version`, and your OS

If the output differs from LilyPond's, say so explicitly and give the LilyPond
source you compared against — that turns a bug report into a fidelity report,
which is the more useful kind here.

## Building and testing

```bash
dotnet build LilySharp.slnx
dotnet test  LilySharp.Tests/LilySharp.Tests.csproj
```

`LilySharp.Core` is expected to build with **zero warnings**, XML documentation
included. A broken `cref` or an unclosed tag fails that bar.

Many tests are **SVG snapshots**. A change that moves the output will fail them,
and that is the point: rebaselining is a deliberate act, never a way to make a red
test green. If your change is supposed to move the page, say so in the pull request
and explain why the new picture is the correct one.

## Two rules specific to this project

These exist because Lily# is a **port**, not a re-implementation. They are what
keeps it converging on LilyPond instead of drifting into a lookalike.

### 1. Layout code comes from LilyPond's source, not from LilyPond's output

Engraving and layout are transliterated from LilyPond's `lily/*.cc` and `scm/*.scm`,
sign for sign. **Do not write a constant you read off a rendered LilyPond page.**
Measurement has two legitimate uses — finding a defect, and confirming a port — and
neither of them is authoring.

The test: can you name the LilyPond function and line the expression came from? If
yes, cite it:

```csharp
// LILYPOND-REF: lily/beam-quanting.cc:412
```

If no, the code is Lily#'s own invention and must say so with a `LILYSHARP-OWN:`
comment, so that the divergence is visible rather than disguised as a port.

### 2. Do not engineer for byte-identical output

The goal is that the *logic* matches LilyPond, not that the bytes do. Never add a
branch, an exclusion or an approximation whose only purpose is to keep the output
from moving. The test is the same one: which line of LilyPond has that branch?

Byte-identical output is a welcome *result*. It is not evidence that a port is
correct, and it is not a design constraint.

## Pull requests

- One concern per pull request.
- Say what you measured, and how. "It looks right" is not a measurement.
- If you changed engraving, include a before/after image.
- Keep `LILYPOND-REF` citations accurate — a stale line number is worse than none,
  because it invites the next reader to trust it.

## Releasing

Maintainers only, and it is a written procedure rather than a habit:
[`docs/RELEASING.md`](docs/RELEASING.md). One pushed tag builds the CLI binaries,
publishes eight platform-specific VSIXs to the Marketplace and creates the GitHub
Release — none of which can be taken back, since the Marketplace never accepts a version
number twice. The packaging mechanics it builds on are in
[`editors/vscode/DEPLOY.md`](editors/vscode/DEPLOY.md).

## Scope

Lily# is not trying to be LilyPond's front end, and its language is deliberately
not LilyPond's — backslash constructs are rejected on purpose. The long tail of
specialist notation (early music, microtonal, fretboard diagrams, clusters, ambitus)
is out of scope for now. If you are unsure whether something belongs, open an issue
before writing the patch.

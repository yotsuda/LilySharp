// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// LilySharp.Probe — the two corpus measurements that had no home.
//
// WHY IT IS IN THE SOLUTION AND NOT IN scratch\. Every session that needed these rebuilt
// them in scratch\, which is git-ignored, so the next session started from nothing and the
// session after that made the same instrument mistakes again (session 190 made two: a
// comparison whose two sides shared no book names still printed "0 moved", and a raw read
// of .lys that turned a CRLF rewrite into ten books "moving" under an unrelated poison).
// Being in the solution is what stops it rotting: it compiles on every `dotnet build`, so
// an API change breaks the build instead of being discovered by the next person who needs it.
//
// ⚠️ WHAT DOES NOT BELONG HERE: keystroke TIMING. LilySharp.Tests already owns that
// (EditKeystrokeBench, PreviewUpdateBench) and a third home for one quantity is the exact
// shape this repo keeps finding defects in (HANDOFF §2A). This tool answers only the two
// questions nothing else answers:
//
//   sweep / cmp — WHICH BOOKS OBSERVE A MECHANISM. Poison the mechanism in Core, sweep,
//                 and compare against a clean baseline. Two numbers get read, never one:
//                 did the book you care about move, and did ANYTHING move. A poison that
//                 moves nothing has not shown a fixture is blind; it has shown the poison
//                 missed. (HANDOFF RULES §5.4.)
//   census      — WHAT THE CORPUS ACTUALLY CONTAINS, per book, from the COLLECTED model
//                 rather than from grepping the source spelling.
//   alloc       — WHAT A RENDER AND A KEYSTROKE COST, in allocated bytes. Deterministic
//                 where time is not (a same-code time control swings 16% here), so it is
//                 the only instrument a before/after can actually be read off. Rebuilt in
//                 scratch\ by sessions 190 AND 191 before it was given a home. See Alloc.cs.
//   reuse       — HOW MANY TOP-LEVEL ITEMS A KEYSTROKE'S REPARSE ADOPTS from the previous
//                 tree. No correctness test observes this, so it is the only meter that
//                 catches a reuse-map "fix" whose real effect is to reuse less. See Reuse.cs.
//
// Usage (from the repo root):
//   dotnet run --project audit/LilySharp.Probe -c Release -- sweep base
//   ... apply a poison to Core, rebuild ...
//   dotnet run --project audit/LilySharp.Probe -c Release -- sweep poisoned
//   dotnet run --project audit/LilySharp.Probe -c Release -- cmp base poisoned some-fixture
//   dotnet run --project audit/LilySharp.Probe -c Release -- census [top-n]
//   dotnet run --project audit/LilySharp.Probe -c Release -- alloc <book>... [control-book]
//
// `sweep <label> [listfile]` defaults to the fixture tree; pass a file of repo-relative
// paths (e.g. from `git ls-files "*.lys"`) to widen it. Widen before calling a branch dead:
// session 190 was about to record an unreached branch as dead on 0-of-219 fixtures, and it
// is reached by 5 of the 566 tracked books.

using LilySharp.Core.Svg.Renderer;

var root = FindRoot();
var options = new SvgRenderOptions { EmbedFont = false };

if (args.Length >= 2 && args[0] == "sweep")
{
    Sweep.Run(root, args[1], options, args.Length >= 3 ? args[2] : null);
    return 0;
}
if (args.Length >= 3 && args[0] == "cmp")
{
    return Sweep.Compare(root, args[1], args[2], args.Skip(3).ToArray()) ? 0 : 1;
}
if (args.Length >= 2 && args[0] == "alloc")
{
    Alloc.Run(root, args.Skip(1).ToArray(), options);
    return 0;
}
if (args.Length >= 1 && args[0] == "pitches")
{
    return Outputs.Run(root, args.Length >= 2 && args[1].Length > 0 ? args[1] : null,
        args.Length >= 3 ? args[2] : null);
}
if (args.Length >= 2 && args[0] == "reuse")
{
    Reuse.Run(root, args.Skip(1).ToArray());
    return 0;
}
if (args.Length >= 1 && args[0] == "census")
{
    Census.Run(root, args.Length > 1 && int.TryParse(args[1], out int n) ? n : 20);
    return 0;
}

Console.Error.WriteLine("""
    LilySharp.Probe — corpus measurements (see the header of Program.cs)

      sweep <label> [listfile]   render every book and record a hash per book
      cmp <a> <b> [book...]      what moved between two sweeps; names the claimants you list
      pitches [listfile] [only]  where a book's page, MIDI and MusicXML disagree
      census [top-n]             per-book counts from the collected model
      alloc <book>...            MB allocated by a full render and by one keystroke
                                 (books under audit/lpreg; always include a control)
      reuse <book>...            top-level items ADOPTED per keystroke by the incremental
                                 reparse — the number a reuse-map change must move (or not)

    Sweeps are written to audit/probe-out/<label>.csv (git-ignored).
    """);
return 2;

static string FindRoot()
{
    var d = AppContext.BaseDirectory;
    while (d != null)
    {
        if (Directory.Exists(Path.Combine(d, "LilySharp.Core"))) return d;
        d = Path.GetDirectoryName(d);
    }
    throw new DirectoryNotFoundException(
        "repo root (no ancestor directory contains LilySharp.Core)");
}

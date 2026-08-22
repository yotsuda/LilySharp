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

using LilySharp.Core.Syntax;

/// <summary>
/// How many top-level items a keystroke's incremental reparse ADOPTS from the
/// previous tree, per keystroke.
/// </summary>
/// <remarks>
/// ⚠️ THE REUSE RATE HAS NO CORRECTNESS OBSERVER, AND THAT IS WHY THIS EXISTS. Every
/// equivalence test compares the incremental tree against a full parse, so a change that
/// makes the reuse map more cautious — reusing LESS — passes all of them while silently
/// spending the latency the map exists to save (keystroke latency is a property of the
/// product, RULES §5.6). A fix to the reuse map therefore carries a before/after of THIS
/// number, or it has only shown it stopped reusing, not that it fixed anything.
/// <para>
/// Two regimes, both deterministic:
/// <c>toggle</c> — the Alloc anchor's note re-pitched back and forth: a clean steady-state
/// edit on a diagnostic-free book, the regime the map is optimized for.
/// <c>type-in</c> — a phrase declaration typed character by character at the anchor: the
/// intermediate states carry real transient diagnostics (unclosed brace, dangling name),
/// so diagnostic-adjacency rules in the map are actually exercised.
/// </para>
/// </remarks>
internal static class Reuse
{
    private const string Snippet = "\nphrase zz { c4 d e f | g2 g | }\n";

    public static void Run(string root, string[] books)
    {
        Console.WriteLine($"{"book",-24}{"members",9}{"toggle",9}{"type-in",9}   anchor");
        foreach (var b in books)
        {
            var path = Path.Combine(root, "audit", "lpreg", b.EndsWith(".lys") ? b : b + ".lys");
            if (!File.Exists(path)) { Console.WriteLine($"{b,-24} MISSING {path}"); continue; }
            var text = File.ReadAllText(path).Replace("\r\n", "\n");

            if (Alloc.PickEdit(text) is not { } e)
            {
                Console.WriteLine($"{b,-24} (no anchor)");
                continue;
            }

            var tree = SyntaxTree.Parse(text);
            int members = tree.Root.SlotCount - 1;

            // Toggle: find→repl, repl→find, … — every keystroke edits the same spot.
            double toggleSum = 0;
            const int Toggles = 12;
            bool forward = true;
            foreach (var _ in Enumerable.Range(0, Toggles))
            {
                var (from, to) = forward ? (e.Find, e.Repl) : (e.Repl, e.Find);
                tree = tree.WithChange(
                    new TextChange(new TextSpan(e.Index, from.Length), to));
                toggleSum += tree.AdoptedTopLevelItems;
                forward = !forward;
            }

            // Type-in: insert Snippet one character at a time at the start of the
            // line holding the anchor (a top-level boundary in every lpreg book).
            int lineStart = text.LastIndexOf('\n', e.Index) + 1;
            double typeSum = 0;
            tree = SyntaxTree.Parse(text);
            for (int k = 0; k < Snippet.Length; k++)
            {
                tree = tree.WithChange(new TextChange(
                    new TextSpan(lineStart + k, 0), Snippet[k].ToString()));
                typeSum += tree.AdoptedTopLevelItems;
            }

            Console.WriteLine(
                $"{b,-24}{members,9}{toggleSum / Toggles,9:F2}{typeSum / Snippet.Length,9:F2}   {e.Find}");
        }
    }
}

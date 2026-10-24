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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys in a <c>fonts { }</c> block (owner's requests 2026-10-24) — on a <c>size</c> or a
/// <c>step</c> its number (<see cref="FontSizeEdit"/>), and on an entry's key or name: either
/// pair writes the next (Down) / previous (Up) font of the entry's key, so the fonts can be tried
/// one press at a time without knowing their names — down the list as the completion shows it,
/// as the clefs and <c>midiInstrument</c> step (owner's decision 2026-10-24, the reverse of the
/// first day's). <c>music "…"</c> walks the music fonts this machine
/// can engrave in (<see cref="MusicFonts.AvailableNames"/>: Emmentaler, the bundled SMuFL fonts,
/// then the user's); a text key (<c>bend "TeX Gyre Heros"</c>, <c>serif</c>, <c>lyrics</c> …)
/// walks the faces the editor offers for that key (the caller's list — the language server's
/// completion order — else the two bundled faces).
/// </summary>
/// <remarks>
/// Lily#'s choices: the caret anywhere from the key to its last name; on a name of a fallback
/// list (<c>music "Bravura" "Leland"</c>) that name steps, elsewhere the first. The walk stops
/// at either end, like the clefs. An entry with no name reads as the list's first (for
/// <c>music</c>, Emmentaler, the default), so Down writes the one after it. A name the list lacks
/// steps to its first font (Down) or its last (Up).
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The most fonts the status bar lists by name; a longer list is counted only.</summary>
    private const int FontsListedByName = 8;

    /// <summary>The fonts entry the caret at <paramref name="offset"/> is on — the name token
    /// under the caret, else the entry's key token — or null.</summary>
    private static SyntaxTokenNode? FontEntryAt(SyntaxTree tree, int offset)
    {
        foreach (var font in tree.GetNodes<FontDeclarationSyntax>())
            foreach (var (key, names) in FontEntries(font))
            {
                int end = names.Count > 0 ? names[^1].Span.End : key.Span.End;
                if (offset < key.Span.Start || offset > end)
                    continue;
                foreach (var name in names)
                    if (name.Span.Start <= offset && offset <= name.Span.End)
                        return name;
                return key;
            }
        return null;
    }

    /// <summary>A block's entries: each key token and its quoted names, in order.</summary>
    private static List<(SyntaxTokenNode Key, List<SyntaxTokenNode> Names)> FontEntries(FontDeclarationSyntax font)
    {
        var result = new List<(SyntaxTokenNode, List<SyntaxTokenNode>)>();
        var entries = font.Entries;
        for (int e = 0; e < entries.Count; e++)
        {
            var key = entries[e].KeyToken;
            int next = e + 1 < entries.Count ? entries[e + 1].KeyToken.Span.Start : int.MaxValue;
            var names = new List<SyntaxTokenNode>();
            for (int i = 0; i < font.SlotCount; i++)
                if (font.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } t
                    && t.Span.Start > key.Span.Start && t.Span.Start < next)
                    names.Add(t);
            result.Add((key, names));
        }
        return result;
    }

    /// <summary>One step of a fonts entry: the edit (null when it cannot move) and the status-bar
    /// message. <paramref name="textFaces"/> gives a text key's faces in the editor's order.</summary>
    private static (Edit? Edit, string Message) FontEdit(SyntaxTokenNode at, int dir,
        Func<string, IReadOnlyList<string>>? textFaces)
    {
        SyntaxTokenNode? key = at.Kind == SyntaxKind.StringLiteral ? null : at, name = key == null ? at : null;
        if (at.Parent is FontDeclarationSyntax font)
            foreach (var (k, names) in FontEntries(font))
                if (key != null ? k.Span.Start == key.Span.Start : names.Contains(at))
                {
                    key = k;
                    name ??= names.Count > 0 ? names[0] : null;
                }
        if (key == null)
            return (null, "fonts: write the entry's key first (music \"Bravura\")");

        string word = key.Text;
        IReadOnlyList<string> available = TextRoles.IsMusicKey(word) ? MusicFonts.AvailableNames()
            : textFaces?.Invoke(word) is { Count: > 0 } offered ? offered
            : [TextFontMetrics.SerifFamily, TextFontMetrics.SansFamily];
        string list = available.Count <= FontsListedByName ? $" ({string.Join(", ", available)})" : "";
        string current = name != null ? StringLiteral.Value(name.Text) : available[0];
        int index = IndexOfName(available, current);
        int toward = -dir;   // +1: down the list
        int to = index >= 0 ? index + toward : toward > 0 ? 0 : available.Count - 1;
        if (to < 0 || to >= available.Count)
            return (null, $"{word} \"{current}\": already the {(toward > 0 ? "last" : "first")} of {available.Count}{list}");
        string next = available[to];
        string said = $"{word} \"{next}\": {to + 1} of {available.Count}{list}";
        if (name == null)
            return (new Edit(key.Span.End, key.Span.End, $" \"{next}\""), said);
        return (new Edit(name.Span.Start, name.Span.End, $"\"{next}\""), said);
    }

    /// <summary>A <c>size N</c> / <c>step ±N</c> in a <c>fonts { }</c> block the caret is on, from the
    /// word to its number: the word and its operand tokens (the sign, the number), or null.</summary>
    private static (SyntaxTokenNode Word, List<SyntaxTokenNode> Operand)? FontSizeAt(SyntaxTree tree, int offset)
    {
        foreach (var font in tree.GetNodes<FontDeclarationSyntax>())
            for (int i = 0; i < font.SlotCount; i++)
            {
                if (font.GetChild(i) is not SyntaxTokenNode { Text: "size" or "step" } word)
                    continue;
                var operand = new List<SyntaxTokenNode>();
                for (int j = i + 1; j < font.SlotCount && font.GetChild(j) is SyntaxTokenNode t
                     && (t.Kind is SyntaxKind.IntegerLiteral or SyntaxKind.DecimalLiteral
                         || (operand.Count == 0 && t.Kind is SyntaxKind.Plus or SyntaxKind.Minus)); j++)
                    operand.Add(t);
                int end = operand.Count > 0 ? operand[^1].Span.End : word.Span.End;
                if (word.Span.Start <= offset && offset <= end)
                    return (word, operand);
            }
        return null;
    }

    /// <summary>
    /// One step of a <c>size</c> / <c>step</c> (owner's request 2026-10-24): Up is larger. A
    /// <c>size</c> moves by a tenth of a staff space on the semitone pair and by one on the octave
    /// pair, 0.5 to 20; a <c>step</c> by one font-size step on either, −12 to +12, written signed
    /// (<c>+2</c>, <c>-1</c>, <c>0</c>). Only the number changes.
    /// </summary>
    private static (Edit? Edit, string Message) FontSizeEdit(SyntaxTokenNode word, List<SyntaxTokenNode> operand,
        int dir, bool semitone)
    {
        bool isStep = word.Text == "step";
        string written = string.Concat(operand.Select(t => t.Text));
        if (operand.Count == 0 || !double.TryParse(written, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
            return (null, isStep ? "step: write its number first (lyrics step +1)" : "size: write its number first (lyrics size 2.2)");
        double by = isStep || !semitone ? 1 : 0.1;
        double next = Math.Round(value + dir * by, 2);
        // The reader's bounds, so the keys never write a size it refuses.
        double low = isStep ? -Semantics.FontPlanReader.MaxStep : Semantics.FontPlanReader.MinSize;
        double high = isStep ? Semantics.FontPlanReader.MaxStep : Semantics.FontPlanReader.MaxSize;
        if (next < low || next > high)
            return (null, $"{word.Text} {written}: already the {(dir > 0 ? "largest" : "smallest")} ({Format(low, isStep)} to {Format(high, isStep)})");
        string spelled = Format(next, isStep);
        return (new Edit(operand[0].Span.Start, operand[^1].Span.End, spelled),
            isStep ? $"step {spelled}: font-size steps (6 double the size)" : $"size {spelled}: staff spaces");

        static string Format(double v, bool signed)
        {
            string s = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return signed && v > 0 ? "+" + s : s;
        }
    }

    private static int IndexOfName(IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}

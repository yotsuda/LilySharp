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

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// An information note at each member of a <c>&lt;…&gt;</c> chord that is not a tone of the
/// chord symbol written on it (<c>&lt;c' e' f'&gt;4@chord(C)</c>: F), naming the chord the
/// notes spell when the recognizer names one (<see cref="DiagnosticCodes.ChordToneOutsideSymbol"/>).
/// </summary>
/// <remarks>
/// Owner's decisions 2026-10-27: the chord carries the name, so the name says what it sounds —
/// a member outside it is likely a slip (of the name, of a note, of a scan's reader); but a
/// voicing may hold a tension or a suspension the name leaves out on purpose, so INFORMATION,
/// not a warning. Only a chord's own <c>@chord</c>: a melody note's is not checked (its notes
/// are passing and neighbour tones as often as chord tones), nor a degree chord, a shape chord,
/// a Roman degree or free text. A tone the name has and the chord leaves out is not reported —
/// a voicing leaves out the fifth, the root, the third. As <see cref="ChordShapes.Mismatch"/>
/// does for a written shape (LYS1039), the name's tones are its registered tone set, the slash
/// bass among them.
/// </remarks>
internal sealed class ChordToneValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var mark in tree.GetRoot().KindSites(SyntaxKind.MusicMark).OfType<MusicMarkSyntax>())
        {
            if (mark.Parent is not ChordSyntax { IsShapeChord: false } chord
                || chord.Degrees.Any() || chord.DrumNames.Any()
                || ChordAnnotation.Of(mark) is not { Symbol: { } symbol, Structure: { } structure }
                || !ChordVoicings.TryTones(structure, out int allowed, out _, out _))
                continue;
            var members = chord.Pitches.Select(p => (Pitch: p,
                Step: RelativeOctave.StepIndex(p.PitchName.ToLowerInvariant()[0]), Alter: p.AccidentalOffset)).ToList();
            var foreign = members.Where(m => (allowed & (1 << PitchClass(m.Step, m.Alter))) == 0).ToList();
            if (foreign.Count == 0)
                continue;
            string? named = ChordStructure.TryRecognize(members.Select(m => (m.Step, m.Alter)).ToList(), out var recognized)
                && recognized != null ? ChordShapes.SourceSymbol(recognized) : null;
            foreach (var m in foreign)
            {
                string note = ChordStructure.SpellPitch(m.Step, m.Alter);
                _diagnostics.Info(m.Pitch.Span, DiagnosticCodes.ChordToneOutsideSymbol,
                    named != null && named != symbol
                        ? $"{note} is not a tone of {symbol}: the chord's notes spell {named} — write @chord({named}), or keep {symbol} if {note} is meant as a tension."
                        : $"{note} is not a tone of {symbol} — check the note or the name, or keep it if {note} is meant as a tension.");
            }
        }
    }

    private static int PitchClass(int step, int alter)
        => (((RelativeOctave.StepSemitoneOf(step) + alter) % 12) + 12) % 12;
}

/// <summary>What the LSP's quick fix for <see cref="DiagnosticCodes.ChordToneOutsideSymbol"/> reads.</summary>
public static class ChordTones
{
    /// <summary>The chord name an LYS1054 message proposes (<c>write @chord(Csus4)</c>), or null.</summary>
    public static string? ProposedName(string message)
    {
        var m = Regex.Match(message, @"write @chord\(([^()\s]+)\)");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Where the chord symbol is written on the chord holding <paramref name="member"/> —
    /// word 1 of its <c>@chord(…)</c> — or null.</summary>
    public static TextSpan? SymbolSpan(SyntaxTree tree, TextSpan member)
    {
        var chord = tree.GetRoot().DescendantNodes().OfType<ChordSyntax>()
            .LastOrDefault(c => c.Span.Start <= member.Start && c.Span.End >= member.End);
        var mark = chord?.ChildNodes().OfType<MusicMarkSyntax>()
            .FirstOrDefault(m => ChordAnnotation.Of(m) is { Symbol: not null });
        if (mark == null || ChordAnnotation.WordSpans(mark) is not { Count: > 0 } words)
            return null;
        return new TextSpan(words[0].Start, words[0].End - words[0].Start);
    }
}

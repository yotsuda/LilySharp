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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports two voices of a section — parts, or a part and a chords row — that write DIFFERENT
/// repeat signs on the same bar line (<see cref="DiagnosticCodes.RepeatSignsDisagree"/>).
/// </summary>
/// <remarks>
/// <para>
/// A repeat sign written in a section's music is the SCORE's: every part follows it, whichever
/// part writes it (docs/inline-repeat-design.md §3.2). So a part that writes a plain <c>|</c>
/// where another writes <c>|:</c> is the ordinary spelling, and says nothing here — the sign is
/// written once. Two parts that write two different signs there (<c>|:</c> and <c>:|</c>, or
/// <c>:|*2</c> and <c>:|*3</c>) leave no reading, and that is an ERROR (owner's decision
/// 2026-11-14: "the fix is plain to the writer"): each sign past the first is reported, naming
/// the voice that wrote the first.
/// </para>
/// <para>
/// A bar line's place is how many bars of its section stand before it, counted as each voice's
/// own readers count them: a part's by <see cref="MeasureModel.Split"/> (the written lines it
/// hands back), a row's by <see cref="ChordRowBarLines"/>. A sign the count cannot place — one
/// inside a voice span's later voice — is left out rather than guessed at.
/// </para>
/// <para>
/// LILYSHARP-OWN: the language's rule about its own signs. Departs from: nothing in LilyPond
/// asks (a <c>\repeat</c> is written around the music of each voice). Goes away: never on its
/// own. Observed by: <c>RepeatSignAgreementValidatorTests</c>.
/// </para>
/// </remarks>
internal sealed class RepeatSignAgreementValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>A repeat sign a voice writes: the bars of its section before it, the sign as
    /// written (with its <c>*N</c>), and who wrote it.</summary>
    internal readonly record struct Sign(string Section, int BarsBefore, string Text, BarlineSyntax Node, string Voice);

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        // No repeat sign in the music: nothing to compare, and no voice is split for it.
        if (!root.DescendantNodesOfKinds(SyntaxKind.Barline).Any(n => n is BarlineSyntax b
                && SyntaxFacts.IsRepeatBarlineKind(b.BarToken.Kind) && !b.IsInside<FormDeclarationSyntax>()))
            return;

        foreach (var place in Signs(root).GroupBy(s => (s.Section, s.BarsBefore)))
        {
            var first = place.First();
            foreach (var sign in place.Skip(1))
            {
                if (sign.Text == first.Text || sign.Voice == first.Voice)
                    continue;
                _diagnostics.Error(sign.Node.BarToken.Span, DiagnosticCodes.RepeatSignsDisagree,
                    $"'{sign.Text}' here and '{first.Text}' in {first.Voice} stand on the same bar line of "
                    + $"section '{sign.Section}' (after its bar {sign.BarsBefore}). A repeat is the whole "
                    + "score's, so the parts that write a sign there write the same one: make the two "
                    + "agree, or write a plain '|' in one of them - it follows the other's sign.");
            }
        }
    }

    /// <summary>Every repeat sign the sections' voices write, in document order.</summary>
    internal static List<Sign> Signs(SyntaxNode root)
    {
        var signs = new List<Sign>();
        var phrases = SectionBarCounts.PhraseBodies(root);
        foreach (var voice in SectionBarCounts.SemanticVoices(root, phrases))
        {
            if (voice.IsChords)
            {
                int bars = 0;
                bool holds = false;
                var items = voice.Container is ChordPartBlockSyntax block
                    ? block.Items : ChordNameCollector.SectionItems((SectionDeclarationSyntax)voice.Container);
                foreach (var item in items)
                {
                    if (item is BarlineSyntax line)
                    {
                        if (ChordRowBarLines.Closes(line, holds))
                            bars++;
                        holds = false;
                        Add(line, bars);
                    }
                    else if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
                        holds = true;
                }
                continue;
            }

            var written = new List<(int End, int Bars)>();
            bool any = false;
            foreach (var node in voice.Container.DescendantNodesOfKinds(SyntaxKind.Barline))
            {
                if (node is not BarlineSyntax line || !SyntaxFacts.IsRepeatBarlineKind(line.BarToken.Kind))
                    continue;
                if (!any)
                {
                    MeasureModel.Split(voice.Container, phrases, longestVoice: true, writtenLines: written);
                    any = true;
                }
                int end = line.Span.Start + line.Span.Length;
                foreach (var (lineEnd, bars) in written)
                    if (lineEnd == end)
                    {
                        Add(line, bars);
                        break;
                    }
            }

            void Add(BarlineSyntax line, int barsBefore)
            {
                if (SyntaxFacts.IsRepeatBarlineKind(line.BarToken.Kind))
                    signs.Add(new Sign(voice.SectionName, barsBefore,
                        line.BarToken.Text + (line.HasExplicitRepeatCount ? "*" + line.RepeatCount : ""), line, voice.Label));
            }
        }
        return signs;
    }
}
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
/// repeat signs on the same bar line (<see cref="DiagnosticCodes.RepeatSignsDisagree"/>) — bar
/// lines, endings, or navigation marks.
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
        // No sign in the music: nothing to compare, and no voice is split for it.
        if (!RepeatStream.InTheMusic(root))
            return;
        ReportEndingsAndMarks(root);

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

    /// <summary>
    /// The same rule for the endings and the navigation marks (session 1251): two voices that
    /// each write an ending opening on one bar line write the same passes over the same bars,
    /// and two that each write marks on one bar line write the same marks. A voice that writes
    /// none there follows the one that does, as with the bar lines.
    /// </summary>
    /// <remarks>Until then the first voice's ending stood and the other's numbers were dropped
    /// unsaid (<c>RepeatStream.AddTheMusics</c>), and two parts' different jumps on one line
    /// were both on the column, the route taking the first.</remarks>
    private void ReportEndingsAndMarks(SyntaxNode root)
    {
        var phrases = SectionBarCounts.PhraseBodies(root);
        var voices = SectionBarCounts.SemanticVoices(root, phrases);
        var label = new Dictionary<SyntaxNode, string>(ReferenceEqualityComparer.Instance);
        foreach (var voice in voices)
            label.TryAdd(voice.Container, voice.Label);
        string Who(SyntaxNode container) => label.GetValueOrDefault(container, "another part");

        foreach (string section in voices.Select(v => v.SectionName).Distinct(StringComparer.Ordinal))
        {
            var written = SectionSigns.Of(section, voices, phrases);

            foreach (var place in written.Endings.GroupBy(e => e.From))
            {
                var first = place.First();
                foreach (var ending in place.Skip(1))
                {
                    if (ReferenceEquals(ending.Container, first.Container))
                        continue;
                    var ink = new TextSpan(ending.Node.Span.Start, Math.Min(ending.Node.Span.Length, 1 + ending.Node.VoltaText.Length));
                    if (!ending.Node.Numbers.SequenceEqual(first.Node.Numbers))
                        _diagnostics.Error(ink, DiagnosticCodes.RepeatSignsDisagree,
                            $"'[{ending.Node.VoltaText}' here and '[{first.Node.VoltaText}' in {Who(first.Container)} open on the same bar line of "
                            + $"section '{section}' (after its bar {ending.From}). An ending is the whole score's, so the parts "
                            + "that write one there write the same passes: make the two agree, or leave the ending out of "
                            + "one of them - it follows the other's.");
                    else if (ending.To != first.To)
                        _diagnostics.Error(ink, DiagnosticCodes.RepeatSignsDisagree,
                            $"The ending '[{ending.Node.VoltaText}' runs {Bars(ending.To - ending.From)} here and {Bars(first.To - first.From)} in "
                            + $"{Who(first.Container)} (section '{section}', after its bar {ending.From}). An ending is the whole score's, so "
                            + "the parts that write it end it on the same bar line: make the two agree, or leave the ending out "
                            + "of one of them - it follows the other's.");
                }
            }

            foreach (var place in written.Marks.GroupBy(m => m.Bars))
            {
                var byVoice = place.GroupBy(m => m.Container, ReferenceEqualityComparer.Instance).ToList();
                var first = byVoice[0].ToList();
                foreach (var other in byVoice.Skip(1).Select(g => g.ToList()))
                {
                    if (other.Select(m => m.Node.MarkType).SequenceEqual(first.Select(m => m.Node.MarkType)))
                        continue;
                    _diagnostics.Error(other[0].Node.Span, DiagnosticCodes.RepeatSignsDisagree,
                        $"{Marks(other)} here and {Marks(first)} in {Who(first[0].Container)} stand on the same bar line of "
                        + $"section '{section}' (after its bar {place.Key}). A navigation mark is the whole score's, so the "
                        + "parts that write one there write the same marks: make the two agree, or leave the mark out of "
                        + "one of them - it follows the other's.");
                }
            }
        }

        static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";
        static string Marks(List<SectionSigns.Mark> marks)
            => string.Join(" ", marks.Select(m => "'" + string.Join(' ',
                Enumerable.Range(0, m.Node.SlotCount).Select(i => m.Node.GetChild(i)).OfType<SyntaxTokenNode>().Select(t => t.Text)) + "'"));
    }

    /// <summary>Every repeat sign the sections' voices write, in document order.</summary>
    internal static List<Sign> Signs(SyntaxNode root)
    {
        var signs = new List<Sign>();
        var phrases = SectionBarCounts.PhraseBodies(root);
        foreach (var voice in SectionBarCounts.SemanticVoices(root, phrases))
            foreach (var line in VoiceBarLines.Of(voice, phrases))
                if (SyntaxFacts.IsRepeatBarlineKind(line.Node.BarToken.Kind))
                    signs.Add(new Sign(voice.SectionName, line.Bars,
                        line.Node.BarToken.Text + (line.Node.HasExplicitRepeatCount ? "*" + line.Node.RepeatCount : ""),
                        line.Node, voice.Label));
        return signs;
    }
}

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

namespace LilySharp.Core.LilyPond;

// Niente: LilyPond has no dynamic that ends a hairpin in silence — it draws a circle at a
// hairpin's tip when the grob's circled-tip is set, and the hairpin ends with \! — so the twin
// asks the page which hairpins it paired with a niente.
public sealed partial class LilyPondExporter
{
    /// <summary>
    /// Reads off the page which hairpins touch a <c>@niente</c> (HairpinEngraver.DetectHairpins
    /// — the pairing the page draws by): the marks whose tip is circled, the nientes that END a
    /// hairpin, and the nientes a circle stands for. <see cref="EmitDynamic"/> writes them.
    /// </summary>
    /// <remarks>
    /// By written source position: a mark played twice is one written mark, and pairs the same
    /// way in every play unless a section boundary cuts it — then the union is written.
    /// </remarks>
    private void CollectNiente(SyntaxTree tree, RenderDeclarationSyntax? render)
    {
        if (render == null)
            return;
        if (!tree.GetRoot().DescendantNodes<DynamicSyntax>().Any(d => d.Level == DynamicLevel.Niente))
            return;
        if (PageModel(tree, render) is not { } score)
            return;
        foreach (var h in Svg.Layout.HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics,
                     Svg.Collector.SectionPlays.For(score)))
        {
            if (h.CircledTip)
            {
                _shared.CircledHairpinMarks.Add(h.SourcePosition);
                _shared.CircledNientes.Add(h.CircledNiente!.SourcePosition);
            }
            if (h.NienteAtEnd is { } end)
                _shared.NienteEnds.Add(end.SourcePosition);
        }
    }

    /// <summary>
    /// A <c>@niente</c> as the twin writes it: <c>\!</c> where it ends a hairpin; nothing more
    /// where a circled tip stands for it; else the word, in italic, as the page prints it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/declarations-init.ly:86 — "\\!" = make-span-event 'CrescendoEvent STOP,
    ///   which ends the hairpin on its note's column (lily/dynamic-engraver.cc:246-249).
    /// LILYPOND-REF: scm/define-markup-commands.scm:4237-4251 define-markup-command (italic …).
    ///   The word is a TextScript, not a DynamicText, as the page's is (DynamicEngraver: a
    ///   niente is placed and drawn as free expressive text).
    /// </remarks>
    private string EmitNiente(DynamicSyntax d)
    {
        int at = d.SourceStart;
        string end = _shared.NienteEnds.Contains(at) ? "\\!" : "";
        if (_shared.CircledNientes.Contains(at))
            return end;
        string word = (d.ForcedAbove switch { true => "^", false => "_", null => "-" })
            + "\\markup { \\italic \"niente\" }";
        return end.Length > 0 ? end + " " + word : word;
    }

    /// <summary>The circled tip of a hairpin the page pairs with a niente at its thin end,
    /// as a tweak on the hairpin's own start event: <c>-\tweak circled-tip ##t \&gt;</c>.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:153 Hairpin::print reads circled_tip (the circled-tip of scm/define-grob-properties.scm);
    ///   Documentation/snippets/printing-hairpins-using-al-niente-notation.ly.
    /// </remarks>
    private string CircledTipTweak(DynamicSyntax d)
        => _shared.CircledHairpinMarks.Contains(d.SourceStart) ? "-\\tweak circled-tip ##t " : "";
}

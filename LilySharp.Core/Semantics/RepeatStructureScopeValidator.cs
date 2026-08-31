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

using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Keeps the constructs that change a book's PLAYING ORDER inside its <c>form { … }</c>:
/// a repeat barline (<c>|:</c> <c>:|</c> <c>:|:</c>) and a volta ending (<c>[1. … ]</c>)
/// written in music are refused (<see cref="DiagnosticCodes.RepeatStructureOutsideForm"/>).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ ONE PREDICATE, NOT N PARSER ARMS, and that is the load-bearing choice. The four shapes
/// the tree's books spread this across — inside a phrase, inside a <c>chords</c> row, inside a
/// part-major section, inside a section-major part block — all reach the same two node types,
/// so asking the TREE catches them together. Editing the parser instead means enumerating the
/// arms, and the session that measured this rule's reach miscounted that very quantity three
/// times in one hour (HANDOFF §2 F ⒫).
/// </para>
/// <para>
/// ⚠️ THE ISLAND, COUNTED BY SINK rather than by source text (RULES §5.0). Every
/// <see cref="BarlineSyntax"/> that can stand in a parsed tree comes from four sites —
/// <c>Parser.Music.cs</c>'s music item, <c>Parser.Sections.cs</c>'s chord-row item, and the two
/// form arms (a stray <c>:|</c>, and <c>ParseFormBarline</c>'s "every other barline") — and
/// every <see cref="InlineVoltaSyntax"/> from one, <c>ParseInlineVolta</c>. The form arms are
/// why the ancestor test is needed at all: a form's own <c>|: … :|</c> block keeps its bars as
/// RAW TOKENS inside <c>FormRepeatBlockGreen</c> and makes no barline node, but a <c>:|</c> or
/// <c>:|:</c> standing loose in a form body does make one.
/// </para>
/// <para>
/// ⚠️ TWO SPELLINGS THIS MUST NOT CATCH, and neither needs an exclusion: a LYRIC verse header
/// <c>[1. … ]</c> is a <c>LyricVoltaSyntax</c> (the words for the Nth pass — not a repeat), and
/// a lyric row's barline stays a raw token inside <c>LyricMeasureGreen</c>, so it is not a
/// <see cref="BarlineSyntax"/> at all. Both are invisible to a rule written on node types;
/// they were NOT invisible to the text-matching counters that preceded it, which is how two of
/// those three miscounts happened. <c>repeat percent</c> / <c>repeat unfold</c> /
/// <c>tremolo</c> are likewise untouched — they are their own node, and they abbreviate notes
/// rather than reorder them.
/// </para>
/// <para>
/// ⚠️ Only nodes reached from <c>tree.GetRoot()</c> are asked. Two places build these node
/// types by hand with a NULL parent — <c>LilyPondExporter.CreateEnding</c> / <c>CreateBarline</c>
/// (rebuilding a form ending as an inline one so <c>EmitInlineRepeat</c> stays the single place
/// that writes <c>\alternative</c>) and <c>MeasureCollector.Form.CreateBarlineSyntax</c> (the
/// bars a form's repeat composes into the music stream). Those are how a LEGAL form repeat is
/// rendered, so a predicate applied to them would accuse every book that has one; walking from
/// the root is what keeps this rule about what the author WROTE.
/// </para>
/// </remarks>
internal sealed class RepeatStructureScopeValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            switch (node)
            {
                case BarlineSyntax bar
                    when SyntaxFacts.IsRepeatBarlineKind(bar.BarToken.Kind)
                         && !bar.IsInside<FormDeclarationSyntax>():
                    // At the bar token, not the node: `:|*3` carries its count on the same
                    // node, and the count is not what is wrong with it.
                    _diagnostics.Error(bar.BarToken.Span,
                        DiagnosticCodes.RepeatStructureOutsideForm,
                        $"'{bar.BarToken.Text}' is a repeat, so it belongs in the form, not in "
                        + "the music: a repeat changes the ORDER the music plays in, and a "
                        + "form is where a book's order is written. Cut the repeated bars "
                        + "into a section of their own and repeat the SECTION - "
                        + "'section A { … }' with 'form main { |: A :| }'.");
                    break;

                case InlineVoltaSyntax volta when !volta.IsInside<FormDeclarationSyntax>():
                    // At the '[' alone. An ending's body can run for bars, and squiggling all
                    // of it buries the one token to delete (the rule CueRegionValidator
                    // reports a nested cue by).
                    _diagnostics.Error(volta.OpenBracket.Span,
                        DiagnosticCodes.RepeatStructureOutsideForm,
                        $"a volta ending ('[{volta.VoltaText} … ]') belongs in the form, not "
                        + "in the music: an ending changes the ORDER the music plays in, and a "
                        + "form is where a book's order is written. Cut each ending into a "
                        + "section of its own and name it in the form - "
                        + "'form main { |: A [1. B] :| [2. C] }'. (A LYRIC verse keeps this "
                        + "spelling: '[1. … ]' in a 'lyrics' row is the words for the first "
                        + "pass, and stays where it is.)");
                    break;
            }
        }
    }
}

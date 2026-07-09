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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Collector;

// Structure expansion for MeasureCollector: unfolding structure/section/repeat
// blocks and music containers into the flat music-node stream, expanding phrase
// variable references, and the synthetic-barline helper. Split out of
// MeasureCollector.cs as a partial class; same instance state, no behavior change.
public sealed partial class MeasureCollector
{
    private void ProcessRepeatBlock(StructureRepeatBlockSyntax repeat, Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
    {
        bool afterRepeatStart = false;
        var pendingVoltaBrackets = new List<(int startMeasure, int endMeasure, string voltaText, bool isClosed, int sourcePosition)>();

        for (int i = 0; i < repeat.SlotCount; i++)
        {
            var child = repeat.GetChild(i);

            if (child is SyntaxTokenNode token)
            {
                if (token.Text == "|:")
                {
                    processNodes(new[] { CreateBarlineSyntax(token.Text, token.Position) });
                    afterRepeatStart = true;
                }
                else if (token.Text == ":|")
                {
                    processNodes(new[] { CreateBarlineSyntax(token.Text, token.Position) });
                }
                else if (token.Text == ":|:")
                {
                    // Back-to-back repeat divider: close the current repeat and open
                    // the next. The adjacent ':|' + '|:' fuse into the RepeatBoth
                    // glyph at render time, and the following section is still marked
                    // as a repeat (StartBarline = RepeatStart) — exactly ':| |:'.
                    processNodes(new[] { CreateBarlineSyntax(":|", token.Position) });
                    processNodes(new[] { CreateBarlineSyntax("|:", token.Position) });
                    afterRepeatStart = true;
                }
            }
            else if (afterRepeatStart)
            {
                if (child is SectionReferenceSyntax reference)
                {
                    if (_sections.TryGetValue(reference.SectionName, out var section))
                    {
                        RecordSectionStart(reference.SectionName, builder.CurrentMeasureIndex);
                        builder.SectionLabel = ResolveSectionLabel(reference);
                        builder.SectionLabelPosition = SectionDeclPos(reference.SectionName);
                        ProcessSection(section, processNodes);
                    }
                }
                else if (child is { Kind: SyntaxKind.SilentSectionReference } silent
                         && silent.GetChild(1) is SyntaxTokenNode silentName
                         && _sections.TryGetValue(silentName.Text, out var silentSection))
                {
                    // ~Name inside a repeat: render the section's music but show NO
                    // label. The top-level silent-reference case skips in-repeat nodes
                    // (IsInsideRepeatBlock), so without this the section's measures
                    // were dropped entirely, not just its label.
                    RecordSectionStart(silentName.Text, builder.CurrentMeasureIndex);
                    builder.SectionLabel = null;
                    builder.SectionLabelPosition = SectionDeclPos(silentName.Text);
                    ProcessSection(silentSection, processNodes);
                }
                else if (child is StructureAlternativeSyntax alt)
                {
                    string altSectionName = alt.SectionName.Text;
                    if (_sections.TryGetValue(altSectionName, out var section))
                    {
                        // Track measure index before processing this alternative
                        int startMeasureIndex = builder.CurrentMeasureIndex;
                        RecordSectionStart(altSectionName, startMeasureIndex);

                        builder.SectionLabel = alt.DisplayLabel ?? altSectionName;
                        builder.SectionLabelPosition = SectionDeclPos(altSectionName);
                        ProcessSection(section, processNodes);

                        // Track measure index after processing
                        int endMeasureIndex = builder.CurrentMeasureIndex;
                        // If we're mid-measure, include that measure
                        if (builder.CurrentItemCount > 0)
                            endMeasureIndex++;

                        // Collect volta bracket info if bracket style
                        // endMeasureIndex is exclusive (one-past-end); convert to inclusive
                        // for VoltaBracketItem which stores the last measure index
                        if (alt.HasBracket && !alt.IsSilent)
                        {
                            int lastMeasure = Math.Max(startMeasureIndex, endMeasureIndex - 1);
                            pendingVoltaBrackets.Add((startMeasureIndex, lastMeasure, alt.VoltaText, alt.IsClosed, alt.Position));
                        }
                    }
                }
            }
        }

        // Each ending's right cap follows its source ']' (present = closed); the
        // engraver's segment splitter opens only line-break pieces of a closed one.
        foreach (var (startMeasure, endMeasure, voltaText, isClosed, sourcePosition) in pendingVoltaBrackets)
            _voltaBrackets.Add(new VoltaBracketItem(startMeasure, endMeasure, voltaText, isClosed, sourcePosition));
    }

    private void ProcessSection(SectionDeclarationSyntax section, Action<IEnumerable<SyntaxNode>> processNodes)
    {
        // Reset the relative frame (and revert the octave mode to the file
        // default) at each section boundary.
        _octave.ResetForSection();

        bool matched = false;
        foreach (var child in section.DescendantNodes())
        {
            if (child is PartBlockSyntax partBlock)
            {
                if (_voiceName == null || partBlock.Name == _voiceName)
                {
                    ProcessMusicContainer(partBlock, processNodes);
                    matched = true;

                    if (_voiceName != null) return;
                }
            }
        }

        // Part-major fallback: this section's music for the current voice is not a
        // part-block here but lives inside `part <voice> { section <name> { ... } }`.
        if (!matched && _voiceName != null
            && _partMajorCells.TryGetValue((section.SectionName, _voiceName), out var cell))
        {
            ProcessMusicContainer(cell, processNodes);
        }
    }

    /// <summary>
    /// Process the music inside a container node — a <c>part-block</c> (section-major)
    /// or a part-major inner <c>section</c>. Both expose their music as descendants.
    /// </summary>
    private void ProcessMusicContainer(SyntaxNode container, Action<IEnumerable<SyntaxNode>> processNodes)
    {
        // Collect all music nodes, expanding variable references
        var musicNodes = new List<SyntaxNode>();

        foreach (var node in container.DescendantNodes())
        {
            // Skip nodes inside containers (tuplet/repeat/grace/inline volta/
            // parallel) — they'll be processed by those handlers. Inline voltas
            // in particular must pass through as ONE wrapper node, or the
            // bracket ([1. ]/[2.]) is lost while its notes leak out flat. A
            // << \\ >> span likewise passes through as one node.
            if (IsInsideProcessedContainer(node))
                continue;

            if (node is VariableReferenceSyntax varRef)
                ExpandVariable(varRef.Name.Text, musicNodes);
            else if (IsCollectableMusicNode(node))
                musicNodes.Add(node);
        }

        processNodes(musicNodes);
    }

    private void ExpandVariable(string name, List<SyntaxNode> musicNodes)
    {
        if (!_variables.TryGetValue(name, out var expression))
            return;

        // Each phrase reference evaluates its body in a FRESH relative frame
        // (default octave / pitch / duration): a phrase's pitches must not
        // depend on what happened to be played before the reference, or the
        // same $phrase would render differently at every call site. This is
        // the moral equivalent of LilyPond variables carrying their own
        // \relative block. State flows OUT of the phrase normally, so a note
        // following $phrase is relative to the phrase's last note.
        musicNodes.Add(RelativeResetMarker.Instance);

        // Include the expression itself if it is a music node.
        if (IsCollectableMusicNode(expression))
            musicNodes.Add(expression);

        // Get music nodes from the variable expression descendants; container
        // expressions travel as ONE wrapper node each (inner content skipped).
        var nodes = expression.DescendantNodes()
            .Where(n => !IsInsideProcessedContainer(n) && IsCollectableMusicNode(n));

        musicNodes.AddRange(nodes);
    }

    private static BarlineSyntax CreateBarlineSyntax(string barText, int position)
    {
        var kind = barText switch
        {
            "|:" => SyntaxKind.RepeatStartBar,
            ":|" => SyntaxKind.RepeatEndBar,
            "||" => SyntaxKind.DoubleBar,
            "|." => SyntaxKind.FinalBar,
            _ => SyntaxKind.Bar
        };

        var token = new LilySharp.Core.Syntax.InternalSyntax.SyntaxToken(kind, barText);
        var green = new LilySharp.Core.Syntax.InternalSyntax.BarlineGreen(token);
        return new BarlineSyntax(green, null, position);
    }
}

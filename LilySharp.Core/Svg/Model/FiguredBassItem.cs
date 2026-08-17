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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A single figure in a figured bass group (e.g., "6", "4♯").
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc - BassFigureEvent
/// LILYPOND-REF: scm/define-grob-interfaces.scm - bass-figure-interface
/// </remarks>
public readonly record struct FiguredBassFigure(
    // The figure number (1-9), or 0 for an empty figure placeholder.
    int Number,
    // Alteration: 0=none, 1=sharp, -1=flat, 2=natural (cautionary).
    // LILYPOND-REF: lily/figured-bass-engraver.cc:120 alteration property
    int Alteration = 0,
    // A held / continuation figure ('_' in @fig): the figure sustains from the
    // previous bass note and is drawn as a horizontal extension dash. Continuo.
    bool Held = false
)
{
    /// <summary>
    /// Gets the display text for this figure (number + optional accidental,
    /// or an extension dash for a held figure).
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (Held)
                return "–";  // en dash: continuo extension / held figure

            string numStr = Number > 0 ? Number.ToString() : "";
            string altStr = Alteration switch
            {
                1 => "\u266F",   // ♯
                -1 => "\u266D",  // ♭
                2 => "\u266E",   // ♮
                _ => ""
            };
            return numStr + altStr;
        }
    }
}

/// <summary>
/// A group of figured bass figures attached to a bass note.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc - Figure_group struct
/// LILYPOND-REF: scm/define-grobs.scm:362-380 - BassFigure grob defaults
///
/// Figured bass appears as stacked numbers below bass notes, indicating
/// chord inversions and alterations. Common figures:
/// - 6 = first inversion
/// - 6/4 = second inversion (6 and 4 stacked)
/// - 7 = seventh chord
/// - 6/4/3 = third inversion of seventh
///
/// Syntax in LilySharp: @fig.6 (single), @fig.6.4 (two figures),
/// @fig.6.s (with sharp), @fig.4.f (with flat), @fig.7.n (with natural)
/// </remarks>
public sealed record FiguredBassItem
{
    /// <summary>The figures in this group, ordered top to bottom.</summary>
    public ImmutableArray<FiguredBassFigure> Figures { get; }

    /// <summary>Measure index containing this figured bass.</summary>
    public int MeasureIndex { get; }

    /// <summary>Item index of the bass note within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; init; }

    /// <summary>Global staff index this figured bass belongs to (multi-staff
    /// routing; see <c>DynamicItem.StaffIndex</c>). 0 for single-staff.</summary>
    public int StaffIndex { get; }

    /// <summary>Creates a figured bass group attached to a bass note.</summary>
    public FiguredBassItem(
        ImmutableArray<FiguredBassFigure> figures,
        int measureIndex,
        int itemIndex,
        int sourcePosition,
        int staffIndex = 0)
    {
        Figures = figures;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
    }

    /// <summary>
    /// Parses the argument of a <c>@fig(…)</c> annotation into its stacked figures, or
    /// returns null when those tokens spell no figure group Lily# can draw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written <c>@fig(6)</c> (single), <c>@fig(5 3)</c> (two stacked), <c>@fig(6 s)</c>
    /// (6♯; <c>f</c> flat, <c>n</c> natural), <c>@fig(#)</c> (a bare sharp — the raised
    /// third), <c>@fig(_)</c> (a held / continuation figure). A figure is a digit 0-9,
    /// where 0 is the empty placeholder.
    /// </para>
    /// <para>
    /// ★ A SUB-LANGUAGE, so it parses TOKENS (VALUE_SITE_AUDIT §9.2, §9.5.3 ⑴). It used
    /// to take the dotted <see cref="LilySharp.Core.Syntax.MusicMarkSyntax.MarkName"/> and split it back on
    /// '.', which is the string round trip <c>@chord</c> and <c>@mark</c> have already
    /// left; it was the last argument still being read out of that name. Tokens are the
    /// right unit and not merely a tidier one: the spelling is whitespace-INSENSITIVE
    /// because the token boundary is the separator, so <c>@fig(6#6)</c> means the same as
    /// <c>@fig(6 # 6)</c> and <c>@fig(6_)</c> the same as <c>@fig(6 _)</c> — measured, and
    /// unchanged here. Reading the argument RUNS instead would have needed a second copy
    /// of the lexer to recover those boundaries (<c>6s6</c> splits because <c>s</c> is a
    /// pitch token, while <c>6S0</c> does not), which is the defect §5.2.1② names.
    /// </para>
    /// <para>
    /// ⚠️ <b>A behaviour change, declared and chosen</b> — the same one <c>@chord</c>'s
    /// swallowed dot was: MarkName DROPS a '.' written inside the brackets, so
    /// <c>@fig(6.4)</c> printed 6 over 4, and <c>@fig(.6)</c>, <c>@fig(6.)</c> and
    /// <c>@fig(6.s)</c> all printed as though the dot had not been typed. A dot is a
    /// token here, so those now name no figure and are reported as unknown annotations.
    /// Measured over 3,825 generated spellings: 3,326 read identically, and every one of
    /// the 499 that differ contains a written '.', all in the direction "was accepted,
    /// now unknown" — nothing that was refused became accepted, and nothing that was
    /// accepted draws different figures. No book writes a dot inside <c>@fig(</c>: all
    /// 308 .lys on disk contain 13 <c>@fig(</c> sites spelling only <c>6</c>, <c>7</c>,
    /// <c>5 3</c> and <c>6 4</c>, and the MusicXML importer writes the spaced forms
    /// (<see cref="MusicXmlImport.LysWriter"/>). The net is
    /// <c>FiguredBassTests.ADotWrittenInsideTheParentheses_…</c>.
    /// </para>
    /// <para>
    /// LILYSHARP-OWN: the <c>@fig(…)</c> SPELLING is Lily#'s, not a port — LilyPond writes
    /// figures in its own <c>\figuremode</c> language. What the figures MEAN is LilyPond's
    /// and is cited on <see cref="FiguredBassFigure"/> and on this type. Nothing observes
    /// the spelling but this reader and the importer that writes it.
    /// </para>
    /// </remarks>
    public static ImmutableArray<FiguredBassFigure>? ParseFigures(
        ImmutableArray<Syntax.SyntaxTokenNode> argumentTokens)
    {
        if (argumentTokens.IsDefaultOrEmpty)
            return null;

        var figures = ImmutableArray.CreateBuilder<FiguredBassFigure>();
        int currentNumber = -1;
        int currentAlteration = 0;
        int pendingAlteration = 0; // a leading '#' seen BEFORE its figure number

        foreach (var token in argumentTokens)
        {
            // ',' separates arguments and says nothing about the figures, exactly as it
            // said nothing to MarkName (which left it out of the dotted string).
            if (token.Kind == Syntax.SyntaxKind.Comma)
                continue;

            var part = token.Text;
            if (string.IsNullOrEmpty(part))
                return null;

            if (int.TryParse(part, out int number) && number >= 0 && number <= 9)
            {
                // Flush previous figure if any
                if (currentNumber >= 0)
                    figures.Add(new FiguredBassFigure(currentNumber, currentAlteration));

                currentNumber = number;
                currentAlteration = pendingAlteration; // a leading '#6' binds here
                pendingAlteration = 0;
            }
            else if (part == "#")
            {
                // '#' is the jazz / continuo sharp. Written before a number ('#6')
                // it binds to the coming figure; alone ('#') it is a bare sharp.
                // (Suffix sharp keeps the existing 's' spelling: '6.s'.)
                pendingAlteration = 1;
            }
            else if (part == "_")
            {
                // '_' is a held / continuation figure line (continuo): its own
                // stacked slot that sustains from the previous bass note. Flush any
                // current figure, then add the held slot.
                if (currentNumber >= 0)
                    figures.Add(new FiguredBassFigure(currentNumber, currentAlteration));
                figures.Add(new FiguredBassFigure(0, 0, Held: true));
                currentNumber = -1;
                currentAlteration = 0;
                pendingAlteration = 0;
            }
            else if (currentNumber >= 0 && part.Length == 1)
            {
                // Alteration suffix for the current figure
                currentAlteration = part[0] switch
                {
                    's' or 'S' => 1,   // sharp
                    'f' or 'F' => -1,  // flat
                    'n' or 'N' => 2,   // natural
                    _ => 0
                };
                if (currentAlteration == 0)
                    return null; // Invalid alteration
            }
            else
            {
                return null; // Invalid part
            }
        }

        // Flush last figure
        if (currentNumber >= 0)
            figures.Add(new FiguredBassFigure(currentNumber, currentAlteration));

        // A lone '#' with no figure at all is a standalone sharp (raised third).
        if (pendingAlteration != 0 && currentNumber < 0)
            figures.Add(new FiguredBassFigure(0, pendingAlteration));

        return figures.Count > 0 ? figures.ToImmutable() : null;
    }
}

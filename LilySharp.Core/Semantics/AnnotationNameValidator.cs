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

using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns about <c>@name</c> annotations the collector does not recognize.
/// Unknown names previously fell through <c>CollectArticulations</c> silently,
/// so a typo like <c>@glisando</c> simply produced no output. This validator
/// mirrors every name the collector consumes; when adding a new annotation,
/// add its name here too (the all-samples sweep test pins the sync).
/// </summary>
public sealed class AnnotationNameValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>
    /// Plain <c>@name</c> annotations consumed outside ArticulationRegistry and
    /// MusicMarkItem.ParseMarkName (trill spanners, courtesy accidentals,
    /// glissando, cue notes, cross-staff, arpeggio, l.v./repeat ties).
    /// </summary>
    private static readonly HashSet<string> ExtraPlainNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "starttrillspan", "stoptrillspan",
            "courtesy", "editorial",
            "gliss", "glissando",
            "cue",
            "cross",
            "arpeggio",
            "laissezvibrer", "repeattie",
            "dead",
            "stemup", "stemdown",
            "ho", "hammeron", "po", "pulloff", "tap", "snappizz", "slide", "stopped",
            "thumb", "heel", "toe", "scoop", "plop",
        };

    /// <summary>
    /// Mark names accepted by <see cref="MusicMarkItem.ParseMarkName"/> plus the
    /// compound families — used only to power "did you mean" suggestions
    /// (validity itself is checked against the real parsers).
    /// </summary>
    private static readonly string[] SuggestionCandidates =
    [
        "staccato", "accent", "tenuto", "marcato", "fermata", "portato",
        "staccatissimo", "upbow", "downbow", "harmonic", "flageolet",
        "sfz", "sf", "fp", "rf", "rfz", "fz", "sffz",
        "pppp", "ppppp", "ffff", "fffff",
        "trill", "mordent", "prall", "turn", "invertedturn", "pralltriller",
        "starttrillspan", "stoptrillspan", "courtesy", "editorial", "glissando",
        "cue", "cross", "arpeggio", "laissezvibrer", "repeattie",
        "segno", "coda", "fine", "rit", "accel", "cresc", "decresc", "dim",
        "ottava", "ottava.bassa", "loco", "ped", "ped.off",
        "sost", "sost.off", "una.corda", "tre.corde", "to.coda",
        "mark.A", "finger.1", "feather.right", "feather.left",
        "notehead.x", "notehead.diamond", "notehead.slash",
        "trillspan.start", "trillspan.stop", "fig.6", "chord.C",
    ];

    /// <summary>
    /// Validates all <c>@name</c> annotations in a syntax tree.
    /// </summary>
    public void Validate(SyntaxTree tree) => Walk(tree.GetRoot());

    private void Walk(SyntaxNode node)
    {
        switch (node)
        {
            case ArticulationSyntax art:
            {
                var name = art.NameToken.Text;
                if (!IsKnownPlainName(name))
                    WarnUnknown(art, name);
                break;
            }
            case MusicMarkSyntax mark:
            {
                var name = mark.MarkName;
                if (!IsKnownCompoundName(name))
                    WarnUnknown(mark, name);
                break;
            }
        }

        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null)
                Walk(child);
        }
    }

    /// <summary>
    /// True iff a plain (dot-free) annotation name is consumed somewhere in the
    /// collector: an articulation/ornament, a music mark, or one of the named
    /// feature annotations.
    /// </summary>
    public static bool IsKnownPlainName(string name) =>
        ArticulationRegistry.IsKnown(name)
        || MusicMarkItem.ParseMarkName(name) != null
        || ExtraPlainNames.Contains(name);

    /// <summary>
    /// True iff a compound (dotted) annotation name is consumed somewhere:
    /// a music mark (incl. <c>mark.*</c> rehearsal marks), a trill-spanner
    /// event, fingering, feathered beams, figured bass, or a chord name.
    /// </summary>
    public static bool IsKnownCompoundName(string name)
    {
        if (MusicMarkItem.ParseMarkName(name) != null)
            return true;

        var lower = name.ToLowerInvariant();
        if (lower is "trillspan.start" or "trillspan.stop")
            return true;
        // @text("…") — free expressive text (the compound name carries the
        // string payload, e.g. text."dolce" or text."dolce".up).
        if (lower.StartsWith("text.", StringComparison.Ordinal))
            return true;
        if (lower is "feather.right" or "feather.left" or "feather.accel" or "feather.rit")
            return true;
        if (lower.StartsWith("finger.", StringComparison.Ordinal)
            && int.TryParse(lower.AsSpan(7), out int finger) && finger >= 0)
            return true;
        if (lower is "notehead.x" or "notehead.cross" or "notehead.diamond"
            or "notehead.triangle" or "notehead.slash" or "notehead.xcircle")
            return true;
        if (lower.StartsWith("frame.", StringComparison.Ordinal)
            && lower.Length is >= 10 and <= 14
            && lower.AsSpan(6).ToString().All(ch => ch is 'x' or 'o' or (>= '0' and <= '9')))
            return true;
        if (lower is "pluck.p" or "pluck.i" or "pluck.m" or "pluck.a")
            return true;
        if (lower is "bend.half" or "bend.full"
            || (lower.StartsWith("bend.", StringComparison.Ordinal)
                && int.TryParse(lower.AsSpan(5), out int bend) && bend is > 0 and <= 12))
            return true;
        if (FiguredBassItem.ParseFigures(name) != null)
            return true;
        if (ChordNameItem.ParseChordName(name) != null)
            return true;

        return false;
    }

    private void WarnUnknown(SyntaxNode node, string name)
    {
        var message = $"Unknown annotation '@{name}' — it is ignored.";
        var suggestion = FindSuggestion(name);
        if (suggestion != null)
            message += $" Did you mean '@{suggestion}'?";

        _diagnostics.Warning(
            new TextSpan(node.Position, node.FullWidth),
            DiagnosticCodes.UnknownAnnotation,
            message);
    }

    /// <summary>
    /// Returns the closest known name within edit distance 2 (typo range), or
    /// null when nothing is close enough to suggest.
    /// </summary>
    private static string? FindSuggestion(string name)
    {
        string? best = null;
        int bestDistance = 3; // accept distance 1..2 only

        foreach (var candidate in SuggestionCandidates)
        {
            int d = Levenshtein(name.ToLowerInvariant(), candidate.ToLowerInvariant(), bestDistance);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Bounded Levenshtein distance (returns <paramref name="cap"/> when exceeding it).</summary>
    private static int Levenshtein(string a, string b, int cap)
    {
        if (Math.Abs(a.Length - b.Length) >= cap)
            return cap;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            int rowMin = curr[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, curr[j]);
            }
            if (rowMin >= cap)
                return cap;
            (prev, curr) = (curr, prev);
        }
        return Math.Min(prev[b.Length], cap);
    }
}

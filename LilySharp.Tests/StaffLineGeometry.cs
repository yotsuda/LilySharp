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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LilySharp.Tests;

/// <summary>
/// Reads the staves back out of a rendered page, by their five drawn lines.
/// </summary>
/// <remarks>
/// A staff line is the one horizontal stroke that spans the system, so this instrument needs
/// no X model at all: consecutive lines one staff space apart are one staff, and any larger
/// step starts the next. Everything is in staff spaces, page-DOWN — the SVG's own frame.
/// <para>
/// ⚠️ ONE COPY, TWO CALLERS. It was written inside <see cref="SystemGapStaffFrameTests"/> and
/// moved here the moment a second file wanted it (HANDOFF 5.2.1②): two spellings of one
/// instrument is how two tests come to disagree about what they measured.
/// </para>
/// </remarks>
internal static class StaffLineGeometry
{
    // The elements this instrument reads, with the groups around them: the preview's SVG
    // draws each system in its own frame — a <g transform="translate(0,Y) …"> around it
    // (IDocumentContext.SystemLocalFrames) — so a line's or a text's y is page-down only
    // after the translates of the groups it sits in are added. Static export has no such
    // groups and reads as before.
    private static readonly Regex Token = new(
        "<g\\b[^>]*>|</g>|<line [^>]*>|<text[^>]*>[^<]*</text>", RegexOptions.Compiled);
    private static readonly Regex TranslateY = new(
        "transform=\"translate\\(-?[0-9.]+,(-?[0-9.]+)\\)", RegexOptions.Compiled);

    /// <summary>Every line and text element of the page with the Y translation of the
    /// groups enclosing it, in document order.</summary>
    private static IEnumerable<(string Element, double Dy)> Elements(string svg)
    {
        var frames = new Stack<double>();
        frames.Push(0);
        foreach (Match t in Token.Matches(svg))
        {
            string s = t.Value;
            if (s.StartsWith("</g", System.StringComparison.Ordinal))
            {
                if (frames.Count > 1) frames.Pop();
                continue;
            }
            if (s.StartsWith("<g", System.StringComparison.Ordinal))
            {
                double dy = frames.Peek();
                var tm = TranslateY.Match(s);
                if (tm.Success)
                    dy += double.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture);
                frames.Push(dy);
                continue;
            }
            yield return (s, frames.Peek());
        }
    }

    private static readonly Regex Line = new(
        "<line x1=\"(-?[0-9.]+)\" y1=\"(-?[0-9.]+)\" x2=\"(-?[0-9.]+)\" y2=\"(-?[0-9.]+)\"",
        RegexOptions.Compiled);
    private static readonly Regex Text = new(
        "<text[^>]*\\sy=\"(-?[0-9.]+)\"[^>]*>([^<]+)</text>", RegexOptions.Compiled);

    /// <summary>Every staff's (top line, bottom line), in page order.</summary>
    internal static List<(double Top, double Bottom)> Staves(string svg)
    {
        var ys = Elements(svg)
            .Select(e => (Match: Line.Match(e.Element), e.Dy))
            .Where(e => e.Match.Success)
            .Select(e => (
                X1: double.Parse(e.Match.Groups[1].Value, CultureInfo.InvariantCulture),
                Y1: double.Parse(e.Match.Groups[2].Value, CultureInfo.InvariantCulture) + e.Dy,
                X2: double.Parse(e.Match.Groups[3].Value, CultureInfo.InvariantCulture),
                Y2: double.Parse(e.Match.Groups[4].Value, CultureInfo.InvariantCulture) + e.Dy))
            .Where(l => l.Y1 == l.Y2 && l.X2 - l.X1 > 20)
            .Select(l => l.Y1)
            .Distinct().OrderBy(y => y).ToList();

        var staves = new List<(double, double)>();
        int start = 0;
        for (int i = 1; i <= ys.Count; i++)
        {
            if (i < ys.Count && System.Math.Abs(ys[i] - ys[i - 1] - 1.0) < 1e-6) continue;
            staves.Add((ys[start], ys[i - 1]));
            start = i;
        }
        return staves;
    }

    /// <summary>
    /// The distinct text baselines lying strictly between two Y bounds, in page order — the
    /// syllable rows of one system, when the bounds are its two staves.
    /// </summary>
    /// <remarks>
    /// A baseline, not a box: an SVG <c>text</c> element's <c>y</c> IS the baseline, which is
    /// the reference point LilyPond spaces a Lyrics line by (ly/engraver-init.ly:648-658), so
    /// no font metric enters this reading and it is comparable across platforms.
    /// </remarks>
    internal static List<double> Baselines(string svg, double above, double below)
        => Elements(svg)
            .Select(e => (Match: Text.Match(e.Element), e.Dy))
            .Where(e => e.Match.Success && Regex.IsMatch(e.Match.Groups[2].Value, "[A-Za-z]"))
            .Select(e => double.Parse(e.Match.Groups[1].Value, CultureInfo.InvariantCulture) + e.Dy)
            .Where(y => y > above && y < below)
            .Distinct().OrderBy(y => y).ToList();

    /// <summary>
    /// The gap between consecutive staves, in page order: within a system it is the
    /// staff-to-staff room, between systems it is the inter-system one.
    /// </summary>
    internal static List<double> Gaps(string svg)
    {
        var s = Staves(svg);
        return Enumerable.Range(1, s.Count - 1).Select(i => s[i].Top - s[i - 1].Bottom).ToList();
    }
}

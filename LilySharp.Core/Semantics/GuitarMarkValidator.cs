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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>Which parts are a fretted instrument's, asked of the syntax: where a mark means the
/// guitarist's thing (<c>@vibrato</c> the wavy line, 2026-11-13) and not the singer's.</summary>
public static class FrettedParts
{
    /// <summary>Whether <paramref name="node"/> is written in the music of a part whose
    /// instrument is fretted — a guitar, a bass, a ukulele, a banjo, a mandolin: one whose header
    /// names a tab tuning or a preset that has one (<see cref="PartHeaderDefaults.FrettedTuning"/>,
    /// what the chord diagrams and the tab read). False outside every part (a phrase's body).</summary>
    public static bool Holds(SyntaxNode node)
    {
        SyntaxNode root = node;
        while (root.Parent != null)
            root = root.Parent;
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is PartDeclarationSyntax declared)
                return PartHeaderDefaults.Read(declared).FrettedTuning != null;
            if (p is PartBlockSyntax block)
                return ConcertPitch.FindPart(root, block.Name) is { } part && PartHeaderDefaults.Read(part).FrettedTuning != null;
        }
        return false;
    }
}

/// <summary>
/// <c>@release</c>: a guitar bend let back (owner-approved 2026-11-13, grammar evaluation 3).
/// Written on the bent note itself, <c>a'4@bend(full)@release</c>, the string is bent up and let
/// back within the note; on the note after a bent one, <c>a'4@bend(full) a'4@release</c>, it is
/// let back to that note. Read on the syntax, in the voice line the mark is written in
/// (<see cref="SpanNames.Line"/>): "the note before" is the note or chord written before it
/// there, and a rest between them breaks the pair.
/// </summary>
/// <remarks>LILYSHARP-OWN, as the bend-up is (LilyPond's BendSpanner, <c>\^</c> with
/// <c>\bendHold</c>, is not ported: it bends BETWEEN two written pitches, where <c>@bend(full)</c>
/// is one note and an amount). The picture follows LilyPond's all the same: up with the amount,
/// then a curve down to an arrow head (MEASURED against 2.26.0, featF/lp/tab.ly, by eye).</remarks>
public static class BendReleases
{
    /// <summary>The mark's name.</summary>
    public const string Name = "release";

    /// <summary>What <paramref name="release"/> lets back: the bend on its own note, the bend on
    /// the note before, or — <see cref="Svg.Model.BendRelease.None"/> — nothing (an error).</summary>
    public static Svg.Model.BendRelease Of(ArticulationSyntax release) => Of(release, out _);

    /// <summary><see cref="Of(ArticulationSyntax)"/>, and the semitones of the bend let back (0 for none).</summary>
    public static Svg.Model.BendRelease Of(ArticulationSyntax release, out int semitones)
    {
        semitones = 0;
        if (release.Name != Name || HostOf(release) is not { } host)
            return Svg.Model.BendRelease.None;
        if (BendOf(host) is { } own)
        {
            semitones = own;
            return Svg.Model.BendRelease.WithinTheNote;
        }
        var line = SpanNames.Line(release);
        SyntaxNode? before = null;
        foreach (var d in line.DescendantNodes())
        {
            if (d.Span.Start >= host.Span.Start)
                break;
            if (d is NoteSyntax or ChordSyntax or RestSyntax or ChordRepetitionSyntax or BareDurationSyntax or SlashNoteSyntax
                && d.Parent is not ChordSyntax && ReferenceEquals(SpanNames.Line(d), line))
                before = d;
        }
        if (before is not (NoteSyntax or ChordSyntax) || BendOf(before) is not { } earlier)
            return Svg.Model.BendRelease.None;
        semitones = earlier;
        return Svg.Model.BendRelease.ToTheNote;
    }

    /// <summary>Whether a <c>@bend(…)</c> is written on <paramref name="item"/> (a note, a chord).</summary>
    public static bool IsBent(SyntaxNode item)
        => item.DescendantNodes().OfType<MusicMarkSyntax>().Any(m => AnnotationValues.Bend(m) != null);

    /// <summary>The semitones of the bend <paramref name="item"/> carries, or null.</summary>
    public static int? BendOf(SyntaxNode item)
        => item.DescendantNodes().OfType<MusicMarkSyntax>().Select(AnnotationValues.Bend).FirstOrDefault(b => b != null);

    private static SyntaxNode? HostOf(SyntaxNode mark)
        => mark.Parent is { Parent: ChordSyntax chord } ? chord : mark.Parent;
}

/// <summary>
/// What the guitar marks of grammar evaluation 3 (owner-approved 2026-11-13) can be told where
/// they are written: a <c>@barre</c> that names no barre (<see cref="DiagnosticCodes.BarreMalformed"/>),
/// a fretted instrument's <c>@vibrato</c> written with a singer's numbers
/// (<see cref="DiagnosticCodes.VibratoNumbersOnAnInstrument"/>), a <c>@release</c> with no
/// bend before it (<see cref="DiagnosticCodes.ReleaseWithoutBend"/>).
/// Each mark's reader is <see cref="AnnotationValues"/>'s; this says why a reading failed, in
/// place of "unknown annotation", which sends the writer to check a name spelled right.
/// </summary>
internal sealed class GuitarMarkValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds))
            switch (node)
            {
                case ArticulationSyntax { Name: MusicMarkItem.BarreName } bare:
                    BarreNamesNothing(bare, "it is written with no fret");
                    break;
                case MusicMarkSyntax { Name: MusicMarkItem.BarreName, IsSpanEnd: false } barre
                    when AnnotationValues.Barre(barre) is null:
                    BarreNamesNothing(barre, WhyNoBarre(barre));
                    break;
                case ArticulationSyntax { Name: BendReleases.Name } release
                    when BendReleases.Of(release) == BendRelease.None:
                    _diagnostics.Error(release.Span, DiagnosticCodes.ReleaseWithoutBend,
                        "this '@release' lets back no bend - there is no '@bend(…)' on its own note and none on the note "
                        + "before it in this voice - so nothing is drawn. Write it on the bent note for a bend up and back "
                        + "within the note (a'4@bend(full)@release), or on the note right after the bent one for a release "
                        + "to that note (a'4@bend(full) a'4@release).");
                    break;
                // '@vibrato(60)' on a guitar: the numbers are a singing voice's depth and rate,
                // and a fretted instrument's vibrato is the wavy line, which takes none.
                case MusicMarkSyntax harmonic when AnnotationValues.IsFretHarmonic(harmonic):
                    NaturalHarmonic(harmonic);
                    break;
                case MusicMarkSyntax { Name: "vibrato", IsSpanEnd: false, HasArgumentList: true } vibrato
                    when FrettedParts.Holds(vibrato):
                    _diagnostics.Error(vibrato.Span, DiagnosticCodes.VibratoNumbersOnAnInstrument,
                        "'@vibrato' on a fretted instrument's note is the wavy line over the note and takes no "
                        + "numbers - write '@vibrato' alone (c4@vibrato). The numbers are a sung note's, how far and "
                        + "how fast a singing voice swings (@vibrato(60, 6)); nothing is drawn for this one.");
                    break;
            }
    }

    /// <summary>
    /// <c>@harmonic(12)</c>, a natural harmonic at a fret (owner-approved 2026-11-13, grammar
    /// evaluation 3): what the text alone can refuse — an argument that is no harmonic's fret
    /// (<see cref="DiagnosticCodes.HarmonicFretMalformed"/>), a host that is not a single note
    /// (<see cref="DiagnosticCodes.HarmonicFretNotOnANote"/>), a part with no strings
    /// (<see cref="DiagnosticCodes.HarmonicFretNotFretted"/>). One reason a mark, in that
    /// order. Whether the note is an open string needs its pitch, and is
    /// <see cref="HarmonicOpenStringValidator"/>'s.
    /// </summary>
    private void NaturalHarmonic(MusicMarkSyntax harmonic)
    {
        string frets = string.Join(", ", Music.Harmonics.Frets);
        if (AnnotationValues.HarmonicFret(harmonic) is not { } fret)
        {
            string written = string.Join(" ", harmonic.Arguments.Select(a => a.Text));
            _diagnostics.Error(harmonic.Span, DiagnosticCodes.HarmonicFretMalformed,
                $"'{written}' is not a fret a natural harmonic is written at, so the note is drawn and played as written. "
                + $"The frets are {frets}: write the open string's note and the fret the string is touched at "
                + "(e,4@harmonic(12)). '@harmonic' alone is the small circle over a note.");
            return;
        }
        if (harmonic.Parent is not NoteSyntax)
        {
            _diagnostics.Error(harmonic.Span, DiagnosticCodes.HarmonicFretNotOnANote,
                $"'@harmonic({fret})' is written on a single note, the open string the harmonic is played on "
                + $"(e,4@harmonic({fret})), and this is not one - so nothing is changed by it. Harmonics on several "
                + "strings at once are written in voices, a string each; in a <...> chord, a "
                + "member's '@harmonic' with no fret is the touched note of an artificial harmonic (<c' f'@harmonic>4).");
            return;
        }
        if (!FrettedParts.Holds(harmonic))
            _diagnostics.Error(harmonic.Span, DiagnosticCodes.HarmonicFretNotFretted,
                $"'@harmonic({fret})' names a fret on a string, and this note is not in the music of a fretted "
                + "instrument's part (a guitar, a bass, a ukulele, a banjo, a mandolin; a phrase's body is no part's) - "
                + "so the note is drawn and played as written. Elsewhere '@harmonic' alone draws the small circle "
                + "over the note, and <c' f'@harmonic>4 an artificial harmonic's diamond.");
    }

    private static string WhyNoBarre(MusicMarkSyntax barre)
    {
        var arguments = barre.Arguments;
        if (arguments.Length == 0)
            return "it is written with no fret";
        if (arguments[0].Value?.AsInt is not { } fret || arguments[0].Text.Any(c => !char.IsAsciiDigit(c)))
            return $"'{arguments[0].Text}' is not a fret";
        if (fret < 1 || fret > MusicMarkItem.BarreMaxFret)
            return $"fret {fret} is outside 1 to {MusicMarkItem.BarreMaxFret}";
        return arguments.Length > 2
            ? "it takes a fret and at most the word 'half'"
            : $"'{arguments[1].Text}' is not a word of a barre (the one word is 'half')";
    }

    private void BarreNamesNothing(SyntaxNode barre, string why)
        => _diagnostics.Error(barre.Span, DiagnosticCodes.BarreMalformed,
            $"this '@barre' names no barre - {why} - so nothing is drawn. A barre is written with its fret, 1 to "
            + $"{MusicMarkItem.BarreMaxFret}: '@barre(7)' prints CVII, '@barre(7 half)' a half barre, and '@!barre' on the last note it "
            + "holds ends its line (b4@barre(7) d' fis' b'@!barre); with no '@!barre' after it the barre is the label on its one note.");
}

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

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reads the <c>transpose</c> of a part: its own option, with the file's top-level
/// <c>transpose</c> (<see cref="FileTranspose"/>) composed outside it. Shared by the renderer's
/// collector and the MIDI / MusicXML / LilyPond exporters so the single transpose grammar has one
/// reader.
/// </summary>
/// <remarks>
/// Until 2026-11-16 a top-level <c>transpose</c> was the DEFAULT of every part that wrote none (a
/// part's own replaced it), and <c>shift N</c> moved the whole piece outside it. The owner retired
/// <c>shift</c> into the top-level <c>transpose</c>, which now moves the whole piece — a part's own
/// <c>transpose</c> included.
/// </remarks>
public static class PartTranspose
{
    /// <summary>
    /// The effective transpose for <paramref name="partName"/>, or null: the part's own option and
    /// the file's transpose outside it — and, in a file written at concert pitch
    /// (<see cref="ConcertPitch"/>), the instrument's own written-side shift composed inside both,
    /// so an alto saxophone's part prints a major sixth above the letters.
    /// </summary>
    /// <remarks>
    /// ⚠️ The concert-pitch shift lives HERE, in the one reader every consumer of a part's
    /// transpose already asks (the page's collector, MIDI, MusicXML, the LilyPond twin),
    /// and not beside any of them: the alternative — each consumer adding the instrument's
    /// shift for itself — is a second spelling of "shift this part", and the MIDI exporter
    /// would then pass the preset's −9 twice (once here, once as the sounding shift).
    /// </remarks>
    public static (int step, int alt, int oct)? Read(SyntaxNode root, string partName)
    {
        // Part declarations are top-level only (Parser.ParseTopLevelItem), so the
        // root's direct children are the whole search space (SyntaxNode.ChildNodes).
        var partDecl = ConcertPitch.FindPart(root, partName);
        return Read(partDecl, ConcertPitch.FileIsConcert(root), FileTranspose.Read(root));
    }

    /// <summary>
    /// The same reading for a caller that already holds the file's values —
    /// <paramref name="fileIsConcert"/> is <see cref="ConcertPitch.FileIsConcert"/>'s answer,
    /// <paramref name="fileTranspose"/> <see cref="FileTranspose.Read"/>'s. The page's collector
    /// reads both ONCE per collect, in the definitions walk it makes anyway (they are a function of
    /// the whole tree, not of the part). The file's transpose is composed OUTSIDE all of it — the
    /// whole piece moves, a part's own transpose included.
    /// </summary>
    public static (int step, int alt, int oct)? Read(PartDeclarationSyntax? partDecl,
        bool fileIsConcert, (int step, int alt, int oct)? fileTranspose = null)
    {
        var written = partDecl != null ? Read(partDecl) : null;
        return PitchTransposer.NullIfIdentity(PitchTransposer.Compose(PitchTransposer.Compose(
            ConcertPitch.InputShift(fileIsConcert, partDecl), written), fileTranspose));
    }

    /// <summary>
    /// Parses a score header's <c>transpose</c> into a c→target interval, or null. A numeric one
    /// (<c>transpose +2</c>) is spelled from the key the music is in when the score takes it: the
    /// file's first key after the file's own transpose.
    /// </summary>
    public static (int step, int alt, int oct)? ReadProperty(PropertyAssignmentSyntax prop)
    {
        if (!TransposeValue.IsTranspose(prop))
            return null;
        return FileTranspose.IntervalOf(prop, () =>
        {
            var root = RootOf(prop);
            int from = FileTranspose.FirstKeySharps(root);
            if (FileTranspose.Read(root) is { } file)
                from += PitchTransposer.KeySignatureFifthsShift(file.step, file.alt);
            return from;
        });
    }

    /// <summary>Reads the transpose option from a part declaration, or null. A numeric one is
    /// spelled from the file's first key.</summary>
    public static (int step, int alt, int oct)? Read(PartDeclarationSyntax partDecl)
    {
        foreach (var prop in partDecl.Properties)
            if (TransposeValue.IsTranspose(prop))
                return FileTranspose.IntervalOf(prop, () => FileTranspose.FirstKeySharps(RootOf(partDecl)));
        return null;
    }

    private static SyntaxNode RootOf(SyntaxNode node)
    {
        while (node.Parent != null)
            node = node.Parent;
        return node;
    }
}

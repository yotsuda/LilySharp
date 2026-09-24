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

using System;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// A key's tonic as a diatonic step (0=C..6=B) and alteration in semitones
/// (Ees=-1, Fis=+1). <see cref="Valid"/> is false for a custom/atonal key,
/// which has no tonic to transpose from or to.
/// </summary>
public readonly record struct KeyTonic(int Step, int Alter, bool Valid)
{
    /// <summary>C major — the tonic assumed when no key is stated.</summary>
    public static readonly KeyTonic CMajor = new(0, 0, true);

    /// <summary>The tonic of a non-custom key declaration.</summary>
    public static KeyTonic Of(KeySignatureSyntax key) => key.IsCustom
        ? new KeyTonic(0, 0, false)
        : new KeyTonic(Math.Max(0, KeySpelling.StepOf(key.Pitch.PitchName[0])),
                       key.Pitch.AccidentalOffset, true);
}

/// <summary>
/// The score's home key tonic — the top-level <c>key</c> declaration (not one
/// inside a section / phrase / part block). It is the reference key a movable
/// phrase is written in; each phrase reference is auto-transposed from it to the
/// ambient key at the reference site. Shared by the renderer's collector and the
/// MIDI / MusicXML exporters so all three place a phrase identically.
/// </summary>
public static class ScoreHomeKey
{
    /// <summary>
    /// The score's home tonic. A later top-level key overrides an earlier one
    /// (matching the collector's header pass); no top-level key means C major.
    /// </summary>
    public static KeyTonic Read(SyntaxNode root)
    {
        var home = KeyTonic.CMajor;
        foreach (var key in root.DescendantNodes<KeySignatureSyntax>())
            if (!IsInsideMusicContent(key, excludePartHeader: true))
                home = KeyTonic.Of(key);
        return home;
    }

    /// <summary>
    /// The home key signature's sharp count (−7..+7; flats are negative). The
    /// WRITTEN key that scale-degree chords stack against. A later top-level key
    /// overrides an earlier one; no top-level key means C major (0). A custom
    /// home key has no sharp count, so 0.
    /// </summary>
    public static int Sharps(SyntaxNode root)
    {
        int sharps = 0;
        foreach (var key in root.DescendantNodes<KeySignatureSyntax>())
            if (!IsInsideMusicContent(key, excludePartHeader: false) && !key.IsCustom)
                sharps = KeySpelling.SharpsFor(
                    key.Pitch.ToFullString().Trim().ToLowerInvariant(),
                    key.Mode.Text.ToLowerInvariant()) ?? 0;
        return sharps;
    }

    /// <summary>
    /// The home key's DECLARATION node, or null when the file writes none (C major
    /// by default). Same walk as <see cref="Read"/> — the LilyPond exporter re-emits
    /// this node verbatim when a section boundary restores the score key, so mode
    /// and spelling come from the source instead of a reverse sharps→tonic table.
    /// </summary>
    public static KeySignatureSyntax? Declaration(SyntaxNode root)
    {
        KeySignatureSyntax? home = null;
        foreach (var key in root.DescendantNodes<KeySignatureSyntax>())
            if (!IsInsideMusicContent(key, excludePartHeader: true))
                home = key;
        return home;
    }

    /// <summary>
    /// The key a part's HEADER states (<c>part p { key bes major … }</c>, outside its sections),
    /// or null — that part's own home, which the page opens it in and restores it to at a section
    /// boundary (MeasureCollector.GetPartDefaults), ahead of the file-level one.
    /// </summary>
    public static KeySignatureSyntax? PartHeaderDeclaration(PartDeclarationSyntax? part)
    {
        if (part == null) return null;
        KeySignatureSyntax? home = null;
        foreach (var key in part.DescendantNodes<KeySignatureSyntax>())
            if (NearestContainer(key) == part)
                home = key;
        return home;
    }

    // A key inside a section/phrase/part cell is a modulation, not the score home.
    // ⚠️ A PART HEADER'S key is that part's default, not the file's (MeasureCollector's own
    // IsInsideMusicContent excludes PartDeclarationSyntax for exactly this reason): Read and
    // Declaration exclude it, so the phrase auto-transpose home is the page's (HANDOFF §2 R12⒞,
    // session 570 — a part's header key used to become EVERY part's home, the last part's
    // winning). Sharps still counts it: the MIDI exporter's scale-degree reset reads that, and
    // moving it is a separate question.
    private static bool IsInsideMusicContent(SyntaxNode node, bool excludePartHeader)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PhraseDeclarationSyntax or SectionDeclarationSyntax
                or VariableDeclarationSyntax or PartBlockSyntax
                || (excludePartHeader && p is PartDeclarationSyntax))
                return true;
        return false;
    }

    /// <summary>The nearest enclosing declaration or cell of <paramref name="node"/>.</summary>
    internal static SyntaxNode? NearestContainer(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PhraseDeclarationSyntax or SectionDeclarationSyntax
                or VariableDeclarationSyntax or PartBlockSyntax or PartDeclarationSyntax)
                return p;
        return null;
    }
}

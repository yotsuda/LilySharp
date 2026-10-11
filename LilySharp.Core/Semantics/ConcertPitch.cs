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
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The concert-pitch convention, read from its two spellings and turned into the one
/// thing every consumer wants: a <c>transpose</c>-shaped interval to compose onto a part.
/// </summary>
/// <remarks>
/// <para>
/// Two conventions exist for a transposing instrument. WRITTEN pitch (the default, and what
/// every book on disk means): the note letters are what the player reads, the part prints
/// exactly that, and only playback moves — <c>InstrumentDefaults.GetTransposition</c>
/// answers the sounding shift. CONCERT pitch (<c>pitch concert</c> at the top level): the
/// letters are what SOUNDS, and a transposing part is printed the way its player reads it —
/// an E♭ alto saxophone's <c>c'</c> prints as <c>a'</c>, in A major when the piece is in C.
/// </para>
/// <para>
/// A part header takes the same words to say it for THAT part alone (own &gt; file default,
/// as <c>transpose</c> and <c>octave</c> do), so a saxophone copied from a transposed
/// part-sheet and a piano copied from a concert-pitch score can share one book.
/// The second spelling is on a score header — <c>score full pitch concert { … }</c>,
/// <c>score trumpet pitch transposed { … }</c> — and says how THAT score prints: at sounding
/// pitch (the conductor's score) or the way each player reads. A score that writes none
/// prints each part in the convention its letters are written in — the part's own, else
/// the file's — so nothing moves (owner's decision 2026-10-11: one word, one meaning at
/// every level, and a score inherits it; until then a score that wrote none printed for
/// the players, so a concert-pitch file's own score was transposed). The letters' convention
/// and the page's compose to one page shift per part:
/// </para>
/// <list type="table">
/// <item><term>letters written, page written</term><description>nothing moves (the
/// default, and every book that writes no <c>pitch</c>)</description></item>
/// <item><term>letters written, page concert</term><description>+T: a written part shown
/// at what it sounds</description></item>
/// <item><term>letters concert, page written</term><description>−T: the part printed for
/// its player</description></item>
/// <item><term>letters concert, page concert</term><description>nothing moves</description></item>
/// </list>
/// <para>
/// where T is the part's chromatic transposition (<see cref="PartHeaderDefaults.ConcertShiftSemitones"/>:
/// an octave-only transposer keeps its notation in a concert score, so its T is 0).
/// </para>
/// <para>
/// ⚠️ THE SHIFT RIDES THE <c>transpose</c> CHANNEL AND NOTHING ELSE. <see cref="PartTranspose.Read(SyntaxNode, string)"/>
/// composes the file-level half onto the part's transpose, so the page, the MIDI, the
/// MusicXML and the LilyPond twin all move the SAME way a hand-written <c>transpose a</c>
/// would move them — pitches, key signature and accidentals together, which is the
/// machinery <c>transpose</c> already has (OctaveContext.TransposeKeySharps). The
/// score-level half is composed where the score's own <c>transpose</c> is composed. There
/// is no second spelling of "shift a part" anywhere, and no second reader of the sounding
/// shift: playback still adds <see cref="PartHeaderDefaults.SoundingShiftSemitones"/> to the
/// PRINTED pitch, so a concert-pitch file's alto saxophone prints <c>a'</c> and sounds
/// <c>c'</c> — the two shifts cancel exactly once, which <c>ConcertPitchTests</c> holds with
/// a non-transposing part as the control.
/// </para>
/// </remarks>
public static class ConcertPitch
{
    /// <summary>The property name, in both positions.</summary>
    public const string Property = "pitch";

    /// <summary>The value that says the notes are sounding pitches — typed (top level, part)
    /// or printed (score, staff).</summary>
    public const string Concert = "concert";

    /// <summary>The value that says the TYPED notes are what the player reads — the default,
    /// at the top level and in a part header.</summary>
    public const string Written = "written";

    /// <summary>The value that says a score or a staff PRINTS a transposing part in its
    /// player's key.</summary>
    public const string Transposed = "transposed";

    /// <summary>The two words <c>pitch</c> takes where it says how the notes are TYPED — the
    /// top level and a part header — the default first.</summary>
    /// <remarks>
    /// Owner's decision 2026-10-11 (option D): one keyword, and the word after it says which
    /// question it answers — <c>written</c> is how notes are written, <c>transposed</c> how a
    /// page is printed — so a misplaced one is refused with the fix
    /// (<see cref="MisplacedMessage"/>). Until then both places took <c>written</c>, read as
    /// "as the file writes it" as often as "as the player reads it".
    /// </remarks>
    public static readonly IReadOnlyList<string> NotesModes = [Written, Concert];

    /// <summary>The two words <c>pitch</c> takes where it says how a page PRINTS — a score
    /// header and a staff's <c>as pitch …</c>.</summary>
    public static readonly IReadOnlyList<string> PageModes = [Transposed, Concert];

    /// <summary>The message for a mode written where the other kind belongs — <c>written</c>
    /// on a score or a staff, <c>transposed</c> at the top level or in a part header — naming
    /// the word that place takes; null for any other word.</summary>
    public static string? MisplacedMessage(string word, bool page) => (word, page) switch
    {
        (Written, true) => "'written' says how notes are typed; a score or a staff prints them "
                         + "'transposed' (in the player's key) or 'concert' (as they sound).",
        (Transposed, false) => "'transposed' says how a score or a staff prints; notes are typed "
                             + "'written' (as the player reads them) or 'concert' (as they sound).",
        _ => null,
    };

    /// <summary>
    /// Reads a TYPED-notes <c>pitch</c> property (top level, part header): true for
    /// <c>concert</c>, false for <c>written</c>, null when the node is not a pitch property or
    /// carries another word (the parser or the header's validator has reported that one).
    /// </summary>
    public static bool? ReadProperty(PropertyAssignmentSyntax prop)
    {
        if (!string.Equals(prop.NameToken.Text, Property, StringComparison.OrdinalIgnoreCase))
            return null;
        return prop.ValueText switch
        {
            Concert => true,
            Written => false,
            _ => null,
        };
    }

    /// <summary>Reads a PAGE mode word (score header, staff): true for <c>concert</c>, false
    /// for <c>transposed</c>, null for anything else.</summary>
    private static bool? ReadPageWord(string? word) => word switch
    {
        Concert => true,
        Transposed => false,
        _ => null,
    };

    /// <summary>
    /// Whether the file's music is written at concert pitch: the first top-level
    /// <c>pitch</c> directive says so. A directive on a score header is that score's own
    /// (<see cref="ScoreMode"/>) and is not the file's.
    /// </summary>
    /// <remarks>
    /// Green finder rather than a red walk, for the reason <see cref="PartTranspose.ReadScoreDefault"/>
    /// gives: this is asked per part per keystroke.
    /// </remarks>
    public static bool FileIsConcert(SyntaxNode root)
    {
        foreach (var prop in root.GreenSites(
                     static g => (g.Kind == SyntaxKind.PropertyAssignment, Descend: true)))
            if (prop is PropertyAssignmentSyntax pa && TryReadFileMode(pa, out bool concert))
                return concert;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="prop"/> is a site <see cref="FileIsConcert"/> answers from — a
    /// <c>pitch</c> outside every part header and score block carrying one of the two words —
    /// and, when it is, the mode. One predicate for the whole-tree read above and for the
    /// collector's definitions walk, which meets every property assignment in the same
    /// pre-order and keeps the first such site (see <see cref="PartTranspose.TryReadScoreDefault"/>).
    /// </summary>
    public static bool TryReadFileMode(PropertyAssignmentSyntax prop, out bool concert)
    {
        if (!prop.IsInside<PartDeclarationSyntax>() && !prop.IsInside<RenderDeclarationSyntax>()
            && ReadProperty(prop) is { } mode)
        {
            concert = mode;
            return true;
        }
        concert = false;
        return false;
    }

    /// <summary>
    /// How a <c>staff</c> item prints its part — <c>staff tp as pitch transposed</c> (false),
    /// <c>as pitch concert</c> (true) — or null when it writes no <c>pitch</c> and the score's
    /// own answers (<see cref="ScoreMode"/>).
    /// </summary>
    public static bool? StaffMode(StaffRenderSyntax staff) => ReadPageWord(StaffTargets.Of(staff).Pitch?.Text);

    /// <summary>
    /// How <paramref name="render"/> prints the part <paramref name="partName"/>: the first of
    /// its <c>staff</c> items for that part that writes a <c>pitch</c> (a group's members
    /// included), else the score's own (<see cref="ScoreMode"/>) — null when neither says,
    /// and the part prints in its letters' convention (<see cref="OutputShift"/>).
    /// </summary>
    /// <remarks>
    /// Owner's decision 2026-10-11: how a page reads belongs to the score — the whole score
    /// (<c>score full pitch concert</c>) or one of its staves (a sounding full score with the
    /// trumpet in its player's key: <c>staff tp as pitch transposed</c>).
    /// </remarks>
    public static bool? PageMode(RenderDeclarationSyntax? render, string partName)
        => StaffModes(render)?.TryGetValue(partName, out bool own) == true ? own : ScoreMode(render);

    /// <summary>The parts whose <c>staff</c> items in <paramref name="render"/> write a
    /// <c>pitch</c>, each with the first one's mode; null when none does.</summary>
    public static System.Collections.Immutable.ImmutableDictionary<string, bool>? StaffModes(RenderDeclarationSyntax? render)
    {
        if (render == null)
            return null;
        System.Collections.Immutable.ImmutableDictionary<string, bool>.Builder? modes = null;
        foreach (var staff in render.DescendantNodes().OfType<StaffRenderSyntax>())
        {
            if (StaffMode(staff) is not { } own)
                continue;
            modes ??= System.Collections.Immutable.ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.Ordinal);
            modes.TryAdd(staff.PartToken?.Text ?? SyntaxFacts.UnnamedPartName, own);
        }
        return modes?.ToImmutable();
    }

    /// <summary>Whether <paramref name="render"/> asks to be printed at concert pitch (true),
    /// for the players (false), or writes no <c>pitch</c> and prints each part in the
    /// convention its letters are written in (null).</summary>
    public static bool? ScoreMode(RenderDeclarationSyntax? render)
        => render?.PitchMode is { } p ? ReadPageWord(p.ValueText) : null;

    /// <summary>
    /// The part header's own <c>pitch</c>, or null when it writes none: a part that copies a
    /// transposed part-sheet beside one that copies a concert-pitch score can say so, each
    /// for itself. Own &gt; file default, the rule <c>transpose</c> and <c>octave</c> follow.
    /// </summary>
    public static bool? PartMode(PartDeclarationSyntax? part)
    {
        if (part == null) return null;
        foreach (var prop in part.Properties)
            if (ReadProperty(prop) is { } mode)
                return mode;
        return null;
    }

    /// <summary>
    /// The written-side shift a concert-pitch part gives itself — sounding to written, −T —
    /// or null when the part is written-pitch or does not transpose chromatically. The
    /// part's own <c>pitch</c> decides; <paramref name="fileIsConcert"/> is the default for a
    /// part that writes none.
    /// </summary>
    public static (int step, int alt, int oct)? InputShift(bool fileIsConcert, PartDeclarationSyntax? part)
        => (PartMode(part) ?? fileIsConcert)
            ? Interval(-PartHeaderDefaults.Read(part).ConcertShiftSemitones)
            : null;

    /// <summary>
    /// The shift a score printing <paramref name="part"/> AT concert pitch gives it — written
    /// to sounding, +T — or null when the page is for the players, or the part does not
    /// transpose chromatically. The page's convention is the score's own
    /// (<see cref="ScoreMode"/>), else the part's letters' (its own <c>pitch</c>, else
    /// <paramref name="fileIsConcert"/>). Composed onto the part's transpose where the score's
    /// own <c>transpose</c> is, so with <see cref="InputShift"/> also armed the two cancel.
    /// </summary>
    public static (int step, int alt, int oct)? OutputShift(bool? scoreMode, bool fileIsConcert, PartDeclarationSyntax? part)
        => (scoreMode ?? PartMode(part) ?? fileIsConcert)
            ? Interval(PartHeaderDefaults.Read(part).ConcertShiftSemitones)
            : null;

    /// <summary>The declared part named <paramref name="partName"/>, or null: part
    /// declarations are top-level only (Parser.ParseTopLevelItem).</summary>
    public static PartDeclarationSyntax? FindPart(SyntaxNode root, string partName)
        => root.ChildNodes().OfType<PartDeclarationSyntax>()
            .FirstOrDefault(p => p.Name.Text == partName);

    /// <summary>Null for no shift, so a part that does not transpose stays "untransposed"
    /// to every reader that tests the interval for null.</summary>
    private static (int step, int alt, int oct)? Interval(int semitones)
        => semitones == 0 ? null : PitchTransposer.IntervalFromSemitones(semitones);
}

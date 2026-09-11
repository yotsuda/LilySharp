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

namespace LilySharp.Core.Semantics;

/// <summary>Whether a rule looks at the note's own octave or at any octave.</summary>
/// <remarks>LILYPOND-REF: scm/music-functions.scm:1756-1771 make-accidental-rule —
/// <c>'same-octave</c> is "different from the last active pitch in the same octave",
/// <c>'any-octave</c> "looks at the last active pitch in any octave".</remarks>
public enum AccidentalOctaveness
{
    /// <summary>LilyPond's <c>'same-octave</c>.</summary>
    SameOctave,
    /// <summary>LilyPond's <c>'any-octave</c>.</summary>
    AnyOctave,
}

/// <summary>
/// One accidental rule: an octave scope and a LAZINESS — how many bar lines an
/// alteration is remembered across.
/// </summary>
/// <param name="Octaveness">Which remembered alteration the rule consults.</param>
/// <param name="Laziness">
/// <c>0</c> = to the end of the current measure (LilyPond's default), a positive N = that
/// many bar lines further, <see cref="Forever"/> = never forgotten (LilyPond's <c>#t</c>),
/// <see cref="ForgetImmediately"/> = only the key signature is consulted (LilyPond's
/// <c>-1</c>).
/// </param>
/// <remarks>LILYPOND-REF: scm/music-functions.scm:1767-1771 make-accidental-rule — the
/// laziness paragraph, whose four cases these four values are.</remarks>
public readonly record struct AccidentalRule(AccidentalOctaveness Octaveness, int Laziness)
{
    /// <summary>LilyPond's <c>#t</c> laziness: remembered for the whole piece.</summary>
    public const int Forever = int.MaxValue;

    /// <summary>LilyPond's <c>-1</c> laziness: nothing is remembered.</summary>
    public const int ForgetImmediately = -1;

    /// <summary>
    /// LilyPond's <c>recent-enough?</c> for an entry stamped <paramref name="entryBar"/>,
    /// asked in bar <paramref name="bar"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm recent-enough? — at :1653-1656,
    /// <c>(or (number? alteration-def) (equal? laziness #t) (&lt;= bar-number (+ (cadr
    /// alteration-def) laziness)))</c>. The first arm is LilyPond's key-signature entry,
    /// which carries no bar number; Lily# reads the key signature separately
    /// (MeasureCollector.GetKeySignatureAlteration), so only the other two arms are here.
    /// </remarks>
    public bool RecentEnough(int entryBar, int bar) =>
        Laziness == Forever || bar <= entryBar + Laziness;
}

/// <summary>
/// One accidental style: the rules that decide whether an accidental prints, the rules
/// that make it a CAUTIONARY one instead, and LilyPond's <c>extraNatural</c> flag.
/// </summary>
/// <param name="Word">The word written in <c>layout { accidentals … }</c>.</param>
/// <param name="LilyPondName">The word LilyPond's <c>\accidentalStyle</c> takes — the
/// same style, hyphenated, which the <c>.ly</c> twin writes.</param>
/// <param name="ExtraNatural">Whether a "restore" natural is prepended to an accidental
/// that steps down inside one sign (𝄪→♯).</param>
/// <param name="Accidentals">LilyPond's <c>autoAccidentals</c> rules.</param>
/// <param name="Cautionaries">LilyPond's <c>autoCautionaries</c> rules.</param>
public sealed record AccidentalStyleSpec(
    string Word,
    string LilyPondName,
    bool ExtraNatural,
    IReadOnlyList<AccidentalRule> Accidentals,
    IReadOnlyList<AccidentalRule> Cautionaries)
{
    /// <summary>
    /// True when no rule of this style remembers anything past the bar line, so the
    /// memory may be CLEARED there instead of carrying bar-stamped entries forward.
    /// </summary>
    /// <remarks>
    /// A representation choice, not a rule change: with every laziness at 0 or −1,
    /// <see cref="AccidentalRule.RecentEnough"/> is false for every entry stamped in an
    /// earlier bar, so clearing and keeping are observationally identical. It is worth
    /// having because the collector's resume gate (MeasureCollector.WalkCarriesNothing)
    /// asks whether the accidental memory is EMPTY — carrying entries across bars for
    /// every book would switch incremental resume off for all of them.
    /// </remarks>
    public bool ForgetsAtBar { get; } =
        Accidentals.Concat(Cautionaries).All(r => r.Laziness <= 0);
}

/// <summary>
/// The accidental styles <c>layout { accidentals … }</c> takes — LilyPond's
/// <c>\accidentalStyle</c> table, transcribed for the styles whose context is the staff.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: scm/music-functions.scm accidental-styles — at :1901-2072, the alist
/// this table is a transcription of. Each entry there is (extraNatural, autoAccidentals,
/// autoCautionaries) and, for the piano/choral styles, a default context.
/// </para>
/// <para>
/// ⚠️ ONLY THE STAFF-CONTEXT STYLES ARE HERE. LilyPond's <c>voice</c>,
/// <c>modern-voice</c>, <c>neo-modern-voice</c>, <c>piano</c> and <c>choral</c> families
/// name a Voice / GrandStaff / ChoirStaff context, and a Lily# <c>layout { }</c> switch
/// is score-wide with no context to name; <c>neo-modern</c>, <c>teaching</c> and the
/// dodecaphonic family need rules that are not <c>make-accidental-rule</c> (they read the
/// note's own end moment, or make every note carry an accidental) and would be a second
/// mechanism rather than a second row of this table. Both are absences, not
/// approximations: a style this table does not hold is refused at the word.
/// </para>
/// <para>
/// ⚠️ THE MEMORY'S SCOPE IS LILY#'S, NOT LILYPOND'S CONTEXT. The collector keeps one
/// alteration memory per music walk, which is where LilyPond keeps a Staff context's for
/// a one-voice staff. This table does not change that scope; it changes what is
/// remembered and for how long.
/// </para>
/// </remarks>
public static class AccidentalStyles
{
    /// <summary>The key as written in a <c>layout { }</c> block.</summary>
    public const string Key = "accidentals";

    private static AccidentalRule Same(int laziness) => new(AccidentalOctaveness.SameOctave, laziness);
    private static AccidentalRule Any(int laziness) => new(AccidentalOctaveness.AnyOctave, laziness);

    /// <summary>LilyPond's <c>default</c>: accidentals as they were common in the 18th
    /// century — one rule, the note's own octave, to the end of the measure.</summary>
    /// <remarks>LILYPOND-REF: scm/music-functions.scm accidental-styles — at :1909-1911,
    /// (default #t (Staff same-octave 0) ()).</remarks>
    public static readonly AccidentalStyleSpec Default =
        new("default", "default", ExtraNatural: true, [Same(0)], []);

    /// <remarks>LILYPOND-REF: scm/music-functions.scm accidental-styles — at :1920-1924,
    /// (modern #f (Staff same-octave 0, any-octave 0, same-octave 1) ()): Kurt Stone's,
    /// which cancels in other octaves and in the next measure too.</remarks>
    public static readonly AccidentalStyleSpec Modern =
        new("modern", "modern", ExtraNatural: false, [Same(0), Any(0), Same(1)], []);

    /// <remarks>LILYPOND-REF: scm/music-functions.scm accidental-styles — at :1926-1929,
    /// (modern-cautionary #f (Staff same-octave 0) (Staff any-octave 0, same-octave 1)):
    /// the accidentals Stone ADDS to the old standard are the cautionary ones.</remarks>
    public static readonly AccidentalStyleSpec ModernCautionary =
        new("modernCautionary", "modern-cautionary", ExtraNatural: false,
            [Same(0)], [Any(0), Same(1)]);

    /// <remarks>LILYPOND-REF: scm/music-functions.scm accidental-styles — at :2063-2065,
    /// (forget () (Staff same-octave -1) ()): nothing is remembered, so every note is read
    /// against the key signature alone. <c>extraNatural</c> is not in the entry (the
    /// <c>()</c>), so the context keeps the value it had: LilyPond's own default, true.</remarks>
    public static readonly AccidentalStyleSpec Forget =
        new("forget", "forget", ExtraNatural: true, [Same(AccidentalRule.ForgetImmediately)], []);

    /// <remarks>LILYPOND-REF: scm/music-functions.scm accidental-styles — at :2069-2071,
    /// (no-reset () (Staff same-octave #t) ()): the key is not reset at the start of a
    /// measure, so an accidental is printed once and holds until overridden, possibly many
    /// measures later. <c>extraNatural</c> as for <c>forget</c>.</remarks>
    public static readonly AccidentalStyleSpec NoReset =
        new("noReset", "no-reset", ExtraNatural: true, [Same(AccidentalRule.Forever)], []);

    /// <summary>Every style, in the order the completion offers them (the default first).</summary>
    public static readonly IReadOnlyList<AccidentalStyleSpec> All =
        [Default, Modern, ModernCautionary, Forget, NoReset];

    /// <summary>The words, in the same order.</summary>
    public static readonly IReadOnlyList<string> Words = [.. All.Select(s => s.Word)];

    /// <summary>The style <paramref name="word"/> names, or null.</summary>
    public static AccidentalStyleSpec? Find(string word) =>
        All.FirstOrDefault(s => string.Equals(s.Word, word, System.StringComparison.Ordinal));
}

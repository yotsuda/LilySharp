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
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The word after <c>key</c> names a TONIC, and a tonic is a note. Anything else is
/// rejected here instead of being read as C.
/// </summary>
/// <remarks>
/// <para>
/// <c>ParseKeySignature</c> takes the tonic with a bare <c>ParsePitch</c>, which consumes
/// whatever token is current — an ordinary Identifier included. <c>KeySpelling.TonicFifths</c>
/// then answers null for it, and every one of the eight callers coerces that to 0 with
/// <c>?? 0</c>. So <c>key ef major</c> engraved as C major, exported
/// <c>&lt;fifths&gt;0&lt;/fifths&gt;</c>, and said nothing: the "fallback swallows it" shape
/// (HANDOFF §7.7), and the one a writer is most likely to hit, because <c>ef</c> and
/// <c>eb</c> are how E flat is spelled nearly everywhere except LilyPond.
/// </para>
/// <para>
/// ⚠️ THE MODE HALF OF THE SAME DECLARATION HAS REFUSED AN UNKNOWN WORD ALL ALONG
/// (Parser.Directives, "Unknown mode 'x'. Modes are case-sensitive: major, minor, …"). One
/// declaration, two words, two weights — the shape HANDOFF §5.0 records as "the noisy
/// spelling looks done, so it drops out of the list". The tonic was the quiet half.
/// </para>
/// <para>
/// LILYPOND-REF: ly/music-functions-init.ly — the <c>key</c> music function declares its
/// first argument <c>(ly:pitch? '())</c>, so "a tonic is a pitch" is LilyPond's own contract
/// and not a rule Lily# is adding. MEASURED 2026-08-24 on 2.26.0: <c>\key ef \major</c>
/// gives "wrong type for argument 1. Expecting pitch, found \"ef\"" and stops.
/// </para>
/// <para>
/// ⚠️ A KEY PAST SEVEN FIFTHS IS NOT THIS. <c>key gis major</c> is eight sharps and engraves
/// them (ledger key.signature.glyphs.tonic-past-the-table); only a tonic that is not a note
/// AT ALL is refused. The two used to fail the same way, which is why they read as one
/// defect and are two.
/// </para>
/// </remarks>
internal sealed class KeyTonicValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var key in tree.GetRoot().DescendantNodes().OfType<KeySignatureSyntax>())
        {
            // A non-traditional signature names its altered pitches instead of a tonic, and
            // the parser has already gated those on IsPitchStart.
            if (key.IsCustom || key.Pitch is not { } pitch)
                continue;

            string written = pitch.PitchName;
            if (written.Length == 0 || KeySpelling.TonicFifths(written) is not null)
                continue;

            _diagnostics.Error(pitch.Span, DiagnosticCodes.UnknownSymbolCase,
                $"'{written}' is not a key. A tonic is a letter a-g with an optional "
                + "accidental suffix — is sharp, isis double sharp, es flat, eses double "
                + "flat (e.g. 'key ees major', 'key fis minor')."
                + DidYouMean(written));
        }
    }

    /// <summary>
    /// The nearest legal spelling for the habits that bring a writer here: English
    /// <c>Eb</c> and the German-ish <c>Ef</c> / <c>Gs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ A CANDIDATE IS THE ONE PLACE RECONSTRUCTION IS RIGHT — there is no source text for
    /// a spelling the writer did not write (HANDOFF §5.0: report from the source, suggest
    /// from a rule). It is built through <see cref="KeySpelling.TonicFifths"/> so a
    /// suggestion the compiler would reject cannot be offered: the editor shipped exactly
    /// that defect twice in session 240.
    /// </para>
    /// <para>
    /// ⚠️ THERE IS NO <c>#</c> ARM, and its absence is measured rather than assumed:
    /// <c>key f# major</c> never reaches here because the lexer refuses the <c>#</c> as a
    /// stray character first, so the tonic this sees is <c>f</c> — a perfectly good one. An
    /// arm for it would be a dead exclusion, which reads as coverage (HANDOFF §5.0).
    /// </para>
    /// </remarks>
    private static string DidYouMean(string written)
    {
        if (written.Length != 2) return "";
        string? suffix = char.ToLowerInvariant(written[1]) switch
        {
            'b' or 'f' => "es",
            's' => "is",
            _ => null,
        };
        if (suffix is null) return "";
        string candidate = char.ToLowerInvariant(written[0]) + suffix;
        return KeySpelling.TonicFifths(candidate) is null ? "" : $" Did you mean '{candidate}'?";
    }
}

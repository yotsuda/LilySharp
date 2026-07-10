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
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Parser;

internal sealed partial class Parser
{
    // ========== Structure Declarations ==========

    private PartDeclarationGreen ParsePartDeclaration()
    {
        var keyword = Expect(SyntaxKind.PartKeyword);
        var name = ExpectPartName();   // names may be clef-name words (bass/treble/...)

        // Check if there's a body
        if (!Check(SyntaxKind.OpenBrace))
        {
            // No body: part name
            return new PartDeclarationGreen(keyword, name);
        }

        // With body: part name { props }
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var properties = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            // Part-major form: a `part` may hold its own `section` blocks
            //   part bass { clef bass  section A { c d } section B { e f } }
            // Each inner section's music belongs to THIS part (cell = section x part).
            if (Check(SyntaxKind.SectionKeyword))
            {
                properties.Add(ParsePartInnerSection());
                continue;
            }

            var prop = ParsePartProperty();
            if (prop != null)
                properties.Add(prop);
            else
                Advance();
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new PartDeclarationGreen(keyword, name, openBrace, [.. properties], closeBrace);
    }

    /// <summary>
    /// Parse a section nested inside a part (part-major form). Unlike a top-level
    /// section — whose body is per-part blocks — an inner section's body is the
    /// music itself, implicitly bound to the enclosing part. Built faithfully
    /// (no synthesized tokens) so source positions stay exact.
    /// </summary>
    private SectionDeclarationGreen ParsePartInnerSection()
    {
        var keyword = Expect(SyntaxKind.SectionKeyword);
        var name = ExpectPartName();
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = new List<GreenNode?>();
        while (_pendingPostEventMarkers.Count > 0
               || (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile)))
        {
            var item = ParseMusicItem();
            if (item != null)
                items.Add(item);
            else
                Advance();
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new SectionDeclarationGreen(keyword, name, openBrace, [.. items], closeBrace);
    }

    // Legacy part inside score: part Name "display" { staff... }
    /// <summary>
    /// Parses items with <paramref name="parseItem"/> until <paramref name="close"/>
    /// or EOF. A null result skips one token (the single infinite-loop guard shared
    /// by every brace-delimited list).
    /// </summary>
    private List<GreenNode?> ParseList(SyntaxKind close, System.Func<GreenNode?> parseItem)
    {
        var items = new List<GreenNode?>();
        while (!Check(close) && !Check(SyntaxKind.EndOfFile))
        {
            var item = parseItem();
            if (item != null)
                items.Add(item);
            else
                Advance();
        }
        return items;
    }

    private GreenNode? ParsePartProperty()
    {
        // In a part/staff header every attribute is written BARE ('name value'),
        // including time and tempo (which keep their richer value grammars); a stray
        // ':' is flagged and dropped. This matches the bare music-stream forms.
        if (Current.Kind == SyntaxKind.TimeKeyword)
            return ParseTimeSignature();
        if (Current.Kind == SyntaxKind.TempoKeyword)
            return ParseTempoDeclaration();

        // clef treble, instrument "Violin", channel 1, tuning standard, transpose d
        if (Current.Kind == SyntaxKind.Identifier ||
            Current.Kind == SyntaxKind.ClefKeyword ||
            Current.Kind == SyntaxKind.InstrumentKeyword ||
            Current.Kind == SyntaxKind.ChannelKeyword ||
            Current.Kind == SyntaxKind.TuningKeyword ||
            Current.Kind == SyntaxKind.OctaveKeyword ||
            Current.Kind == SyntaxKind.TransposeKeyword)
        {
            var propName = Advance();
            // Bare canonical form ('clef treble'); a stray ':' is flagged and skipped.
            var colon = ConsumeRejectedColon();
            var value = Advance(); // identifier, string, number, or pitch
            // A transpose target may carry octave marks (transpose d' / c,);
            // harmless for the other properties, which never have trailing marks.
            // A hyphenated bare value ('instrument bass-guitar') is ONE word:
            // keep consuming minus+word pairs — it used to truncate silently
            // to "bass". (Lyrics/chords never reach this header-only path, so
            // merging hyphens here is safe.)
            var values = new List<GreenNode?> { value };
            while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma)
                   || (Check(SyntaxKind.Minus) && IsPartNameKind(Peek(1)?.Kind)))
            {
                if (Check(SyntaxKind.Minus))
                {
                    values.Add(Advance()); // -
                    values.Add(Advance()); // word
                }
                else
                {
                    values.Add(Advance());
                }
            }
            // An instrument preset may carry a quoted display-name label:
            // `instrument violin "1st Violin"` — the preset drives clef/octave/tuning
            // defaults while the quoted label overrides the shown instrument name.
            if (propName.Kind == SyntaxKind.InstrumentKeyword && Check(SyntaxKind.StringLiteral))
                values.Add(Advance());
            return new PropertyAssignmentGreen(propName, colon, [.. values]);
        }
        return null;
    }

    // ========== Properties and Metadata ==========

    // Top-level `transpose d` (bare, like `time 4/4` / `key c major`): a default
    // transpose applied to every part that does not set its own. Octave marks
    // (transpose d' / c,) are allowed on the target.
    private PropertyAssignmentGreen ParseTopLevelTranspose()
    {
        var keyword = Advance(); // transpose
        var value = Advance();   // target pitch
        var values = new List<GreenNode?> { value };
        while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
            values.Add(Advance());
        return new PropertyAssignmentGreen(keyword, null, [.. values]);
    }

    private MetadataDeclarationGreen ParseMetadataDeclaration()
    {
        var keyword = Advance();
        var valueTokens = new List<GreenNode?>();

        // The value is a quoted string — like score names and other free-text values
        // (title "Song", composer "Name"); a bare, unquoted value is rejected.
        if (Check(SyntaxKind.StringLiteral))
        {
            valueTokens.Add(Advance());
        }
        else
        {
            var span = new TextSpan(_textPosition, Math.Max(1, Current.FullWidth));
            _diagnostics.Error(span, DiagnosticCodes.MetadataValueMustBeQuoted,
                $"The {keyword.Text} value must be a quoted string, e.g. {keyword.Text} \"…\".");
            // Recover by consuming the old loose run so the rest still parses.
            while (Check(SyntaxKind.StringLiteral) ||
                   Check(SyntaxKind.IntegerLiteral) ||
                   Check(SyntaxKind.Identifier) ||
                   IsPitchStart() ||
                   Check(SyntaxKind.MajorKeyword) ||
                   Check(SyntaxKind.MinorKeyword) ||
                   Check(SyntaxKind.Slash))
            {
                valueTokens.Add(Advance());
            }
        }

        return new MetadataDeclarationGreen(keyword, [.. valueTokens]);
    }

    // 'time 4/4' is written bare everywhere — as a music-stream command and as a
    // part/staff-header attribute. A stray ':' ('time: 4/4') is flagged and dropped
    // by ConsumeRejectedColon so the rest still parses.
    private TimeSignatureGreen ParseTimeSignature()
    {
        var timeKeyword = Expect(SyntaxKind.TimeKeyword);
        SyntaxToken? colon = ConsumeRejectedColon();
        // Senza misura: `time none` — unmeasured music (no signature printed,
        // no bar-length validation). MusicXML <senza-misura/>.
        if (Current.Kind == SyntaxKind.Identifier
            && Current.Text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return new TimeSignatureGreen(timeKeyword, colon, Advance());
        }
        var numerator = Expect(SyntaxKind.IntegerLiteral);
        // Additive meter: time 3+2/8 — MusicXML <beats>3+2</beats>.
        if (Check(SyntaxKind.Plus))
        {
            var numTokens = new List<GreenNode?> { numerator };
            while (Check(SyntaxKind.Plus))
            {
                numTokens.Add(Advance()); // +
                numTokens.Add(Expect(SyntaxKind.IntegerLiteral));
            }
            var addSlash = Expect(SyntaxKind.Slash);
            var addDen = Expect(SyntaxKind.IntegerLiteral);
            return new TimeSignatureGreen(timeKeyword, colon, [.. numTokens], addSlash, addDen);
        }
        var slash = Expect(SyntaxKind.Slash);
        var denominator = Expect(SyntaxKind.IntegerLiteral);
        return new TimeSignatureGreen(timeKeyword, colon, numerator, slash, denominator);
    }

    // 'tempo 120' is written bare everywhere (music-stream command and part/staff
    // attribute alike); a stray ':' is flagged and dropped by ConsumeRejectedColon.
    private TempoDeclarationGreen ParseTempoDeclaration()
    {
        var tempoKeyword = Expect(SyntaxKind.TempoKeyword);
        SyntaxToken? colon = ConsumeRejectedColon();
        var valueTokens = new List<GreenNode?>();

        // A single unquoted word right after `tempo` is the marking —
        // `tempo Comodo 4 = 84` — matching the bare-identifier rule used for
        // staff display names (quotes stay available for multi-word text).
        // Only the FIRST value position, so a following swing word or the
        // music that comes after the declaration is never swallowed.
        if (Check(SyntaxKind.Identifier) && !IsSwingWord(Current.Text))
            valueTokens.Add(Advance());

        // Collect value tokens: "marking" duration = bpm, plus an optional trailing
        // 'swing' / 'shuffle' feel word (kept as a value token; the red node reads it
        // via IsSwing). These are NOT reserved words, so they stay usable as names.
        while (Check(SyntaxKind.StringLiteral) ||
               Check(SyntaxKind.IntegerLiteral) ||
               // a dotted beat unit: "tempo \"Lively\" 4. = 116" lexes as
               // IntegerLiteral + Dot at declaration level — without accepting
               // the dot the parser stopped there and ". = 116" was dropped.
               Check(SyntaxKind.Dot) ||
               Check(SyntaxKind.Equals) ||
               (Check(SyntaxKind.Identifier) && IsSwingWord(Current.Text)))
        {
            valueTokens.Add(Advance());
        }

        return new TempoDeclarationGreen(tempoKeyword, colon, [.. valueTokens]);
    }

    private static bool IsSwingWord(string text) => text is "swing" or "shuffle";

    // partial <duration> — declares the following measure a pickup (anacrusis)
    // of the given length. The value reuses the note-duration grammar (number +
    // optional dots) so 'partial 4', 'partial 8' and 'partial 2.' all parse.
    private PartialDeclarationGreen ParsePartialDeclaration()
    {
        var partialKeyword = Expect(SyntaxKind.PartialKeyword);
        var number = Expect(SyntaxKind.IntegerLiteral);
        var dots = new List<GreenNode?>();
        while (Check(SyntaxKind.Dot))
            dots.Add(Advance());
        var duration = new DurationGreen(number, [.. dots]);
        return new PartialDeclarationGreen(partialKeyword, duration);
    }

    // ========== Variables ==========

    private VariableReferenceGreen ParseVariableReference()
    {
        // $name — reference a phrase.
        var dollar = Expect(SyntaxKind.Dollar);
        var name = ExpectPartName();   // phrase refs may name clef-name words too
        return new VariableReferenceGreen(dollar, name);
    }


    // A bare identifier in music is a PHRASE REFERENCE (the `$` sigil is gone —
    // `Chorus` not `$Chorus`). A word that reads like an English-accidental note
    // slip (eb, bb, fsharp) is almost certainly a mistyped pitch rather than a
    // phrase, so keep the Dutch-spelling hint for that case; anything else is taken
    // as a phrase reference and SymbolReferenceValidator reports it if undefined.
    private VariableReferenceGreen ParseBareVariableReference()
    {
        if (PitchSuggestion(Current.Text) is { } pitch)
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            var bad = Advance();
            _diagnostics.Error(span, DiagnosticCodes.BareReferenceRequiresDollar,
                $"'{bad.Text}' is not a valid note — did you mean the pitch '{pitch}'?");
            return new VariableReferenceGreen(bad);
        }
        return new VariableReferenceGreen(Advance());
    }

    // English-style accidental spellings map to Lily#'s Dutch note names:
    // eb -> ees, bb -> bes, gflat -> ges, fsharp -> fis. Returns null when the
    // word is not a plausible pitch typo.
    private static string? PitchSuggestion(string word)
    {
        if (word.Length is < 2 or > 6)
            return null;
        char letter = char.ToLowerInvariant(word[0]);
        if (letter is < 'a' or > 'g')
            return null;
        return word[1..].ToLowerInvariant() switch
        {
            "b" or "flat" => $"{letter}es",
            "sharp" => $"{letter}is",
            _ => null,
        };
    }
}

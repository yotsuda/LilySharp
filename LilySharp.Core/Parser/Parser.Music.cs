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
    private MusicBlockGreen ParseMusicBlock()
    {
        var openBrace = Expect(SyntaxKind.OpenBrace);
        var items = new List<GreenNode?>();

        while (_pendingPostEventMarkers.Count > 0
               || (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile)))
        {
            var item = ParseMusicItem();
            if (item != null)
                items.Add(item);
            else
                Advance(); // Skip unexpected token
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new MusicBlockGreen(openBrace, [.. items], closeBrace);
    }

    private bool IsMusicItemStart()
    {
        // Markers pending replay count as music items regardless of Current.
        if (_pendingPostEventMarkers.Count > 0)
            return true;

        var kind = Current.Kind;
        if (SyntaxFacts.IsPitchKind(kind) || SyntaxFacts.IsBarlineKind(kind))
            return true;

        return kind switch
        {
            SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => true,
            SyntaxKind.OpenAngle => true, // Chord
            SyntaxKind.VoiceKeyword => true, // Parallel voices: voice { } voice { }
            SyntaxKind.DoubleOpenAngle => true, // removed << >> — dispatched to a migration hint
            SyntaxKind.Tilde => true,
            SyntaxKind.OpenParen or SyntaxKind.CloseParen => true,
            SyntaxKind.OpenBracket or SyntaxKind.CloseBracket => true,
            SyntaxKind.RepeatKeyword => true,
            SyntaxKind.TupletKeyword => true,
            SyntaxKind.BreakKeyword or SyntaxKind.NoBreakKeyword => true,
            SyntaxKind.PartialKeyword => true,
            SyntaxKind.KeyKeyword => true,
            SyntaxKind.ClefKeyword => true,
            SyntaxKind.OctaveKeyword => true,
            SyntaxKind.GraceKeyword or SyntaxKind.AcciaccaturaKeyword or SyntaxKind.AppoggiaturaKeyword => true,
            SyntaxKind.LyricsKeyword => true,
            SyntaxKind.OverrideKeyword or SyntaxKind.RevertKeyword or SyntaxKind.OnceKeyword => true,
            SyntaxKind.Identifier => true, // Variable reference
            _ => false
        };
    }

    private GreenNode? ParseMusicItem()
    {
        // Replay slur/tie/beam markers that ParsePostEvents consumed out of
        // order (g4(@cresc — the '(' re-enters the sequence here).
        if (_pendingPostEventMarkers.Count > 0)
            return _pendingPostEventMarkers.Dequeue();

        var kind = Current.Kind;
        if (SyntaxFacts.IsPitchKind(kind)) return ParseNote();
        if (SyntaxFacts.IsBarlineKind(kind)) return ParseBarline();

        return kind switch
        {
            SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => ParseRest(),

            SyntaxKind.OpenAngle => ParseChord(),

            SyntaxKind.VoiceKeyword => ParseVoiceBlocksCheckingNesting(),

            // Removed syntax: report a migration hint, then recover by parsing the
            // old structure so no cascade of errors follows.
            SyntaxKind.DoubleOpenAngle => ParseRemovedParallelExpression(),

            // A leading backslash on a known LilyPond command (\tempo, \new, …) —
            // a habit from LilyPond — gets a hint pointing at the Lily# form.
            SyntaxKind.Backslash => ParseLilypondBackslashCommand(topLevel: false),

            SyntaxKind.Tilde => ParseTie(),

            SyntaxKind.OpenParen or SyntaxKind.CloseParen => ParseSlur(),

            SyntaxKind.OpenBracket => ParseBeamOrInlineVolta(),
            SyntaxKind.CloseBracket => ParseBeamMarker(),

            SyntaxKind.Dollar => ParseVariableReference(),


            SyntaxKind.RepeatKeyword => ParseRepeatExpression(),
            SyntaxKind.TupletKeyword => ParseTupletExpression(),
            SyntaxKind.KeyKeyword => ParseKeySignature(),
            SyntaxKind.ClefKeyword => ParseClefDeclaration(),
            SyntaxKind.OctaveKeyword => ParseOctaveDirective(),
            SyntaxKind.TimeKeyword => ParseTimeSignature(),
            SyntaxKind.TempoKeyword => ParseTempoDeclaration(),
            SyntaxKind.PartialKeyword => ParsePartialDeclaration(),

            SyntaxKind.GraceKeyword or SyntaxKind.AcciaccaturaKeyword or
            SyntaxKind.AppoggiaturaKeyword => ParseGraceExpression(),

            SyntaxKind.LyricsKeyword => ParseLyricsBlock(),
            SyntaxKind.BreakKeyword or SyntaxKind.NoBreakKeyword => ParseBreak(),
            SyntaxKind.OverrideKeyword => ParseOverrideDeclaration(),
            SyntaxKind.RevertKeyword => ParseRevertDeclaration(),
            SyntaxKind.OnceKeyword => ParseOnceModifier(),

            // Drum-kit vocabulary (bd, sn, hh, …) claims otherwise-invalid bare
            // identifiers; anything else keeps the deprecated-variable warning.
            SyntaxKind.Identifier => DrumNameRegistry.Contains(Current.Text)
                ? ParseDrumNote()
                : ParseBareVariableReference(),
            _ => null
        };
    }

    // ========== Notes and Pitches ==========

    private bool IsPitchStart() => SyntaxFacts.IsPitchKind(Current.Kind);

    private PitchGreen ParsePitch(bool inChord = false)
    {
        var pitchToken = Advance(); // Consume pitch token (c, cis, des, etc.)
        var octaveMarks = new List<GreenNode?>();

        // Collect octave marks: ' or ,
        while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
        {
            octaveMarks.Add(Advance());
        }

        // LILYPOND-REF: lily/lily-parser.yy — chord_body grammar accepts post-event
        // articulations on each pitch (e.g., <c@finger.1 e@finger.3>). Outside of
        // chord brackets, articulations belong to the surrounding NoteSyntax and
        // are consumed by ParseNote's own ParseArticulations call — we must NOT
        // pre-consume them here or they'd never reach the note.
        if (!inChord)
            return new PitchGreen(pitchToken, [.. octaveMarks]);

        var articulations = ParseArticulations();
        if (articulations.Length == 0)
            return new PitchGreen(pitchToken, [.. octaveMarks]);
        return new PitchGreen(pitchToken, [.. octaveMarks], articulations);
    }

    private DurationGreen? ParseOptionalDuration()
    {
        if (!Check(SyntaxKind.IntegerLiteral))
            return null;

        var number = Advance();
        var dots = new List<GreenNode?>();

        while (Check(SyntaxKind.Dot))
        {
            dots.Add(Advance());
        }

        return new DurationGreen(number, [.. dots]);
    }

    private NoteGreen ParseNote()
    {
        var pitch = ParsePitch();
        var duration = ParseOptionalDuration();
        var tremolo = Check(SyntaxKind.TremoloSuffix) ? Advance() : null;
        var articulations = ParsePostEvents();
        return new NoteGreen(pitch, duration, tremolo, articulations);
    }

    // bd4, sn8@f, hh — same trailing structure as ParseNote.
    // LILYPOND-REF: \drummode note events.
    private DrumNoteGreen ParseDrumNote()
    {
        var name = Advance();
        var duration = ParseOptionalDuration();
        var tremolo = Check(SyntaxKind.TremoloSuffix) ? Advance() : null;
        var articulations = ParsePostEvents();
        return new DrumNoteGreen(name, duration, tremolo, articulations);
    }

    /// <summary>
    /// Queue of slur/tie/beam markers consumed out of order by
    /// <see cref="ParsePostEvents"/>; <see cref="ParseMusicItem"/> replays them
    /// as the following sequence items, preserving source order.
    /// </summary>
    private readonly Queue<GreenNode> _pendingPostEventMarkers = new();

    /// <summary>
    /// Parses a note's/chord's trailing post-events with LilyPond's order-free
    /// semantics: slur parens, ties and beam brackets may interleave with
    /// <c>@</c>-articulations (<c>g4(@cresc</c> ≡ <c>g4@cresc(</c>). Every
    /// articulation belongs to the host note; the markers are replayed into
    /// the music sequence in source order via the pending queue.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lily-parser.yy post_events — an unordered list of
    /// post-events following the note.
    /// </remarks>
    private GreenNode?[] ParsePostEvents()
    {
        var articulations = ParseArticulations();
        if (!PendingMarkerRunHasArticulation())
            return articulations;

        var combined = new List<GreenNode?>(articulations);
        while (PendingMarkerRunHasArticulation())
        {
            _pendingPostEventMarkers.Enqueue(ParsePostEventMarker());
            combined.AddRange(ParseArticulations());
        }
        return [.. combined];
    }

    /// <summary>
    /// True when the upcoming run of post-event markers (<c>( ) ~ [ ]</c>) is
    /// followed by an <c>@</c>-articulation that must attach to the current
    /// note. A bare marker run (no trailing <c>@</c>) takes the normal
    /// sequence-item path untouched.
    /// </summary>
    private bool PendingMarkerRunHasArticulation()
    {
        if (!IsPostEventMarkerKind(Current.Kind))
            return false;
        int k = 1;
        while (IsPostEventMarkerKind(Peek(k).Kind))
            k++;
        return Peek(k).Kind == SyntaxKind.At;
    }

    private static bool IsPostEventMarkerKind(SyntaxKind kind) => kind
        is SyntaxKind.OpenParen or SyntaxKind.CloseParen
        or SyntaxKind.Tilde
        or SyntaxKind.OpenBracket or SyntaxKind.CloseBracket;

    private GreenNode ParsePostEventMarker() => Current.Kind switch
    {
        SyntaxKind.Tilde => ParseTie(),
        SyntaxKind.OpenBracket or SyntaxKind.CloseBracket => ParseBeamMarker(),
        _ => ParseSlur(),
    };

    private RestGreen ParseRest()
    {
        var restToken = Advance();
        var duration = ParseOptionalDuration();

        // LILYPOND-REF: lily/lily-parser.yy — R<dur>*N grammar.
        // Only valid for full-measure rests (R), but we accept the syntax for any
        // rest token and let semantic analysis enforce the constraint if needed.
        SyntaxToken? asterisk = null;
        SyntaxToken? measureCount = null;
        if (Check(SyntaxKind.Asterisk))
        {
            asterisk = Advance();
            if (Check(SyntaxKind.IntegerLiteral))
            {
                measureCount = Advance();
            }
            else
            {
                var span = new TextSpan(_textPosition, Current.FullWidth);
                _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                    $"Expected integer measure-count after '*', found '{Current.Kind}'");
            }
        }

        // Post-events on rests (r4@fermata, r2@coda, ...) — standard notation;
        // LILYPOND-REF: lily/lily-parser.yy — post-events attach to rests
        // (r4\fermata).
        var articulations = ParseArticulations();

        return new RestGreen(restToken, duration, asterisk, measureCount, articulations);
    }

    private ChordGreen ParseChord()
    {
        var openAngle = Expect(SyntaxKind.OpenAngle);
        var pitches = new List<GreenNode?>();

        // A chord is EITHER all named pitches (<c e g>) or an optional root pitch
        // followed by scale degrees (<c 3 5>). Mixing the two — a second named
        // pitch alongside degrees, or a pitch after a degree — is ambiguous, so
        // flag it once (best-effort parse continues).
        int pitchCount = 0;
        bool sawDegree = false;
        bool reportedMix = false;
        void ReportMixOnce()
        {
            if (reportedMix) return;
            reportedMix = true;
            var span = new TextSpan(_textPosition, Math.Max(1, Current.FullWidth));
            _diagnostics.Error(span, DiagnosticCodes.ChordMixesPitchesAndDegrees,
                "A chord can't mix named pitches and scale degrees — write all pitches "
                + "(<c e g>) or a root and degrees (<c 3 5>).");
        }

        while (true)
        {
            if (IsPitchStart())
            {
                if (sawDegree) ReportMixOnce(); // a named pitch after a degree
                // LILYPOND-REF: lily/lily-parser.yy chord_body — per-pitch articulations.
                pitches.Add(ParsePitch(inChord: true));
                pitchCount++;
                continue;
            }
            // Degree-chord member: after the root pitch, a bare number (or a
            // number with a glued accidental) is a scale degree stacked on the
            // root — <d 3 5 7,> = root d + the 3rd/5th/7th of the current key.
            if (Current.Kind is SyntaxKind.IntegerLiteral or SyntaxKind.ScaleDegree)
            {
                if (pitchCount > 1) ReportMixOnce(); // degrees stack on ONE root
                sawDegree = true;
                pitches.Add(ParseScaleDegree());
                continue;
            }
            // Drum chord member: <bd hh> — a bare drum name (no duration
            // inside the brackets, like pitches).
            if (Current.Kind == SyntaxKind.Identifier && DrumNameRegistry.Contains(Current.Text))
            {
                pitches.Add(new DrumNoteGreen(Advance(), null, null, []));
                continue;
            }
            break;
        }

        var closeAngle = Expect(SyntaxKind.CloseAngle);
        // Octave marks AFTER the closing '>' shift the WHOLE chord: <1 3 5>' up an
        // octave, <c e g>,, down two. Each member's own octave still applies first.
        var octaveMarks = new List<GreenNode?>();
        while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
            octaveMarks.Add(Advance());
        var duration = ParseOptionalDuration();
        var tremolo = Check(SyntaxKind.TremoloSuffix) ? Advance() : null;
        var articulations = ParsePostEvents();

        return new ChordGreen(openAngle, [.. pitches], closeAngle, [.. octaveMarks], duration, tremolo, articulations);
    }

    // A scale-degree chord member: the degree number (with any glued accidental)
    // then octave marks, mirroring ParsePitch's ' / , collection.
    private ScaleDegreeGreen ParseScaleDegree()
    {
        int degreeStart = _textPosition;
        var degree = Advance(); // IntegerLiteral or ScaleDegree
        // A scale degree is 1-based (1 = root); 0 (and anything that parses to < 1)
        // is not a degree. Without this, <0 …> silently read as the step below the
        // root (a leading tone), e.g. <0 2 4> as Bdim in C.
        int digits = 0;
        while (digits < degree.Text.Length && char.IsDigit(degree.Text[digits])) digits++;
        if (int.TryParse(degree.Text.AsSpan(0, digits), out int number) && number < 1)
            _diagnostics.Error(new TextSpan(degreeStart, Math.Max(1, degree.Width)),
                DiagnosticCodes.InvalidScaleDegree,
                $"Scale degree '{number}' is invalid — degrees are 1-based (1 = root, "
                + "3 = third, …).");
        var marks = new List<GreenNode?>();
        while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
            marks.Add(Advance());
        return new ScaleDegreeGreen(degree, [.. marks]);
    }

    private BarlineGreen ParseBarline()
    {
        var barToken = Advance();

        // Optional explicit repeat count on a :| end-repeat barline: ":|*N"
        // (reuses the R1*N multiplier idiom; sets the volta-repeat play count).
        if (barToken.Kind == SyntaxKind.RepeatEndBar && Check(SyntaxKind.Asterisk))
        {
            var asterisk = Advance();
            var count = Expect(SyntaxKind.IntegerLiteral);
            return new BarlineGreen(barToken, asterisk, count);
        }

        return new BarlineGreen(barToken);
    }

    private BreakGreen ParseBreak()
    {
        // `break` (force a line break here) or `nobreak` (forbid one). The keyword
        // token distinguishes them; BreakSyntax.IsNoBreak reads it.
        var breakKeyword = Advance();
        return new BreakGreen(breakKeyword);
    }

    // ========== Override/Revert ==========

    /// <summary>
    /// Parses: override Grob.property = value
    /// LILYPOND-REF: lily/context-property.cc (push)
    /// </summary>
    private OverrideDeclarationGreen ParseOverrideDeclaration()
    {
        var overrideKeyword = Expect(SyntaxKind.OverrideKeyword);
        var grobName = Expect(SyntaxKind.Identifier);
        var dot = Expect(SyntaxKind.Dot);
        var propertyName = Expect(SyntaxKind.Identifier);
        var equals = Expect(SyntaxKind.Equals);

        // Value: integer (lengths/positions), identifier (symbolic, e.g. up / red),
        // string (e.g. color = "red" — the collector strips the quotes), or a negative
        // integer. The resolver stores the value as a string and reparses it per property
        // (GetInt / GetDouble / GetString / GetBool), so all four forms flow through.
        var value = Current.Kind switch
        {
            SyntaxKind.IntegerLiteral => Advance(),
            SyntaxKind.Identifier => Advance(),
            SyntaxKind.StringLiteral => Advance(),
            SyntaxKind.Minus => CombineNegativeNumber(),
            _ => Expect(SyntaxKind.IntegerLiteral) // error recovery
        };

        return new OverrideDeclarationGreen(overrideKeyword, grobName, dot, propertyName, equals, value);
    }

    /// <summary>
    /// Combines a minus sign with following integer into a negative number token.
    /// </summary>
    private SyntaxToken CombineNegativeNumber()
    {
        var minus = Advance(); // consume -
        if (Current.Kind == SyntaxKind.IntegerLiteral)
        {
            var num = Advance();
            // Keep ALL consumed text — including any whitespace written between the '-'
            // and the digits ("- 5") — in the token's TEXT so its width equals the source
            // span (root.FullWidth == text.Length) and the tree round-trips exactly. The
            // interior trivia has nowhere else to live: a token's trivia is only leading
            // or trailing, never internal. The collector strips this interior whitespace
            // when it reads the numeric value.
            string inner = (minus.TrailingTrivia?.ToFullString() ?? "")
                         + (num.LeadingTrivia?.ToFullString() ?? "");
            return new SyntaxToken(SyntaxKind.IntegerLiteral, "-" + inner + num.Text,
                minus.LeadingTrivia, num.TrailingTrivia);
        }
        // Error: minus not followed by number
        return minus;
    }

    /// <summary>
    /// Parses: revert Grob.property
    /// LILYPOND-REF: lily/context-property.cc (pop)
    /// </summary>
    private RevertDeclarationGreen ParseRevertDeclaration()
    {
        var revertKeyword = Expect(SyntaxKind.RevertKeyword);
        var grobName = Expect(SyntaxKind.Identifier);
        var dot = Expect(SyntaxKind.Dot);
        var propertyName = Expect(SyntaxKind.Identifier);
        return new RevertDeclarationGreen(revertKeyword, grobName, dot, propertyName);
    }

    /// <summary>
    /// Parses: once override/revert ...
    /// LILYPOND-REF: lily/context-property.cc (temporary_override/revert)
    /// </summary>
    private OnceModifierGreen ParseOnceModifier()
    {
        var onceKeyword = Expect(SyntaxKind.OnceKeyword);

        GreenNode command;
        if (Current.Kind == SyntaxKind.OverrideKeyword)
            command = ParseOverrideDeclaration();
        else if (Current.Kind == SyntaxKind.RevertKeyword)
            command = ParseRevertDeclaration();
        else
        {
            // Error: once must be followed by override or revert
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected 'override' or 'revert' after 'once'");
            command = ParseOverrideDeclaration(); // attempt error recovery
        }

        return new OnceModifierGreen(onceKeyword, command);
    }

    private TieGreen ParseTie()
    {
        var tilde = Expect(SyntaxKind.Tilde);
        return new TieGreen(tilde);
    }

    private SlurGreen ParseSlur()
    {
        var paren = Advance();
        return new SlurGreen(paren);
    }

    private BeamMarkerGreen ParseBeamMarker()
    {
        var bracket = Advance();
        return new BeamMarkerGreen(bracket);
    }

    /// <summary>
    /// Disambiguates the overloaded <c>[</c> in a music stream: <c>[</c> followed
    /// by an integer is an inline volta ending (<c>[1. …]</c>); otherwise it is a
    /// beam marker (<c>[c d]</c>). A bare integer never legitimately follows a beam
    /// <c>[</c>, so the lookahead is unambiguous.
    /// </summary>
    private GreenNode ParseBeamOrInlineVolta()
    {
        if (Peek(1).Kind == SyntaxKind.IntegerLiteral)
            return ParseInlineVolta();
        return ParseBeamMarker();
    }

    /// <summary>
    /// Parses an inline volta ending: <c>[1. … ]</c>, <c>[1-2. … ]</c>, or
    /// <c>[1,3. … ]</c>. The body is a sequence of music items up to the closing
    /// <c>]</c>.
    /// </summary>
    private InlineVoltaGreen ParseInlineVolta()
    {
        var openBracket = Expect(SyntaxKind.OpenBracket);
        var number = Expect(SyntaxKind.IntegerLiteral);

        // Optional range/list: [1-2. …] or [1,3. …]
        SyntaxToken? separator = null;
        SyntaxToken? endNumber = null;
        if (Check(SyntaxKind.Minus) || Check(SyntaxKind.Comma))
        {
            separator = Advance();
            endNumber = Expect(SyntaxKind.IntegerLiteral);
        }

        var dot = Expect(SyntaxKind.Dot);

        // The ending body runs until a structural boundary: the closing ']' (which
        // makes the ending CLOSED), the next ending '[N.', a repeat barline, the
        // block close, or EOF. Internal beams '[c8 …]' and plain '|' barlines stay
        // part of the body. Omitting the ']' leaves the ending open on the right.
        var items = new List<GreenNode?>();
        while (_pendingPostEventMarkers.Count > 0 || !AtInlineVoltaBoundary())
        {
            var item = ParseMusicItem();
            if (item != null)
                items.Add(item);
            else
                Advance(); // skip unexpected token to recover
        }

        SyntaxToken? closeBracket = Check(SyntaxKind.CloseBracket) ? Advance() : null;
        return new InlineVoltaGreen(openBracket, number, separator, endNumber, dot, [.. items], closeBracket);
    }

    /// <summary>The token stream is at the end of an inline volta ending body: the
    /// closing ']', the next ending '[N.', a repeat barline, the block close, or EOF.
    /// A '[' that starts a beam (not followed by a number) is NOT a boundary.</summary>
    private bool AtInlineVoltaBoundary() =>
        Check(SyntaxKind.CloseBracket)
        || (Check(SyntaxKind.OpenBracket) && Peek(1).Kind == SyntaxKind.IntegerLiteral)
        || Check(SyntaxKind.RepeatStartBar)
        || Check(SyntaxKind.RepeatEndBar)
        || Check(SyntaxKind.RepeatBothBar)
        || Check(SyntaxKind.CloseBrace)
        || Check(SyntaxKind.EndOfFile);

private GreenNode?[] ParseArticulations()
    {
        var articulations = new List<GreenNode?>();

        while (true)
        {
            if (Check(SyntaxKind.At))
            {
                // @staccato, @accent, @p, @f, etc.
                var at = Advance();
                if (IsDynamicName())
                {
                    // @p, @f, @mf, @cresc, etc. - dynamics with @ prefix (new style).
                    // An optional '.up' / '.down' forces the dynamic above / below.
                    var name = Advance();
                    if (Current.Kind == SyntaxKind.Dot
                        && IsPlacementWord(Peek(1))
                        && Peek(2)?.Kind != SyntaxKind.Dot)
                    {
                        // cresc/decresc/dim drive a HAIRPIN (always below); '.up' / '.down'
                        // is a dynamic-text placement and is not meaningful there. Flag it
                        // explicitly (no silent drop) and recover as a plain trigger.
                        bool isHairpinTrigger = name.Text is "cresc" or "decresc" or "dim";
                        int qStart = _textPosition;
                        Advance();             // .
                        var dir = Advance();   // up / down
                        if (isHairpinTrigger)
                        {
                            var span = new TextSpan(qStart, System.Math.Max(1, _textPosition - qStart));
                            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                                $"'.{dir.Text}' placement is not supported on '@{name.Text}' (a hairpin is always below the staff).");
                            articulations.Add(new DynamicGreen(at, name));
                        }
                        else
                        {
                            articulations.Add(new DynamicGreen(at, name, dir));
                        }
                    }
                    else
                    {
                        articulations.Add(new DynamicGreen(at, name));
                    }
                }
                else if (IsArticulationName())
                {
                    // @name.up / @name.down — forced PLACEMENT on an articulation
                    // (above / below), recognised before the compound-mark form so the
                    // '.up' / '.down' qualifier is a placement, not a mark part.
                    if (Current.Kind == SyntaxKind.Identifier
                        && Peek(1)?.Kind == SyntaxKind.Dot
                        && IsPlacementWord(Peek(2)))
                    {
                        var name = Advance();
                        Advance();             // .
                        var dir = Advance();   // up / down
                        articulations.Add(new ArticulationGreen(at, name, dir));

                        // A second placement suffix (@staccato.up.down) is a mistake:
                        // an articulation takes only one side. Consume the extra
                        // '.up' / '.down' with a clear message instead of letting the
                        // bare 'up' / 'down' cascade into a confusing "not a note" error.
                        while (Check(SyntaxKind.Dot) && IsPlacementWord(Peek(1)))
                        {
                            int extraStart = _textPosition;
                            Advance();              // .
                            var extra = Advance();  // up / down
                            var span = new TextSpan(extraStart, Math.Max(1, _textPosition - extraStart));
                            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                                $"an articulation takes only one of '.up' / '.down'; remove the extra '.{extra.Text}'.");
                        }
                    }
                    // @name(args) — parenthesised arguments, e.g. @fig(6 4), @chord(Dm),
                    // @mark("A"), @finger(3), @feather(right), @ped(off). The '.' is reserved
                    // for .up/.down placement (handled above); EVERY annotation argument now
                    // goes in parentheses, separated by whitespace or commas. The arg tokens
                    // are kept on the green node (so the source span is exact) but excluded
                    // from MusicMarkSyntax.MarkName, which still yields "name.arg.arg" so the
                    // downstream collectors (figured bass / chord / fingering / mark) are
                    // unchanged.
                    else if (Current.Kind == SyntaxKind.Identifier && Peek(1)?.Kind == SyntaxKind.OpenParen)
                    {
                        var name = Advance();
                        var parts = new List<SyntaxToken> { at, name, Advance() /* ( */ };
                        while (!Check(SyntaxKind.CloseParen) && !Check(SyntaxKind.EndOfFile))
                            parts.Add(Advance()); // argument token (or a ',' separator)
                        parts.Add(Expect(SyntaxKind.CloseParen));
                        // @text("…").up / .down — placement on the free-text
                        // annotation. Only @text takes it: the other value
                        // annotations have fixed sides, and consuming a '.'
                        // here would corrupt their dotted MarkName forms.
                        if (name.Text.Equals("text", StringComparison.OrdinalIgnoreCase)
                            && Current.Kind == SyntaxKind.Dot
                            && IsPlacementWord(Peek(1))
                            && Peek(2)?.Kind != SyntaxKind.Dot)
                        {
                            parts.Add(Advance()); // .
                            parts.Add(Advance()); // up / down
                        }
                        articulations.Add(new MusicMarkGreen([.. parts]));
                    }
                    else if (Current.Kind == SyntaxKind.Identifier
                             && Current.Text.Equals("chord", StringComparison.OrdinalIgnoreCase))
                    {
                        // Bare '@chord' (no argument): auto-derive the chord symbol
                        // from the notes it is attached to. Kept as a MusicMark (like
                        // @chord(…)) so the chord-name collector handles it; the
                        // explicit form is still @chord(c:maj7).
                        var name = Advance();
                        articulations.Add(new MusicMarkGreen([at, name]));
                    }
                    else
                    {
                        // @staccato, @accent, @trill, etc. (a bare name; an annotation
                        // argument must use the (…) form above, not a '.').
                        var name = Advance();
                        articulations.Add(new ArticulationGreen(at, name));
                    }
                }
                else
                {
                    // Error: expected articulation or dynamic name after @
                    var span = new TextSpan(_textPosition, Current.FullWidth);
                    _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                        $"Expected articulation or dynamic name after '@', found '{Current.Kind}'");
                }
            }
            else if (Check(SyntaxKind.StringNumber))
            {
                // \4, \3 … — tab string-number annotation on the note (forces the
                // fret's string on a tab staff; ignored on a notation staff).
                var stringNum = Advance();
                articulations.Add(new StringNumberAnnotationGreen(stringNum));
            }
            else if (Check(SyntaxKind.Backslash))
            {
                int startPos = _textPosition;
                var backslash = Advance();
                if (IsDynamicName())
                {
                    // \p, \f, \cresc — a LilyPond habit. Lily# writes annotations with
                    // '@' (e.g. @p); backslash is reserved for tablature (string numbers
                    // like \3, and \tuning). Flag it, then recover by parsing it anyway.
                    var name = Advance();
                    var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
                    _diagnostics.Error(span, DiagnosticCodes.LilypondBackslashCommand,
                        $"Use '@{name.Text}' for annotations; backslash is reserved for tablature (e.g. string numbers like \\3).");
                    articulations.Add(new DynamicGreen(backslash, name));
                }
                else
                {
                    // Not a dynamic - might be other command, stop parsing articulations
                    _position--; // Put backslash back
                    _textPosition -= backslash.FullWidth;
                    break;
                }
            }
            else
            {
                break;
            }
        }

        return [.. articulations];
    }

    // 'up' / 'down' lex as plain identifiers; they are the placement words for the
    // '@name.up' / '@name.down' qualifier.
    private static bool IsPlacementWord(SyntaxToken? token) =>
        token?.Kind == SyntaxKind.Identifier && (token.Text == "up" || token.Text == "down");

    private bool IsArticulationName()
    {
        // Articulation/ornament names are plain identifiers after '@'; the specific
        // name is resolved by ArticulationRegistry, not by the lexer. A few mark
        // names (coda/segno/fine) lex as structure keywords but are equally valid
        // as in-music marks (e.g. g1@coda).
        return Current.Kind is SyntaxKind.Identifier
            or SyntaxKind.CodaKeyword
            or SyntaxKind.SegnoKeyword
            or SyntaxKind.FineKeyword;
    }

    private bool IsDynamicName()
    {
        // Dynamics are recognized by text: the level marks (p, f, mf, …) which the
        // lexer keeps as Dynamic* tokens (or pitch tokens like 'f'), plus cresc /
        // decresc / dim which are now plain identifiers resolved by name downstream.
        var text = Current.Text;
        if (SyntaxFacts.IsDynamicText(text) || SyntaxFacts.IsDynamicSpannerName(text))
            return true;
        return SyntaxFacts.IsDynamicKind(Current.Kind);
    }
}

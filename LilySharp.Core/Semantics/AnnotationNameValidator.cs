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

using System.Linq;
using LilySharp.Core.Music;
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
internal sealed class AnnotationNameValidator : ISemanticValidator
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
            "glissando",
            "cross",
            "arpeggio",
            "laissezvibrer", "repeattie",
            "dead",
            "stemup", "stemdown",
            "ho", "hammeron", "po", "pulloff", "tap", "snappizz", "slide", "stopped",
            "thumb", "heel", "toe", "scoop", "plop",
        };

    /// <summary>
    /// The names above, for tooling: the editor's '@' completion has to offer
    /// every annotation the collector consumes, and a test pins that against
    /// this set (plus ArticulationRegistry and the dynamics table). Adding a
    /// name here without adding a completion item fails that test.
    /// </summary>
    internal static IReadOnlyCollection<string> PlainFeatureNames => ExtraPlainNames;

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
        "startTrillSpan", "stopTrillSpan", "courtesy", "editorial", "glissando",
        // ⚠️ The navigation marks (segno, coda, fine, ds, dc, to coda) are NOT
        // here: they are bare landmarks, and writing one with an '@' has its own
        // diagnostic ("A navigation mark is bare, not '@'"). Suggesting '@segno'
        // would send the reader from one error straight into another.
        // ⚠️ "cue" is NOT here: a cue is a REGION (`cue { … }`), not a note annotation —
        // LilyPond's cue is the CueVoice context and nothing attaches to a note. An `@cue`
        // now falls through to the ordinary unknown-annotation diagnostic, which is the
        // intended message. See docs/cue-context-design.md §5.
        "cross", "arpeggio", "laissezvibrer", "repeattie",
        "rit", "accel", "cresc", "decresc", "dim",
        // Spelled as they should be READ: the matcher lowercases both sides, so
        // camelCase here only affects what the "did you mean" hint shows.
        "ottava", "ottava.bassa", "loco",
        "sustainOn", "sustainOff", "sostenutoOn", "sostenutoOff",
        "unaCorda", "treCorde",
        "mark.A", "finger.1", "feather.right", "feather.left",
        "notehead.x", "notehead.diamond", "notehead.slash",
        "fig.6", "chord.c",
    ];

    /// <summary>The candidates above, for the test that pins every one of them to
    /// a spelling a user can actually type.</summary>
    internal static IReadOnlyList<string> SuggestionNames => SuggestionCandidates;

    /// <summary>
    /// A mark name as it must be TYPED. Internally a compound annotation is one
    /// dotted string — "sost.off", "notehead.x", "fig.6.4" — because that is the
    /// collector's lookup key, but it is NOT source syntax: the source puts the
    /// argument in parentheses, and stacked figures are separated by spaces.
    /// Diagnostics have to speak the typeable form; printed raw, "did you mean
    /// '@sost.off'?" sends the reader straight into "Undefined variable or
    /// phrase: 'off'".
    /// </summary>
    internal static string SourceSpelling(string markName)
    {
        int dot = markName.IndexOf('.');
        if (dot < 0)
            return markName;

        var name = markName.Substring(0, dot);
        var arguments = markName.Substring(dot + 1).Replace('.', ' ');
        return $"{name}({arguments})";
    }

    /// <summary>
    /// Validates all <c>@name</c> annotations in a syntax tree.
    /// </summary>
    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        // root is a CompilationUnit — it never matches an annotation-bearing case,
        // so it is not checked separately (the old CheckNode(root) was a no-op).
        foreach (var node in root.DescendantNodes())
            CheckNode(node);
    }

    private void CheckNode(SyntaxNode node)
    {
        switch (node)
        {
            case ArticulationSyntax art:
            {
                var name = art.NameToken.Text;
                if (!IsKnownPlainName(name))
                    WarnUnknown(art, name);
                else if (OnArpeggioGroupOrMember(art))
                    WarnArpeggioUnsupported(art, name);
                break;
            }
            case DynamicSyntax dyn when dyn.Parent is PitchSyntax { Parent: ArpeggioSyntax }:
                // A dynamic works on the GROUP (`<< … >>@f`) but not on a bare member.
                WarnArpeggioUnsupported(dyn, dyn.DynamicToken.Text);
                break;
            case MusicMarkSyntax mark:
            {
                var name = mark.MarkName;
                if (!IsKnownCompoundName(mark))
                    WarnUnknown(mark, name);
                else if (AnnotationValues.Rehearsal(mark, out var labelIsQuoted) is not null
                         && !labelIsQuoted)
                    _diagnostics.Error(
                        mark.Span,
                        DiagnosticCodes.MarkLabelNotQuoted,
                        "a rehearsal mark label must be quoted: write @mark(\"A\") not @mark(A).");
                else if (name == "chord" && mark.Parent is ChordSyntax chord && !CanNameChord(chord))
                    _diagnostics.Warning(
                        chord.Span,
                        DiagnosticCodes.ChordNotRecognized,
                        "@chord can't name this chord — its notes match no known chord quality; "
                        + "use the explicit form, e.g. @chord(c:maj7).");
                else if (name == "chord" && mark.Parent is ChordRepetitionSyntax rep
                         && Music.ChordRepetitions.OriginalOf(rep) is { } orig && !CanNameChord(orig))
                    _diagnostics.Warning(
                        rep.Span,
                        DiagnosticCodes.ChordNotRecognized,
                        "@chord can't name this chord repetition — the repeated chord's notes "
                        + "match no known chord quality; use the explicit form, e.g. @chord(c:maj7).");
                else if (name == "chord" && mark.Parent is ArpeggioSyntax arp && !CanNameArpeggio(arp))
                    _diagnostics.Warning(
                        arp.Span,
                        DiagnosticCodes.ChordNotRecognized,
                        "@chord can't name this arpeggio — its notes match no known chord quality; "
                        + "use the explicit form, e.g. @chord(c:maj7).");
                else if (OnArpeggioGroupOrMember(mark)
                         && name != "chord" && AnnotationValues.Chord(mark, out _) == null)
                    // Chord names work on the group; everything else is unwired.
                    WarnArpeggioUnsupported(mark, name);
                break;
            }
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
    /// True iff a compound annotation is consumed somewhere: a music mark (incl.
    /// <c>mark.*</c> rehearsal marks), a trill-spanner event, fingering, feathered
    /// beams, figured bass, or a chord name.
    /// </summary>
    /// <remarks>
    /// ⚠️ This method is the reason <see cref="AnnotationValues"/> exists: it answers
    /// "does anything consume this?", which can only be answered by knowing what the
    /// consumers accept — so every family was spelled here a second time, and the
    /// audit counted it as the tenth restatement of the same ten readings
    /// (VALUE_SITE_AUDIT §9.3). Every family now ASKS its one reader, the figured bass
    /// included (§9.5.3 ⑴ — it reads the argument tokens, being a sub-language). What is
    /// left below is the dotted NAMES, which are not arguments at all.
    /// </remarks>
    public static bool IsKnownCompoundName(MusicMarkSyntax mark)
    {
        if (AnnotationValues.Finger(mark) is not null
            || AnnotationValues.Pluck(mark) is not null
            || AnnotationValues.Bend(mark) is not null
            || AnnotationValues.Notehead(mark) is not null
            || AnnotationValues.IsTextAnnotation(mark)
            || AnnotationValues.Feather(mark) != 0
            || AnnotationValues.IsArpeggioBracket(mark)
            || AnnotationValues.Frame(mark) is not null
            || AnnotationValues.Chord(mark, out _) is not null
            || AnnotationValues.Rehearsal(mark, out _) is not null
            || AnnotationValues.Figures(mark) is not null)
            return true;

        // What remains of the dotted name: the compound NAMES (@ds.al.fine, @ottava.bassa).
        return MusicMarkItem.ParseMarkName(mark.MarkName) != null;
    }


    /// <summary>
    /// Whether a bare <c>@chord</c> can auto-name this chord. Only pure named-pitch
    /// chords are checked (their recognition is key-independent); scale-degree
    /// chords, which need the running key, are left to the collector and never
    /// warned here.
    /// </summary>
    private static bool CanNameChord(ChordSyntax chord)
    {
        if (chord.Root is null || chord.Degrees.Any() || chord.DrumNames.Any())
            return true;

        int rootStep = RelativeOctave.StepIndex(chord.Root.PitchName.ToLowerInvariant()[0]);
        var pcs = chord.Pitches.Select(p =>
            RelativeOctave.StepSemitoneOf(RelativeOctave.StepIndex(p.PitchName.ToLowerInvariant()[0]))
            + p.AccidentalOffset);
        return ChordStructure.TryRecognize(rootStep, chord.Root.AccidentalOffset, pcs, out _);
    }

    /// <summary>
    /// Whether a bare <c>@chord</c> can auto-name this broken chord. The same
    /// stance as <see cref="CanNameChord"/>: only pure named-pitch members (a
    /// nested chord contributes its pitches) are checked key-independently; any
    /// scale degree defers to the collector and is never warned here.
    /// </summary>
    private static bool CanNameArpeggio(ArpeggioSyntax arp)
    {
        var pitches = new List<PitchSyntax>();
        foreach (var member in arp.Members)
        {
            switch (member)
            {
                case ScaleDegreeSyntax:
                    return true; // key-dependent — the collector's call
                case PitchSyntax p:
                    pitches.Add(p);
                    break;
                case ChordSyntax c:
                    if (c.Degrees.Any())
                        return true;
                    pitches.AddRange(c.Pitches);
                    break;
            }
        }
        if (pitches.Count == 0)
            return true; // nothing to derive from — the collector shows nothing
        int rootStep = RelativeOctave.StepIndex(pitches[0].PitchName.ToLowerInvariant()[0]);
        var pcs = pitches.Select(p =>
            RelativeOctave.StepSemitoneOf(RelativeOctave.StepIndex(p.PitchName.ToLowerInvariant()[0]))
            + p.AccidentalOffset);
        return ChordStructure.TryRecognize(rootStep, pitches[0].AccidentalOffset, pcs, out _);
    }

    /// <summary>True for an annotation sitting on a <c>&lt;&lt; … &gt;&gt;</c> group
    /// itself or on one of its BARE pitch members (a nested chord member keeps the
    /// chord's own annotation handling and is not flagged).</summary>
    private static bool OnArpeggioGroupOrMember(SyntaxNode annotation) =>
        annotation.Parent is ArpeggioSyntax
        || annotation.Parent is PitchSyntax { Parent: ArpeggioSyntax };

    private void WarnArpeggioUnsupported(SyntaxNode node, string name)
        => _diagnostics.Warning(node.Span, DiagnosticCodes.ArpeggioAnnotationUnsupported,
            $"'@{name}' on a '<< >>' group is not applied yet - only a dynamic (@f) and "
            + "a chord name (@chord) work there; for per-note articulations write the "
            + "passage as plain notes (a tuplet gives the same rhythm).");

    private void WarnUnknown(SyntaxNode node, string name)
    {
        // Both the name and the suggestion are shown as they are TYPED, not as the
        // collector keys them: the reader has to be able to copy what they read.
        //
        // ⚠️ The annotation itself is quoted from the SOURCE, not rebuilt from the
        // dotted name. SourceSpelling is a reconstruction and cannot be faithful: it
        // turns every '.' into a ' ', so it reported '@fig(6.4)' as '@fig(6 4)' — and
        // '@fig(6 4)' is VALID, i.e. the message named a working spelling as the broken
        // one. (It mis-punctuated dotted NAMES the same way: '@ds.al.fien' came out as
        // '@ds(al fien)'.) A candidate has no source text, so the suggestion below is
        // still rendered from its internal name, which is what that reconstruction is for.
        var message = $"Unknown annotation '{Written(node, name)}' — it is ignored.";
        var suggestion = FindSuggestion(name);
        if (suggestion != null)
            message += $" Did you mean '@{SourceSpelling(suggestion)}'?";

        _diagnostics.Warning(
            node.Span,
            DiagnosticCodes.UnknownAnnotation,
            message);
    }

    /// <summary>
    /// The annotation exactly as it was written.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: a message, so there is nothing in LilyPond to port. The guard is
    /// on the node's text INCLUDING its leading trivia (<c>ToFullString</c> is what this
    /// repo has for a node's source): whitespace trims away and an annotation then starts
    /// at its '@', but a comment written between the note and the '@' would not, and
    /// quoting that would be worse than reconstructing. It falls back to the internal
    /// name there, i.e. to the behaviour every message had before. That fallback is
    /// unexercised rather than unreachable: an annotation only carries leading trivia
    /// when it is written apart from its note, and across the 80-book corpus and 219
    /// fixtures not one line begins with '@' (measured 2026-08-15).
    /// </remarks>
    private static string Written(SyntaxNode node, string name)
    {
        var source = node.ToFullString().Trim();
        return source.StartsWith('@') ? source : $"@{SourceSpelling(name)}";
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
            // d == 0 means the name IS this candidate — never suggest it back to
            // itself ("did you mean '@harmonic'?" for '@harmonic'); only real typos
            // (distance 1..2) are useful.
            if (d >= 1 && d < bestDistance)
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

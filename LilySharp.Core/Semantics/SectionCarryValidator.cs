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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports a slur, phrasing slur, tie or hairpin that breaks the section carry rule
/// (<see cref="DiagnosticCodes.SpanAcrossSectionBoundary"/>, LYS4023) — in EVERY FORM A SCORE
/// PLAYS, not only the first score's.
/// </summary>
/// <remarks>
/// <para>
/// The rule is <see cref="SectionPlayCursor"/>'s and the findings are read back from the
/// collect (<see cref="MeasureCollector.SectionCarryWarnings"/>), for the reason
/// <see cref="SlurPairingValidator"/> gives: the page draws by the same rule.
/// </para>
/// <para>
/// ⚠️ PER FORM, because a section's neighbours are the form's: <c>form main { C D }</c> carries
/// C's closing slur into D, and <c>form other { D C }</c> leaves it with nothing after it. The
/// shared collect is the FIRST score's; every other score is collected too when it plays a
/// different form, or engraves a part no collect of that form has covered yet (owner's
/// decision, 2026-09-28: the forms scores use, deduplicated — not every declared form). Such an
/// extra collect also reports what the first score's validators report of the same span
/// families — an unpaired slur (LYS4010), phrasing slur (LYS4018) or tie (LYS4007) — since in
/// that form they are just as lost; those messages name the form.
/// </para>
/// </remarks>
internal sealed class SectionCarryValidator : ISharedCollectValidator
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(() => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // Cheap exit: a book with no section has no boundary for this rule to speak about —
        // and must not pay for extra collects.
        var root = tree.GetRoot();
        if (!root.DescendantNodes().OfType<SectionDeclarationSyntax>().Any())
            return;

        var specs = RenderSpecParser.FindAll(tree);
        var first = specs.Count > 0 ? specs[0] : null;
        Report(sharedCollect.Value, FormLabel(first?.Form), extra: false);

        // Every other form a score plays, once per (form, part): the first score covers its
        // own form's parts.
        var covered = new Dictionary<FormDeclarationSyntax, HashSet<string>>();
        if (first?.Form is { } firstForm)
            covered[firstForm] = new HashSet<string>(first.GetVoiceNames(), StringComparer.Ordinal);
        for (int i = 1; i < specs.Count; i++)
        {
            var spec = specs[i];
            if (spec.Form is not { } form)
                continue;
            if (!covered.TryGetValue(form, out var parts))
                covered[form] = parts = new HashSet<string>(StringComparer.Ordinal);
            bool adds = false;
            foreach (var name in spec.GetVoiceNames())
                adds |= parts.Add(name);
            if (!adds)
                continue;
            Report(SemanticValidation.TryCollect(tree, spec), FormLabel(form), extra: true, tree);
        }
    }

    private void Report(MeasureCollector? collector, string form, bool extra, SyntaxTree? tree = null)
    {
        if (collector == null)
            return;
        foreach (var w in collector.SectionCarryWarnings)
        {
            var span = new TextSpan(w.SourcePosition, 1);
            string message = MessageFor(w, form);
            _diagnostics.Add(w.Fault == SectionCarryFault.AcrossRepeat
                ? Diagnostic.Error(span, DiagnosticCodes.SpanAcrossSectionBoundary, message)
                : Diagnostic.Warning(span, DiagnosticCodes.SpanAcrossSectionBoundary, message));
        }
        if (!extra || tree == null)
            return;
        // In another form the same span families are lost the same way; the first score's
        // validators said so of the first form only.
        var lent = new System.Lazy<MeasureCollector?>(() => collector);
        foreach (var v in new ISharedCollectValidator[]
                 { new SlurPairingValidator(), new TieTargetValidator(), new SpanPairingValidator() })
        {
            v.ValidateWith(tree, lent);
            foreach (var d in v.Diagnostics)
            {
                if (v is SpanPairingValidator && !d.Message.Contains("phrasing slur", StringComparison.Ordinal))
                    continue;
                _diagnostics.Add(new Diagnostic(d.Severity, d.Span, d.Code, d.Message + " (in " + form + ")"));
            }
        }
    }

    /// <summary>"form 'main'", or the order the sections are declared in when no form is.</summary>
    private static string FormLabel(FormDeclarationSyntax? form)
        => form is { NameText: { Length: > 0 } name } ? "form '" + name + "'" : "the order the sections are declared in";

    /// <summary>The words for one fault. ASCII punctuation only: these strings reach
    /// legacy-codepage consoles via the CLI.</summary>
    internal static string MessageFor(SectionCarryWarning w, string form)
    {
        string from = w.From ?? "?", into = w.Into ?? "?";
        (string noun, string open, string close, string nothing) = w.Kind switch
        {
            SectionSpanKind.Slur => ("slur", "a slur '('", "')'", "no slur is drawn"),
            SectionSpanKind.PhrasingSlur => ("phrasing slur", "a phrasing slur", "'@!phrasingSlur'", "no curve is drawn"),
            SectionSpanKind.Tie => ("tie", "a tie '~'", "", "no tie is drawn"),
            _ => ("hairpin", "a hairpin", "a dynamic", "it is cut at the end of section " + from),
        };
        string rule = " - a " + noun + " open when a section ends is carried into the section played next and must end there";
        return w.Fault switch
        {
            SectionCarryFault.NotClosedInNext => w.Kind == SectionSpanKind.Hairpin
                ? open + " is carried from section " + from + " into section " + into + ", which follows it in "
                  + form + ", and nothing ends it there, so " + nothing + "; end it with " + close
                  + " in section " + into + rule
                : open + " is carried from section " + from + " into section " + into + ", which follows it in "
                  + form + ", and is not closed there, so " + nothing + "; close it with " + close
                  + " in section " + into + rule,
            SectionCarryFault.NothingCarriedIn =>
                "this " + close + " closes nothing: no " + noun + " is open, and section " + from
                + ", played before section " + into + " in " + form + ", carries none into it, so "
                + nothing + rule,
            SectionCarryFault.IntoEmptySection =>
                open + " is carried from section " + from + " into section " + into
                + ", where this part plays nothing in " + form + ", so " + nothing
                + "; end it in section " + from + rule,
            _ => (w.AtClose
                    ? "this " + close + " would close a " + noun + " carried into section " + into
                      + " from section " + from
                    : open + " would be carried from section " + from + " into section " + into)
                + " over a repeat sign, a volta ending or a jump in " + form
                + " - what is played before section " + into + " differs from pass to pass, so a "
                + noun + " may not cross there and " + nothing + "; end it in section " + from
                + " (only a tie may cross a repeat)",
        };
        // ⚠️ A TIE NEVER REACHES THE LAST ARM: it is carried over any repeat sign, ending or
        // jump, to the first note of each play that follows in the PLAYED order
        // (SectionPlayGraph), and a target on another pitch is LYS4007 like any tie's.
    }
}

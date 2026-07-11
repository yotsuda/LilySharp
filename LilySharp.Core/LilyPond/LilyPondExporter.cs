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

using System.Text;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.LilyPond;

/// <summary>
/// Exports a Lily# syntax tree to LilyPond (.ly) source — the inverse of the
/// LilyPond-style surface Lily# already speaks. Walks the tree so the original
/// note spelling, sections and structure are preserved. Octaves are remapped from
/// Lily#'s C4 = bare <c>c</c> anchor to LilyPond's C3 = bare <c>c</c>.
/// </summary>
public sealed class LilyPondExporter
{
    private readonly StringBuilder _sb = new();
    private bool _absolute;   // file-level `octave absolute`
    private readonly Dictionary<string, SectionDeclarationSyntax> _sections = new();
    private readonly List<string> _sectionOrder = new();

    // @-annotation name -> LilyPond articulation/command emitted as a note suffix.
    private static readonly Dictionary<string, string> Artic = new()
    {
        ["staccato"] = "\\staccato", ["accent"] = "\\accent", ["tenuto"] = "\\tenuto",
        ["marcato"] = "\\marcato", ["fermata"] = "\\fermata", ["portato"] = "\\portato",
        ["staccatissimo"] = "\\staccatissimo", ["trill"] = "\\trill",
        ["mordent"] = "\\mordent", ["prall"] = "\\prall", ["turn"] = "\\turn",
        ["fall"] = "\\bendAfter #-2", ["doit"] = "\\bendAfter #2",
        ["segno"] = "\\segno", ["coda"] = "\\coda", ["fine"] = "\\fine",
    };

    /// <summary>Exports the given syntax tree as LilyPond (.ly) source text.</summary>
    public string Export(SyntaxTree tree)
    {
        var root = tree.GetRoot();

        // Octave mode: top-level `octave absolute`.
        foreach (var n in root.DescendantNodes().OfType<OctaveDirectiveSyntax>())
            if (!IsInsideMusicContent(n)) _absolute = n.IsAbsolute;

        string? title = MetadataValue(root, "title");
        string? composer = MetadataValue(root, "composer");

        // Collect sections and structure order.
        foreach (var s in root.DescendantNodes().OfType<SectionDeclarationSyntax>())
            _sections.TryAdd(s.SectionName, s);
        // Emit the PRIMARY form (`main`, else the first declared).
        var forms = root.DescendantNodes().OfType<FormDeclarationSyntax>().ToList();
        var structure = forms.FirstOrDefault(f => f.NameText == "main") ?? forms.FirstOrDefault();

        _sb.AppendLine("\\version \"2.24.0\"");
        _sb.AppendLine();
        if (title != null || composer != null)
        {
            _sb.AppendLine("\\header {");
            if (title != null) _sb.AppendLine($"  title = \"{Escape(title)}\"");
            if (composer != null) _sb.AppendLine($"  composer = \"{Escape(composer)}\"");
            _sb.AppendLine("}");
            _sb.AppendLine();
        }

        // The music variable: leading directives (tempo/key/time/clef) then the
        // sections in structure order (volta repeats reconstructed from `|: :|`).
        _sb.AppendLine("music = {");
        EmitLeadingDirectives(root);
        if (structure != null)
            EmitForm(structure);
        else
            foreach (var s in SectionsInDeclarationOrder(root))
                EmitSectionBody(s);
        _sb.AppendLine();
        _sb.AppendLine("}");
        _sb.AppendLine();

        EmitScores(root);
        return _sb.ToString();
    }

    private void EmitLeadingDirectives(SyntaxNode root)
    {
        // Tempo / key / time live at file level; clef/tuning at part level. Emit
        // every directive that is NOT inside a section's music (mid-piece changes
        // are handled in the note stream).
        foreach (var n in root.DescendantNodes())
        {
            if (IsInsideMusicContent(n)) continue;
            switch (n)
            {
                case TempoDeclarationSyntax t when TempoBpm(t) is { } bpm:
                    _sb.AppendLine($"  \\tempo 4 = {bpm}"); break;
                case KeySignatureSyntax k:
                    _sb.AppendLine($"  \\key {k.Pitch.PitchName} \\{k.Mode.Text}"); break;
                case TimeSignatureSyntax ts:
                    _sb.AppendLine($"  \\time {ts.Beats}/{ts.BeatType}"); break;
                case ClefDeclarationSyntax c:
                    _sb.AppendLine($"  \\clef {c.ClefName.Text}"); break;
            }
        }
        // The clef is usually a part property (`part X { clef bass }`).
        var part = root.DescendantNodes().OfType<PartDeclarationSyntax>().FirstOrDefault();
        if (part != null && PartProp(part, "clef") is { } clef)
            _sb.AppendLine($"  \\clef {clef}");
    }

    private static string? PartProp(PartDeclarationSyntax part, string name)
    {
        var prop = part.Properties.FirstOrDefault(p => p.NameToken.Text == name);
        if (prop == null) return null;
        for (int i = 1; i < prop.SlotCount; i++)
            if (prop.GetChild(i) is SyntaxTokenNode t && t.Text != ":")
                return t.Text;
        return null;
    }

    private void EmitForm(FormDeclarationSyntax structure)
    {
        foreach (var child in structure.DescendantNodes())
        {
            switch (child)
            {
                case SectionReferenceSyntax r when !IsInsideRepeat(r):
                    EmitSectionRef(r.SectionName, label: true); break;
                case { Kind: SyntaxKind.SilentSectionReference } sr when !IsInsideRepeat(sr)
                        && sr.GetChild(1) is SyntaxTokenNode nm:
                    EmitSectionRef(nm.Text, label: false); break;
                case FormRepeatBlockSyntax repeat:
                    EmitRepeat(repeat); break;
                case NavigationMarkSyntax nav when !IsInsideRepeat(nav):
                    _sb.AppendLine().Append("  ").AppendLine(NavMarkLy(nav.MarkType)); break;
            }
        }
    }

    private static string NavMarkLy(NavigationMarkType t) => t switch
    {
        NavigationMarkType.Segno => "\\mark \\markup { \\musicglyph #\"scripts.segno\" }",
        NavigationMarkType.Coda => "\\mark \\markup { \\musicglyph #\"scripts.coda\" }",
        NavigationMarkType.Fine => "\\mark \\markup { \\italic \"Fine\" }",
        NavigationMarkType.ToCoda => "\\mark \\markup { \\italic \"To Coda\" }",
        NavigationMarkType.DaCapo => "\\mark \\markup { \\italic \"D.C.\" }",
        NavigationMarkType.DaCapoAlFine => "\\mark \\markup { \\italic \"D.C. al Fine\" }",
        NavigationMarkType.DaCapoAlCoda => "\\mark \\markup { \\italic \"D.C. al Coda\" }",
        NavigationMarkType.DalSegno => "\\mark \\markup { \\italic \"D.S.\" }",
        NavigationMarkType.DalSegnoAlFine => "\\mark \\markup { \\italic \"D.S. al Fine\" }",
        NavigationMarkType.DalSegnoAlCoda => "\\mark \\markup { \\italic \"D.S. al Coda\" }",
        _ => ""
    };

    private void EmitRepeat(FormRepeatBlockSyntax repeat)
    {
        _sb.AppendLine();
        _sb.AppendLine("  \\repeat volta 2 {");
        foreach (var r in repeat.DescendantNodes().OfType<SectionReferenceSyntax>())
            if (!IsInsideAlternative(r)) EmitSectionRef(r.SectionName, indent: "    ");
        var alts = repeat.DescendantNodes().OfType<FormAlternativeSyntax>().ToList();
        _sb.AppendLine("  }");
        if (alts.Count > 0)
        {
            _sb.AppendLine("  \\alternative {");
            foreach (var alt in alts)
            {
                _sb.AppendLine("    {");
                foreach (var r in alt.DescendantNodes().OfType<SectionReferenceSyntax>())
                    EmitSectionRef(r.SectionName, indent: "      ");
                _sb.AppendLine("    }");
            }
            _sb.AppendLine("  }");
        }
    }

    private void EmitSectionRef(string name, string indent = "  ", bool label = true)
    {
        if (_sections.TryGetValue(name, out var section))
            EmitSectionBody(section, indent, label);
    }

    private void EmitSectionBody(SectionDeclarationSyntax section, string indent = "  ", bool label = true)
    {
        _sb.AppendLine();
        _sb.Append(indent).AppendLine($"% {section.SectionName}");
        // A labelled section gets a boxed rehearsal mark; a silent (~Name) one
        // does not. Inline @mark annotations inside the music emit their own marks.
        if (label)
            _sb.Append(indent).AppendLine($"\\mark \\markup {{ \\box {SafeMark(section.SectionName)} }}");
        _sb.Append(indent);
        foreach (var node in section.DescendantNodes())
            EmitMusicNode(node);
        _sb.AppendLine();
    }

    private void EmitMusicNode(SyntaxNode node)
    {
        switch (node)
        {
            case GraceExpressionSyntax grace:
                string kw = grace.IsAcciaccatura ? "\\acciaccatura" : "\\grace";
                _sb.Append(kw).Append(" { ");
                foreach (var g in grace.Body.DescendantNodes().OfType<NoteSyntax>())
                    _sb.Append(EmitNote(g)).Append(' ');
                _sb.Append("} ");
                break;
            // Grace inner notes are emitted by the grace handler above; skip them
            // when the descendant walk reaches them again.
            case NoteSyntax when IsInsideGrace(node):
                break;
            case NoteSyntax note:
                _sb.Append(EmitNote(note)).Append(' '); break;
            case RestSyntax rest:
                _sb.Append(rest.RestToken.Text)
                   .Append(rest.Duration is { } d ? DurationText(d) : "").Append(' '); break;
            case ChordSyntax chord:
                EmitChord(chord); break;
            case BarlineSyntax:
                _sb.Append("| "); break;
            case TieSyntax:
                if (_sb.Length > 0 && _sb[^1] == ' ') _sb.Length--;
                _sb.Append("~ "); break;
            case BreakSyntax brk:
                _sb.Append(brk.IsNoBreak ? "\\noBreak " : "\\break "); break;
            case KeySignatureSyntax k when IsInsideMusicContent(k):
                _sb.Append($"\\key {k.Pitch.PitchName} \\{k.Mode.Text} "); break;
            case TimeSignatureSyntax ts when IsInsideMusicContent(ts):
                _sb.Append($"\\time {ts.Beats}/{ts.BeatType} "); break;
        }
    }

    private string EmitNote(NoteSyntax note)
    {
        var sb = new StringBuilder();
        bool dead = note.Articulations.OfType<ArticulationSyntax>()
            .Any(a => a.NameToken.Text == "dead");
        if (dead) sb.Append("\\deadNote ");
        sb.Append(Pitch(note.Pitch));
        if (note.Duration is { } d) sb.Append(DurationText(d));
        // string number \N
        foreach (var a in note.Articulations.OfType<StringNumberAnnotationSyntax>())
            sb.Append('\\').Append(a.StringNumber);
        foreach (var a in note.Articulations)
            AppendArtic(sb, a);
        return sb.ToString();
    }

    private void EmitChord(ChordSyntax chord)
    {
        _sb.Append('<');
        _sb.Append(string.Join(' ', chord.Pitches.Select(p =>
        {
            var t = Pitch(p);
            foreach (var a in p.Articulations.OfType<StringNumberAnnotationSyntax>())
                t += "\\" + a.StringNumber;
            return t;
        })));
        _sb.Append('>');
        if (chord.Duration is { } d) _sb.Append(DurationText(d));
        _sb.Append(' ');
    }

    private void AppendArtic(StringBuilder sb, SyntaxNode a)
    {
        switch (a)
        {
            case ArticulationSyntax art when art.NameToken.Text != "dead"
                    && Artic.TryGetValue(art.NameToken.Text, out var cmd):
                sb.Append(cmd); break;
            case MusicMarkSyntax mark:
                var name = mark.MarkName;
                if (name.StartsWith("mark."))
                    sb.Append($" \\mark \\markup {{ \\box {SafeMark(name[5..])} }}");
                else if (Artic.TryGetValue(name, out var c))
                    sb.Append(c);
                break;
        }
    }

    private string Pitch(PitchSyntax pitch)
    {
        // Lily# bare c = C4; LilyPond bare c = C3. In absolute mode add one octave
        // so the sounding pitch matches; in relative mode the offsets carry as-is.
        int offset = pitch.OctaveOffset + (_absolute ? 1 : 0);
        string marks = offset > 0 ? new string('\'', offset)
                     : offset < 0 ? new string(',', -offset) : "";
        return pitch.PitchName + marks;
    }

    private static string DurationText(DurationSyntax d) =>
        d.Value + new string('.', d.DotCount);

    private void EmitScores(SyntaxNode root)
    {
        foreach (var render in root.DescendantNodes().OfType<RenderDeclarationSyntax>())
        {
            var spec = RenderSpecParser.Parse(render);
            if (spec == null) continue;
            bool hasTab = spec.HasTab;
            bool hasStaff = spec.Items.Any(i => i is SingleStaffSpec or GrandStaffRenderSpec);
            string body = _absolute ? "\\music" : "\\relative c' { \\music }";
            // LilyPond's TabStaff reads the written pitch directly, whereas Lily#'s
            // bass tab sounds an octave lower (the original .ly used a lower
            // \relative base for the tab). Drop the tab music one octave so the
            // frets match.
            string tabBody = _absolute ? "\\transpose c' c { \\music }"
                                       : "\\relative c { \\music }";
            _sb.AppendLine("\\score {");
            if (hasStaff && hasTab)
            {
                _sb.AppendLine("  <<");
                _sb.AppendLine($"    \\new Staff {{ {body} }}");
                _sb.AppendLine($"    \\new TabStaff {TuningWith(spec)}{{ {tabBody} }}");
                _sb.AppendLine("  >>");
            }
            else if (hasTab)
                _sb.AppendLine($"  \\new TabStaff {TuningWith(spec)}{{ {tabBody} }}");
            else
                _sb.AppendLine($"  \\new Staff {{ {body} }}");
            _sb.AppendLine("  \\layout {}");
            _sb.AppendLine("}");
            _sb.AppendLine();
        }
    }

    private static string TuningWith(RenderSpec spec)
    {
        var tab = spec.Items.OfType<TabStaffSpec>().FirstOrDefault();
        if (tab == null) return "";
        string t = tab.Tuning switch
        {
            TuningType.Bass => "bass-four-string-tuning",
            TuningType.Bass5 => "bass-five-string-tuning",
            TuningType.Bass6 => "bass-six-string-tuning",
            _ => "guitar-tuning"
        };
        return $"\\with {{ stringTunings = #{t} }} ";
    }

    // ---- helpers ----
    private static string? MetadataValue(SyntaxNode root, string field) =>
        root.DescendantNodes().OfType<MetadataDeclarationSyntax>()
            .FirstOrDefault(m => m.Keyword.Equals(field, StringComparison.OrdinalIgnoreCase))
            ?.StringValue;

    private static int? TempoBpm(TempoDeclarationSyntax t)
    {
        foreach (var i in System.Linq.Enumerable.Range(0, t.SlotCount))
            if (t.GetChild(i) is SyntaxTokenNode tok && int.TryParse(tok.Text, out var n))
                return n;
        return null;
    }

    private IEnumerable<SectionDeclarationSyntax> SectionsInDeclarationOrder(SyntaxNode root) =>
        root.DescendantNodes().OfType<SectionDeclarationSyntax>();

    private static string SafeMark(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()) is { Length: > 0 } v ? v : "Mark";

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static bool IsInsideMusicContent(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is SectionDeclarationSyntax or PhraseDeclarationSyntax) return true;
        return false;
    }

    private static bool IsInsideGrace(SyntaxNode node) => node.IsInside<GraceExpressionSyntax>();

    private static bool IsInsideRepeat(SyntaxNode node) => node.IsInside<FormRepeatBlockSyntax>();

    private static bool IsInsideAlternative(SyntaxNode node) => node.IsInside<FormAlternativeSyntax>();
}

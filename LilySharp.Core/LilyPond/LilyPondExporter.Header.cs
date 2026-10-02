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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.LilyPond;

// The file head of the twin: \version, the fonts plan and its overrides, \header and \paper
// (EmitHeader), the bar-number policy, and the score-level settings (EmitScoreSettings). Split
// out of LilyPondExporter.cs as a partial class (2026-10-02); same instance state, no behavior
// change.
public sealed partial class LilyPondExporter
{
    private static Semantics.LayoutPlan ResolveLayoutPlan(SyntaxNode root, RenderDeclarationSyntax? render)
        => Semantics.LayoutPlanReader.Resolve(root, render);

    /// <summary>
    /// The <c>\Score</c> context lines the plan's <c>barNumbers</c> policy spells in
    /// LilyPond — empty for <c>lines</c>, LilyPond's own default.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:774 <c>\consists Bar_number_engraver</c> (Score) —
    ///   removed for <c>none</c>, so no BarNumber grob is ever made.
    /// LILYPOND-REF: scm/translation-functions.scm:987-988 every-nth-bar-number-visible,
    ///   set as <c>barNumberVisibility</c> for <c>every N</c>; and scm/define-grobs.scm:324
    ///   BarNumber break-visibility = begin-of-line-visible, overridden to
    ///   end-of-line-invisible so a mid-line multiple is printed — the pair
    ///   BarNumberEngraver.Calculate ports.
    /// </remarks>
    private string BarNumberContextLines() => _layoutPlan.BarNumbers.Mode switch
    {
        Semantics.BarNumberMode.None => "      \\remove Bar_number_engraver\n",
        Semantics.BarNumberMode.Every =>
            "      barNumberVisibility = #(every-nth-bar-number-visible "
            + _layoutPlan.BarNumbers.Period.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")\n"
            + "      \\override BarNumber.break-visibility = #end-of-line-invisible\n",
        _ => "",
    };

    private Rendering.TextFontPlan ResolveFontPlan(SyntaxTree tree, SyntaxNode root, RenderDeclarationSyntax? render)
    {
        if (render != null && PageModel(tree, render) is { } page)
            return page.Fonts;
        // No score: the file default, read the way the collector reads it
        // (MeasureCollector.Definitions — the unnamed top-level block).
        var file = root.DescendantNodes<FontDeclarationSyntax>()
            .FirstOrDefault(f => f.NameToken == null && f.IsBlock && !Semantics.FontPlanReader.IsInsideRender(f));
        return file != null ? Semantics.FontPlanReader.Read(file, out _) : Rendering.TextFontPlan.Default;
    }

    /// <summary>
    /// The grobs a text role's <c>step</c> and style reach in the twin, or empty for a role
    /// the twin spells another way (the header markups, the navigation markups) or not at
    /// all (a Lily#-own label with no LilyPond grob).
    /// </summary>
    /// <remarks>
    /// ⚠️ A ROLE IS NOT A GROB, and the map is an approximation stated as one: Lily#'s
    /// <c>mark</c> is both RehearsalMark and SectionLabel, <c>text</c> is TextScript and
    /// TextSpanner, <c>pedal</c> is the two TEXT pedals (the sustain pedal is a glyph run
    /// on both sides and its size does not follow the plan on the page either). The
    /// grob names are LilyPond 2.26's (scm/define-grobs.scm); a role with no entry here is
    /// warned about, not silently dropped.
    /// </remarks>
    private static string[] TwinGrobsOf(Rendering.TextRole role) => role switch
    {
        Rendering.TextRole.Instrument => ["InstrumentName"],
        Rendering.TextRole.LyricText => ["LyricText"],
        Rendering.TextRole.Stanza => ["StanzaNumber"],
        Rendering.TextRole.ChordName => ["ChordName"],
        Rendering.TextRole.FretFrame => ["FretBoard"],
        Rendering.TextRole.FiguredBass => ["BassFigure"],
        Rendering.TextRole.Tempo => ["MetronomeMark"],
        Rendering.TextRole.Mark => ["RehearsalMark", "SectionLabel"],
        Rendering.TextRole.Pedal => ["SostenutoPedal", "UnaCordaPedal"],
        Rendering.TextRole.Text => ["TextScript", "TextSpanner"],
        Rendering.TextRole.Dynamics => ["DynamicText"],
        Rendering.TextRole.PartCombine => ["CombineTextScript"],
        Rendering.TextRole.BarNumber => ["BarNumber"],
        Rendering.TextRole.Fingering => ["Fingering"],
        Rendering.TextRole.Tuplet => ["TupletNumber"],
        Rendering.TextRole.Volta => ["VoltaBracket"],
        Rendering.TextRole.Ottava => ["OttavaBracket"],
        Rendering.TextRole.ClefOctave => ["ClefModifier"],
        Rendering.TextRole.TabFret => ["TabNoteHead"],
        _ => [],
    };

    /// <summary>A number as LilyPond's Scheme reader takes it: <c>#1</c>, <c>#-1</c>, <c>#1.5</c>.</summary>
    private static string LyNumber(double value)
        => "#" + value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The <c>\override</c> lines the plan's <c>step</c> and style attributes become, for
    /// the <c>\Score</c> context of the twin's <c>\layout</c> — one per grob the role
    /// reaches. <c>size</c> is not written: an absolute em in staff spaces has no
    /// LilyPond spelling that composes with the grob's own <c>font-size</c>, which is the
    /// whole reason <c>step</c> is the primary form (HANDOFF §2F F-fonts).
    /// </summary>
    /// <remarks>
    /// The style is written as BOTH <c>font-series</c> and <c>font-shape</c>, because on
    /// the page a written style REPLACES the engraving's (TextFontPlan.StyleOf): a
    /// <c>text bold</c> turns TextScript's italic off, so the twin must say
    /// <c>font-shape = #'upright</c> beside <c>font-series = #'bold</c> or it would draw
    /// bold-italic where Lily# draws bold.
    /// </remarks>
    private string FontOverrideLines()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (role, b) in _fontPlan.SizedOrStyledLeaves())
        {
            string spelling = Rendering.TextRoles.Spelling(role);
            if (b.Size is { } size)
                _warnings.Add($"fonts {spelling} size {size.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} "
                              + "is not exported: an absolute em has no LilyPond spelling in the twin; write step instead");
            // The header and navigation roles are spelled in their markups, not here.
            if (role is Rendering.TextRole.Title or Rendering.TextRole.Composer
                or Rendering.TextRole.Subtitle or Rendering.TextRole.Poet or Rendering.TextRole.Navigation)
                continue;
            var grobs = TwinGrobsOf(role);
            if (grobs.Length == 0)
            {
                if (b.Step != null || b.Style != null)
                    _warnings.Add($"fonts {spelling}: its step/style is not exported — the twin has no LilyPond grob for that label");
                continue;
            }
            foreach (var grob in grobs)
            {
                if (b.Step is { } step)
                    sb.Append("      \\override ").Append(grob).Append(".font-size = ").Append(LyNumber(step)).Append('\n');
                if (b.Style is { } style)
                {
                    bool bold = (style & Rendering.FontStyle.Bold) != 0;
                    bool italic = (style & Rendering.FontStyle.Italic) != 0;
                    sb.Append("      \\override ").Append(grob).Append(".font-series = #'").Append(bold ? "bold" : "medium").Append('\n');
                    sb.Append("      \\override ").Append(grob).Append(".font-shape = #'").Append(italic ? "italic" : "upright").Append('\n');
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// A header or navigation string wrapped in the markup its role's <c>step</c> and
    /// style ask for — <c>\markup { \fontsize #n \normal-text \bold "T" }</c> — or the
    /// plain quoted string when the plan says nothing about the role. <c>\normal-text</c>
    /// first, because a written style replaces the default (bookTitleMarkup's bold, the
    /// navigation markup's italic) rather than adding to it.
    /// </summary>
    private string? MarkupForRole(Rendering.TextRole role, string quotedBody, string defaultStyleCommand)
    {
        // A relative step only — an absolute `size` is warned about in FontOverrideLines
        // and has no markup spelling that composes with the default markup's own size.
        double? step = _fontPlan.WrittenStep(role);
        var style = _fontPlan.WrittenStyle(role);
        if (step is null && style is null)
            return null;
        var sb = new System.Text.StringBuilder("\\markup { ");
        if (step is { } s)
            sb.Append("\\fontsize ").Append(LyNumber(s)).Append(' ');
        if (style is { } st)
        {
            sb.Append("\\normal-text ");
            if ((st & Rendering.FontStyle.Bold) != 0) sb.Append("\\bold ");
            if ((st & Rendering.FontStyle.Italic) != 0) sb.Append("\\italic ");
        }
        else if (defaultStyleCommand.Length > 0)
        {
            sb.Append(defaultStyleCommand).Append(' ');
        }
        sb.Append(quotedBody).Append(" }");
        return sb.ToString();
    }

    /// <summary>
    /// The twin's <c>\version</c> and <c>\header</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE <c>font</c> DIRECTIVE IS DELIBERATELY NOT WRITTEN, decided 2026-08-18 when
    /// the per-role form landed. It has a LilyPond counterpart —
    /// <c>#(define fonts (make-pango-font-tree …))</c> in <c>\paper</c>, plus
    /// <c>font-name</c> per grob — so this is a knowing omission and not one of the
    /// exporter's silent-drop holes.
    /// <para>
    /// The twin exists to be measured against: every LP-fidelity probe compares Lily#'s
    /// geometry with what LilyPond does with the same music. Writing a font tree would
    /// change the widths LilyPond itself computes, so the twin would stop being a control
    /// — and it would change them for a directive that, on the Lily# side, does not move
    /// the layout at all (the reservation stays on the bundled face; see TextFontPlan).
    /// Emitting it would therefore introduce a difference that exists only in the
    /// comparison.
    /// </para>
    /// <para>
    /// ⚠️ What that costs, stated so it is not rediscovered as a defect: a twin rendered
    /// for a score with a <c>font</c> directive shows LilyPond's default text face rather
    /// than the score's. Nothing measures typeface identity, so no probe is blind because
    /// of it — but a human comparing the two side by side will see different letterforms.
    /// </para>
    /// </remarks>
    private void EmitHeader(CompilationUnitSyntax root)
    {
        _sb.Append("\\version \"").Append(LilyPondVersion).Append("\"\n\n");

        // The pin (see PinFonts) goes where a hand puts it in a probe: right after
        // \version, before \header, as the first two lines of the only \paper the twin has.
        if (PinFonts)
            _sb.Append("% Pinned so a -dbackend=svg run measures the faces pdf/png would use;\n")
               .Append("% LilyPond 2.26 drops fonts.serif/sans to generic names under svg only.\n")
               .Append("\\paper {\n")
               .Append("  property-defaults.fonts.serif = \"LilyPond Serif\"\n")
               .Append("  property-defaults.fonts.sans = \"LilyPond Sans Serif\"\n")
               .Append("}\n\n");

        // ⚠️ `paper { }` is NOT exported, and unlike the font omission above this one is
        // a drummap-shaped hole, not a knowing equivalence: paper DOES move Lily#'s
        // layout, so the twin of a book that writes one is laid out on different paper
        // and stops being a control. The warning is the honest state until a probe
        // needs such a twin (no tracked book writes paper{} as of 2026-08-23); the true
        // \paper variables would map 1:1, but the staff-spacing family lives on grobs
        // and contexts in LilyPond and would need \layout overrides, so half an export
        // would be worse than a named hole.
        if (root.DescendantNodes<PaperDeclarationSyntax>().Any())
            _warnings.Add(
                "paper { } is not exported — the twin is laid out on LilyPond's default "
                + "paper, so line and page breaks differ wherever the directive bit");

        var meta = root.DescendantNodes<MetadataDeclarationSyntax>().ToList();
        // The \header fields of the same names, each drawn by ly/titling-init.ly's
        // bookTitleMarkup where Lily#'s HeaderBand puts it.
        (string Field, Rendering.TextRole Role)[] fields =
        [
            ("title", Rendering.TextRole.Title),
            ("subtitle", Rendering.TextRole.Subtitle),
            ("poet", Rendering.TextRole.Poet),
            ("composer", Rendering.TextRole.Composer),
        ];
        bool opened = false;
        foreach (var (field, role) in fields)
        {
            if (MetaString(meta, field) is not { } value)
                continue;
            if (!opened)
            {
                _sb.Append("\\header {\n");
                opened = true;
            }
            // A `fonts { title step … }` / style reaches the header through its markup:
            // \fontsize composes with bookTitleMarkup's own \huge \larger \larger, which is
            // exactly what a STEP means on the page (relative to the role's default em).
            _sb.Append("  ").Append(field).Append(" = ")
               .Append(MarkupForRole(role, "\"" + Escape(value) + "\"", "")
                       ?? "\"" + Escape(value) + "\"")
               .Append('\n');
        }
        // "Capo 3" on the header's instrument line — where the page puts it (HeaderBand;
        // owner's design HANDOFF §2 K2, 2026-09-29).
        if (_layoutPlan.Chords.Capo > 0)
        {
            if (!opened)
            {
                _sb.Append("\\header {\n");
                opened = true;
            }
            _sb.Append("  instrument = \"Capo ").Append(_layoutPlan.Chords.Capo.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\"\n");
        }
        if (opened)
            _sb.Append("}\n\n");
        if (NamesBoth)
            _sb.Append(CapoBothNamer);
    }

    private static string? MetaString(List<MetadataDeclarationSyntax> meta, string keyword)
    {
        foreach (var m in meta)
            if (m.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return m.StringValue;
        return null;
    }

    // ---- Part music variable ----------------------------------------------

    private void EmitScoreSettings(CompilationUnitSyntax root)
    {
        // The accidental style, when the score asks for one that is not LilyPond's own
        // default. It stands at the head of the part's music because `\accidentalStyle` is
        // MUSIC (a context-spec music function, not a context mod), and its own default
        // context is the Staff this variable becomes — which is the scope Lily# keeps its
        // accidental memory in (Semantics.AccidentalStyles' remark).
        // LILYPOND-REF: scm/music-functions.scm:2074-2097 set-accidental-style — the
        //   function \accidentalStyle calls, whose context argument defaults to 'Staff.
        // A book that writes no style writes nothing here, so its twin is unchanged.
        if (_layoutPlan.AccidentalStyle is { } style && style != Semantics.AccidentalStyles.Default)
            _sb.Append("  \\accidentalStyle ").Append(style.LilyPondName).Append('\n');

        // `layout { partCombineText false }` is LilyPond's own property, and a Staff one, so
        // it is set in the music like the style above. A score that keeps the words writes
        // nothing (##t is LilyPond's default).
        // LILYPOND-REF: ly/engraver-init.ly printPartCombineTexts — the Staff property
        //   lily/part-combine-engraver.cc reads before it makes the "a2" / "Solo" text.
        if (!_layoutPlan.PartCombineText)
            _sb.Append("  \\set Staff.printPartCombineTexts = ##f\n");

        // Only the file-level (top-level) settings, in source order.
        foreach (var m in root.Members)
        {
            switch (m)
            {
                case TempoDeclarationSyntax t: _sb.Append("  ").Append(EmitTempo(t)).Append('\n'); break;
                case KeySignatureSyntax k:
                    if (EmitKey(k) is { Length: > 0 } key)   // empty in \drummode
                        _sb.Append("  ").Append(key).Append('\n');
                    break;
                case TimeSignatureSyntax ts: _sb.Append("  ").Append(EmitTime(ts)).Append('\n'); break;
                case PartialDeclarationSyntax p: _sb.Append("  ").Append(ArmPartial(p)).Append('\n'); break;
            }
        }

        // The part header's key (part p { key bes major … }): the page opens the part in it
        // (MeasureCollector.GetPartDefaults). It is DISPLAY only —
        // the phrase auto-transpose ambient stays at the file's home, as the page's does
        // (ResetAmbientTonicToHome reads the file-level key).
        if (_partHeaderKeyNode is { } partKey)
        {
            if (EmitKey(partKey) is { Length: > 0 } key)   // empty in \drummode
                _sb.Append("  ").Append(key).Append('\n');
            _tonic = _homeTonic;
        }
    }

    // ---- Ordered music (flatten sections by form) --------------------------
}

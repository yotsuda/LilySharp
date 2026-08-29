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
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LilySharp.Core.Rendering;
using SkiaSharp;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ========== Completion vocabulary ==========

    // (GetChordQualityCompletions retired with the ':' entry format, 2026-08-23:
    // a chord is written as it prints, so there is no ':' to complete after.)

    /// <summary>
    /// Completions for a <c>structure { … }</c> block: everything a structure body
    /// can hold — the document's section names, the navigation marks (segno / coda /
    /// to coda / D.C. / D.S. …), repeat barlines (<c>|:</c> <c>:|</c>), volta
    /// brackets (<c>[1. …]</c>), the silent-section prefix (<c>~</c>) and custom
    /// text (<c>_"…"</c>). Deliberately offers NO note names — the structure is a
    /// playback order of sections, not music.
    /// </summary>
    internal static CompletionList GetFormCompletions(string text)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();

        // Section names declared anywhere in the document (in declaration order,
        // deduplicated) — these are what a structure plays.
        var sections = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in SectionRefRegex().Matches(text))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name))
                sections.Add(name);
        }
        // Plain reference, then the silent form (~Name = render, no rehearsal
        // label) — one per section, so the ~ prefix is never offered on its own.
        foreach (var name in sections)
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Reference,
                Detail = "Section",
            });
        foreach (var name in sections)
            items.Add(new CompletionItem
            {
                Label = "~" + name,
                InsertText = "~" + name,
                Kind = CompletionItemKind.Reference,
                Detail = "Silent section (renders, no rehearsal label)",
            });

        // Navigation marks placed between sections.
        var navs = new (string Label, string Detail)[]
        {
            ("segno", "Segno (jump target)"),
            ("coda", "Coda (jump target)"),
            ("to coda", "Jump to the coda"),
            ("fine", "End here"),
            ("dc", "Da Capo — repeat from the top"),
            ("ds", "Dal Segno — repeat from the segno"),
            ("dc al fine", "Da Capo al Fine"),
            ("dc al coda", "Da Capo al Coda"),
            ("ds al fine", "Dal Segno al Fine"),
            ("ds al coda", "Dal Segno al Coda"),
        };
        foreach (var (label, detail) in navs)
            items.Add(new CompletionItem
            {
                Label = label,
                Kind = CompletionItemKind.Keyword,
                Detail = detail,
            });

        // Repeat barlines, volta brackets, the silent-section prefix and custom
        // text — the remaining things a structure body can hold.
        items.Add(new CompletionItem
        {
            Label = "|:", InsertText = "|:", Kind = CompletionItemKind.Operator,
            Detail = "Repeat start",
        });
        items.Add(new CompletionItem
        {
            Label = ":|", InsertText = ":|", Kind = CompletionItemKind.Operator,
            Detail = "Repeat end (suffix x3 for a count)",
        });

        CompletionItem Snippet(string label, string insert, string detail) => new()
        {
            Label = label,
            InsertText = insert,
            InsertTextFormat = InsertTextFormat.Snippet,
            Kind = CompletionItemKind.Snippet,
            Detail = detail,
        };
        items.Add(Snippet("[1. ]", "[1. $0]", "1st ending (volta bracket)"));
        items.Add(Snippet("[2. ]", "[2. $0]", "2nd ending (volta bracket)"));
        items.Add(Snippet("[1-2. ]", "[${1:1-2}. $0]", "Multi-pass ending, e.g. [1-2. …] or [1,3. …]"));
        items.Add(Snippet("_\"\"", "_\"$0\"", "Custom text annotation"));

        return new CompletionList { Items = items.ToArray() };
    }

    // Most-reached-for first, which is not alphabetical; SortText preserves it. A property the
    // compiler grows and this array has not heard of sorts to the end rather than vanishing.
    private static readonly string[] PartPropertyOrder =
    {
        "clef", "instrument", "tuning", "octave", "transpose",
        "transposition", "pedal", "removeEmpty",
    };

    // Prose per property, and whether the editor has a VALUE list to enumerate for it.
    // ⚠️ Values must be true only where a value context actually exists — AfterClef,
    // AfterInstrument, AfterRemoveEmpty. It is false for `octave` because the part-header
    // `octave` takes a NUMBER (the AfterOctave context is gated to the top-level directive),
    // and false for `tuning`/`pedal`/`transposition`, which have no value context at
    // all. Setting it re-opens suggestions onto whatever list is general to the position,
    // which is worse than not offering to help.
    // ⚠️ Where a description NAMES a vocabulary it is joined from the compiler's list, not
    // typed out. A description is the one place a wrong word costs nothing to write and is
    // never noticed — `octave` advertised `absolute | relative` here for as long as those words
    // did nothing, and went on advertising them for a day after they became an ERROR.
    // A count is a copy of a list too, so `clef` states its two sizes from the two lists.
    //
    // ★★★ THE RULE these strings obey, and which CompletionVocabularyTests enforces:
    // ANYTHING IN PARENTHESES OR BACKTICKS IS SOMETHING THE WRITER MAY TYPE, and is compiled
    // to prove it. Ordinary prose is free — which is why `octave` mentions absolute/relative in
    // running text and NOT in the parentheses where it used to offer them. The rule is worth
    // the small awkwardness because it is the exact shape the old description broke.
    private static readonly System.Collections.Generic.Dictionary<string, (string Detail, bool Values)>
        PartPropertyDetails = new()
        {
            ["clef"] = ($"Clef — {LanguageVocabulary.PartClefNames.Count} names in a part header, "
                        + $"{LanguageVocabulary.ClefNames.Count} inside music", true),
            ["instrument"] = ("Instrument preset — sets the clef, octave and tuning defaults", true),
            ["tuning"] = ($"Tab tuning ({string.Join("/", LanguageVocabulary.TuningNames)})", false),
            ["octave"] = ("Base octave for this part — a whole number, e.g. `octave 3`. "
                          + "The words absolute and relative belong to the TOP-LEVEL octave "
                          + "directive and a part header refuses them", false),
            ["transpose"] = ("Transpose target pitch, e.g. `transpose d`, `transpose bes,`", false),
            ["transposition"] = ($"Sounding-octave marker ({string.Join("/", LanguageVocabulary.TranspositionMarkers)})", false),
            ["pedal"] = ($"Piano pedal style ({string.Join("/", LanguageVocabulary.PedalStyles)})", false),
            ["removeEmpty"] = ("Hara-kiri: hide this staff in rest-only systems "
                               + $"({string.Join(" | ", LanguageVocabulary.RemoveEmptyValues)})", true),
        };

    /// <summary>The property names a part { } header accepts (bare `name value`
    /// pairs plus inner sections), matching docs/GRAMMAR.md PartProperty.</summary>
    internal static CompletionList GetPartPropertyCompletions()
    {
        // ⚠️ The NAMES come from the compiler (LanguageVocabulary), not from this table. Until
        // 2026-08-19 they came from the table and it had gone wrong in both directions at once:
        // it listed six of the nine properties — `transposition`, `lines` and `pedal` were
        // simply absent, so the editor denied that three properties of the language existed —
        // and it described `octave` as taking `absolute | relative`, two words a part header has
        // never read and has REFUSED since the day before (measured: `part p { octave relative }`
        // is now an error). The list below supplies PROSE for a name; it cannot add or withhold
        // one. A property the compiler grows and this table has not been told about is still
        // offered, without a description.
        //
        // Values = the property takes a value LIST the editor can enumerate — so it is true only
        // where a value context actually exists (AfterClef, AfterInstrument, AfterRemoveEmpty).
        // ⚠️ Setting it for a property with no such context re-opens suggestions onto whatever
        // the general list is, which is worse than not offering to help.
        var props = new System.Collections.Generic.List<(string Label, string? Detail, bool Values)>();
        foreach (string name in LanguageVocabulary.PartPropertiesTakingAValuePair
                     .OrderBy(n => System.Array.IndexOf(PartPropertyOrder, n) is var i && i >= 0
                         ? i : int.MaxValue))
        {
            var d = PartPropertyDetails.TryGetValue(name, out var found) ? found : (null, false);
            props.Add((name, d.Item1, d.Item2));
        }

        // `time` / `tempo` are NOT part properties — they are score-level (every part shares
        // one meter and tempo). They belong at the top level or in a section header, so they
        // are offered there, not here (LYS1026 rejects them in a part header).
        // `section` is not a property at all: it is the OTHER thing a part body holds.
        props.Add(("section", "Inner section (part-major form)", true));
        return new CompletionList
        {
            Items = props.Select((p, i) => new CompletionItem
            {
                Label = p.Label,
                Kind = CompletionItemKind.Property,
                Detail = p.Detail,
                InsertTextFormat = p.Values ? InsertTextFormat.Snippet : default,
                InsertText = p.Values ? $"{p.Label} $0" : null,
                Command = p.Values
                    ? new Command { Title = "Suggest value", CommandIdentifier = "editor.action.triggerSuggest" }
                    : null,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    // Prose per value, in the order a reader wants them (the two hiding modes, then the
    // default). As with ClefDetails, membership of this table decides NOTHING — the words
    // come from the compiler.
    private static readonly string[] RemoveEmptyOrder = { "true", "all", "false" };

    private static readonly System.Collections.Generic.Dictionary<string, string> RemoveEmptyDetails = new()
    {
        ["true"] = "Hide in rest-only systems; the FIRST system keeps the staff (LP RemoveEmptyStaves)",
        ["all"] = "Hide in rest-only systems including the first (LP RemoveAllEmptyStaves)",
        ["false"] = "Never hide (default)",
    };

    /// <summary>
    /// The values valid right after the <c>removeEmpty</c> part property, READ FROM THE
    /// COMPILER. LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves (keeps the first
    /// system) / RemoveAllEmptyStaves.
    /// </summary>
    /// <remarks>
    /// ⚠️ This held its own copy of the three words until 2026-08-19, and the day before that
    /// the compiler began REFUSING anything outside them. A private list was harmless while a
    /// stray word merely fell back to <c>false</c>; the moment the value is enforced, the same
    /// list one word out of date makes the editor propose text the compiler rejects. Nothing
    /// had gone wrong yet — the fix is to the shape, not to a symptom.
    /// ⚠️ The test that guarded this held its own fourth copy of the same three words, so it
    /// would have gone green through the drift it existed to catch.
    /// </remarks>
    internal static CompletionList GetRemoveEmptyCompletions()
    {
        var ordered = LanguageVocabulary.RemoveEmptyValues.OrderBy(
            n => System.Array.IndexOf(RemoveEmptyOrder, n) is var i && i >= 0 ? i : int.MaxValue);

        return new CompletionList
        {
            Items = ordered.Select((name, i) => new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.EnumMember,
                Detail = RemoveEmptyDetails.TryGetValue(name, out var d) ? d : null,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>After <c>title</c> / <c>composer</c>: one snippet that drops a
    /// quote pair and parks the caret inside — the text itself is typed.</summary>
    internal static CompletionList GetTitleTextCompletions(string keyword)
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "\"\"",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "\"$0\"",
                    Detail = keyword == "composer" ? "Quoted composer name" : "Quoted title text",
                },
            ]
        };
    }

    /// <summary>
    /// The completion list offered inside a <c>font "…"</c> string: the installed,
    /// embeddable font families. Computed once and cached — enumerating every installed
    /// family, reading each OS/2 table and probing CJK glyph coverage is not free, and
    /// the set does not change within a process.
    /// </summary>
    private static CompletionList? _fontNameCompletions;

    /// <summary>
    /// The faces a binding may name: the two this engine BUNDLES, then the installed
    /// families that may be embedded into an exported PDF, annotated by license class and
    /// CJK coverage. Offered inside a <c>fonts { … "…" }</c> string.
    /// </summary>
    /// <param name="ownerKey">
    /// The key the string belongs to. <c>serif</c> and <c>sans</c> ask for a SHAPE, so the
    /// list is narrowed to it; every other key (a role or a group) may legitimately name any
    /// face, and gets the whole list.
    /// </param>
    internal static CompletionList GetFontNameCompletions(string ownerKey = "")
        => ownerKey switch
        {
            "serif" => _serifFaceCompletions ??= FacesOfShape(FontEmbedInfo.FaceShape.Serif),
            "sans" => _sansFaceCompletions ??= FacesOfShape(FontEmbedInfo.FaceShape.Sans),
            _ => _fontNameCompletions ??= new CompletionList
            {
                Items = [.. BundledFaceCompletions(),
                         .. BuildFontNameCompletions(EnumerateInstalledEmbeddableFonts()).Items],
            },
        };

    private static CompletionList? _serifFaceCompletions;
    private static CompletionList? _sansFaceCompletions;

    /// <summary>
    /// The faces that draw letters of one shape: the bundled face for that family, then the
    /// installed families the font itself classifies that way, then — in a marked tail — the
    /// ones that classify as nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ THE UNCLASSIFIED ARE KEPT, DELIBERATELY. <see cref="FontEmbedInfo.ShapeOf"/> reads
    /// the font's own OS/2 classification, and a font is free to fill in neither field:
    /// measured 2026-08-18 over 232 installed families, 16 answered nothing — among them
    /// SimSun, a CJK SERIF, and the whole Sitka family. Hiding them would make a real and
    /// wanted face unreachable from the binding that wants it, which is worse than a longer
    /// list; they sort last and say why.
    /// </para>
    /// <para>
    /// Ornamental, script and symbolic families ARE dropped here. They are neither shape,
    /// and a <c>serif</c>/<c>sans</c> binding is a statement about the document's prose. A
    /// score that wants a script face for one role still names it under that role's key,
    /// where the whole list is offered.
    /// </para>
    /// </remarks>
    private static CompletionList FacesOfShape(FontEmbedInfo.FaceShape want)
    {
        var items = new List<CompletionItem>
        {
            want == FontEmbedInfo.FaceShape.Sans
                ? BundledFace(TextFontMetrics.SansFamily, "sans")
                : BundledFace(TextFontMetrics.SerifFamily, "serif"),
        };

        foreach (var item in BuildFontNameCompletions(EnumerateInstalledEmbeddableFonts()).Items)
        {
            var shape = FontEmbedInfo.ShapeOf(item.Label!);
            if (shape == want)
                items.Add(Retiered(item, "0", item.Detail));
            else if (shape == FontEmbedInfo.FaceShape.Unknown)
                items.Add(Retiered(item, "9", item.Detail + " - unclassified, may not be "
                                                          + (want == FontEmbedInfo.FaceShape.Sans ? "sans" : "serif")));
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>The same item, moved into a tier. Sorting is by the WHOLE key, so the tier
    /// goes in front of the sort text the licence/CJK ranking already built.</summary>
    private static CompletionItem Retiered(CompletionItem item, string tier, string? detail) => new()
    {
        Label = item.Label,
        Kind = item.Kind,
        Detail = detail,
        SortText = tier + item.SortText,
    };

    /// <summary>The two faces this engine ships, which no enumeration of INSTALLED families
    /// can contain.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ They were the only faces missing from this list, and they are the only two present
    /// on every machine by construction. Skia enumerates installed families, a bundled face
    /// is shipped rather than installed, and <see cref="BuildFontNameCompletions"/> drops
    /// anything that classifies as <c>NotFound</c> — so the popup offered every face except
    /// the two the completion itself pre-fills a block with.
    /// </para>
    /// <para>
    /// ⚠️ This is the THIRD consumer of one question — "is this face available?". The
    /// metrics path answers it correctly (bundle before machine), the missing-face warning
    /// answered it wrongly until f7e18024, and this list answered it wrongly until now. One
    /// question, three readers, fixed one at a time because nothing looked at the set.
    /// </para>
    /// <para>
    /// They sort ahead of the installed families ("!" precedes every digit ordinally): a
    /// bundled face is the one choice that cannot make the page depend on the machine.
    /// </para>
    /// </remarks>
    private static CompletionItem[] BundledFaceCompletions() =>
    [
        BundledFace(TextFontMetrics.SerifFamily, "serif"),
        BundledFace(TextFontMetrics.SansFamily, "sans"),
    ];

    private static CompletionItem BundledFace(string family, string role) => new()
    {
        Label = family,
        Kind = CompletionItemKind.Value,
        Detail = $"bundled with Lily# - the default {role} face, present on every machine",
        SortText = "!" + role,
    };

    /// <summary>
    /// The body a <c>font</c> declaration is completed with, as an LSP snippet: the two
    /// generic families, each pre-filled with the face that role already uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ THE DEFAULTS ARE THE FACES THE DOCUMENT IS ALREADY IN, so accepting the completion
    /// and changing nothing does not move the page. Measured 2026-08-18: a book with
    /// <c>fonts { serif "TeX Gyre Schola"  sans "TeX Gyre Heros" }</c> and the same book with
    /// no <c>font</c> at all have IDENTICAL geometry — every coordinate, every extent — and
    /// differ only in carrying the <c>font-family</c> attribute explicitly, which the
    /// default omits because the document root already names it. Controls: swapping the two
    /// families, or binding <c>serif</c> alone, does move the page, so the comparison sees a
    /// real binding.
    /// </para>
    /// <para>
    /// ⚠️ The names come from <see cref="TextFontMetrics.SerifFamily"/> and
    /// <see cref="TextFontMetrics.SansFamily"/> rather than being typed here. They are one
    /// quantity, and the editor spelling it a second time is how a popup starts offering a
    /// face the engine stopped bundling.
    /// </para>
    /// <para>
    /// ⚠️ TWO placeholders, not one mirrored placeholder. An earlier draft wrote
    /// <c>${1:face}</c> into both so a single face could be typed once — but the two
    /// families have DIFFERENT defaults, and a mirror cannot carry two. A writer who wants
    /// one face everywhere types it in the first field and tabs to the second; a writer who
    /// wants to change only the prose face edits the first and leaves the second alone,
    /// which the mirror made impossible.
    /// </para>
    /// </remarks>
    private static string FontBlockSnippet(string tail)
        => "{\n  serif \"${1:" + TextFontMetrics.SerifFamily + "}\""
         + "\n  sans  \"${2:" + TextFontMetrics.SansFamily + "}\"" + tail + "\n}";

    /// <summary>
    /// At <c>font |</c> (the keyword typed, nothing after it): the block forms. There is no
    /// quoted item — the one-line <c>font "NAME"</c> was removed 2026-08-18, and an editor
    /// must not complete toward a spelling the parser refuses.
    /// </summary>
    internal static CompletionList GetFontDeclarationCompletions()
        => new()
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "{ … }",
                    FilterText = "fonts",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = FontBlockSnippet("$0"),
                    Preselect = true,
                    SortText = "0",
                    Detail = "Bind the whole document's text (pre-filled with the faces in use)",
                },
                // An empty block, for a writer who wants to bind roles rather than the
                // document — the caret lands where a key goes and the key list opens.
                new CompletionItem
                {
                    Label = "{ }",
                    FilterText = "fonts",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "{\n  $0\n}",
                    SortText = "1",
                    Detail = "Bind faces per text role",
                    Command = new Command { Title = "Suggest role key", CommandIdentifier = "editor.action.triggerSuggest" },
                },
            ]
        };

    /// <summary>
    /// The keys a <c>fonts { }</c> body binds: the two generic families, the six role
    /// groups, and every individual role.
    /// </summary>
    /// <remarks>
    /// ⚠️ The vocabulary is read from <see cref="TextRoles.AllKeySpellings"/> — the ONE home
    /// the reader validates against — and never listed here. A hand-copied key list is the
    /// shape of rot this repo has met repeatedly, most recently in the score-item lists.
    /// <para>
    /// Each key inserts <c>key "…"</c> with the caret inside the quotes and re-triggers
    /// suggestions, so the face list appears without a second keystroke — the same motion
    /// the <c>font</c> keyword itself has.
    /// </para>
    /// </remarks>
    private static CompletionList? _fontBlockCompletions;

    internal static CompletionList GetFontBlockCompletions()
        => _fontBlockCompletions ??= new CompletionList
        {
            Items =
            [
                .. TextRoles.AllKeySpellings().Select(key => new CompletionItem
                {
                    Label = key,
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = key + " \"$0\"",
                    Detail = FontKeyDetail(key),
                    Command = new Command
                    {
                        Title = "Suggest font name",
                        CommandIdentifier = "editor.action.triggerSuggest",
                    },
                }),
                // `embedded` is an entry of the block too, not a key — it subsets every
                // named face into an exported PDF.
                new CompletionItem
                {
                    Label = "embedded",
                    Kind = CompletionItemKind.Keyword,
                    Detail = "Subset every named face into the exported PDF",
                },
            ],
        };

    /// <summary>
    /// At <c>paper |</c> (the keyword typed, nothing after it): the block forms, the
    /// same motion as <c>fonts</c>.
    /// </summary>
    /// <remarks>
    /// ★ THE PRE-FILLED VALUES ARE THE DEFAULTS (a4, 210mm x 297mm), so accepting the
    /// completion and changing nothing does not move the page — the reader's conversion
    /// rounds exactly the way the defaults were computed, and PaperBlockTests pins the
    /// equality.
    /// </remarks>
    internal static CompletionList GetPaperDeclarationCompletions()
        => new()
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "{ … }",
                    FilterText = "paper",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "{\n  paperWidth ${1:210mm}\n  paperHeight ${2:297mm}$0\n}",
                    Preselect = true,
                    SortText = "0",
                    Detail = "Set the page's dimensions (pre-filled with the a4 defaults)",
                },
                new CompletionItem
                {
                    Label = "{ }",
                    FilterText = "paper",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "{\n  $0\n}",
                    SortText = "1",
                    Detail = "Set page dimensions key by key",
                    Command = new Command { Title = "Suggest paper key", CommandIdentifier = "editor.action.triggerSuggest" },
                },
            ]
        };

    /// <summary>
    /// The keys a <c>paper { }</c> body takes: the scalar lengths, the raggedRight
    /// flag, and the nested spacing blocks.
    /// </summary>
    /// <remarks>
    /// ⚠️ The vocabulary is read from <see cref="LanguageVocabulary.PaperScalarKeys"/> /
    /// <see cref="LanguageVocabulary.PaperSpacingKeys"/> — the reader's own table,
    /// published — and never listed here, for the reason the font key list is not.
    /// </remarks>
    private static CompletionList? _paperBlockCompletions;

    internal static CompletionList GetPaperBlockCompletions()
        => _paperBlockCompletions ??= new CompletionList
        {
            Items =
            [
                // `size` first: the one-word way to a whole page. Re-triggers so the
                // size-name list opens at the value position.
                new CompletionItem
                {
                    Label = "size",
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "size $0",
                    Detail = PaperKeyDetail("size"),
                    Command = new Command
                    {
                        Title = "Suggest paper size",
                        CommandIdentifier = "editor.action.triggerSuggest",
                    },
                },
                .. LanguageVocabulary.PaperScalarKeys.Select(key => new CompletionItem
                {
                    Label = key,
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = key + " $0",
                    Detail = PaperKeyDetail(key),
                }),
                new CompletionItem
                {
                    Label = "raggedRight",
                    Kind = CompletionItemKind.Keyword,
                    Detail = "Do not justify lines; measures sit at their ideal width",
                },
                .. LanguageVocabulary.PaperSpacingKeys.Select(key => new CompletionItem
                {
                    Label = key,
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = key + " { $0 }",
                    Detail = PaperKeyDetail(key),
                    Command = new Command
                    {
                        Title = "Suggest spacing sub-key",
                        CommandIdentifier = "editor.action.triggerSuggest",
                    },
                }),
            ],
        };

    /// <summary>The four lines of a nested spacing block.</summary>
    private static CompletionList? _paperSpecBlockCompletions;

    internal static CompletionList GetPaperSpecBlockCompletions()
        => _paperSpecBlockCompletions ??= new CompletionList
        {
            Items =
            [
                .. LanguageVocabulary.PaperSpacingSubKeys.Select(key => new CompletionItem
                {
                    Label = key,
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = key + " $0",
                    Detail = key switch
                    {
                        "basicDistance" => "Ideal distance between the pair (staff spaces)",
                        "minimumDistance" => "Absolute floor, whatever the skylines say",
                        "padding" => "Safety margin beyond the skyline distance",
                        "stretchability" => "Spring flexibility (unitless); larger stretches more",
                        _ => "Spacing sub-key",
                    },
                }),
            ],
        };

    /// <summary>
    /// The paper-size names, offered after <c>size</c>. In the BARE position a name
    /// that carries a space inserts itself QUOTED (the only spelling that can carry
    /// it); inside <c>size "…"</c> every name inserts bare — the quotes are already
    /// around the caret.
    /// </summary>
    internal static CompletionList GetPaperSizeNameCompletions(bool insideString)
        => new()
        {
            Items =
            [
                .. LanguageVocabulary.PaperSizeNames.Select((name, i) => new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Value,
                    InsertText = !insideString && name.Contains(' ') ? "\"" + name + "\"" : name,
                    // Table order, so a4/a5 and b5/jisb5 sit where a reader expects
                    // them rather than alphabetized apart.
                    SortText = i.ToString("D3"),
                    Detail = name == "jisb5"
                        ? "JIS B5, 182 x 257 mm (Lily#-own; ISO b5 is 176 x 250)"
                        : "Paper size (LilyPond's table)",
                }),
            ],
        };

    /// <summary>One line of help per paper key — what the key reaches, and its default.</summary>
    private static string PaperKeyDetail(string key) => key switch
    {
        "size" => "Whole page by name: width, height and scaled margins (size jisb5)",
        "paperWidth" => "Page width (default 210mm, a4). Bare numbers are staff spaces",
        "paperHeight" => "Page height (default 297mm, a4); 0 for one content-driven page",
        "leftMargin" => "Left margin (default 15mm)",
        "rightMargin" => "Right margin (default 15mm)",
        "topMargin" => "Top margin (default 10mm)",
        "bottomMargin" => "Bottom margin (default 10mm)",
        "indent" => "First system's indent (default 0 = from instrument names)",
        "shortIndent" => "Later systems' indent (default 0)",
        "topSystemPadding" => "Padding between the title and the first system",
        "spacingIncrement" => "Horizontal note-spacing unit (default 1.2 staff spaces)",
        "systemSystemSpacing" => "Between two consecutive systems",
        "scoreSystemSpacing" => "After a score boundary, before the next system",
        "markupSystemSpacing" => "After a title or markup, before the next system",
        "scoreMarkupSpacing" => "After a system, before the next title or markup",
        "markupMarkupSpacing" => "Between consecutive titles or markups",
        "topSystemSpacing" => "From the page top to the first system",
        "lastBottomSpacing" => "From the last element to the page bottom",
        "staffStaffSpacing" => "Between two staves of a group",
        "staffGroupStaffSpacing" => "Between a group's staff and the next group's",
        "defaultStaffStaffSpacing" => "Between ungrouped staves",
        "nonStaffRelatedStaffSpacing" => "A lyrics/chord row and the staff it belongs to",
        "nonStaffUnrelatedStaffSpacing" => "A lyrics/chord row and an unrelated staff",
        "nonStaffNonStaffSpacing" => "Between two lyrics/chord rows",
        _ => "Paper key",
    };

    /// <summary>One line of help per key, so the popup says what the key REACHES rather
    /// than only repeating its spelling.</summary>
    private static string FontKeyDetail(string key) => key switch
    {
        "serif" => "Generic family: everything except chord symbols falls back here",
        "sans" => "Generic family: chord symbols fall back here",
        "header" => "Group: title, composer, instrument names",
        "lyrics" => "Group: lyric syllables and stanza numbers",
        "chords" => "Group: chord symbols, diagrams, figured bass",
        "marks" => "Group: tempo, rehearsal marks, pedal, navigation, free text, dynamics",
        "numbers" => "Group: bar numbers, fingerings, tuplet / volta / ottava labels",
        "notation" => "Group: text that is really notation — the treble_8 digit, a "
                      + "compound meter's +, tab fret numbers. Reached ONLY when named",
        _ => "Text role",
    };

    /// <summary>
    /// After a key (<c>fonts { lyricText |</c>): the values THAT KEY takes.
    /// </summary>
    /// <param name="key">The key the caret sits after. A generic family narrows the list.</param>
    /// <remarks>
    /// <para>
    /// A role or a group takes a quoted face, or a generic family to FOLLOW instead
    /// (<c>chordName serif</c>). A GENERIC FAMILY takes only quoted names: pointing
    /// <c>serif</c> at <c>sans</c> is a re-classification and no role reads it, which
    /// <c>FontPlanReader</c> refuses with LYS8006 — "a generic family takes quoted face
    /// names, not another family".
    /// </para>
    /// <para>
    /// ⚠️ The list was flat until 2026-08-18 and offered the redirect after EVERY key, so
    /// at <c>fonts { serif |</c> the popup proposed exactly the two words the reader was
    /// about to refuse. The reader's own message even says the offer must not be made
    /// there — it "must not offer the family form the other keys accept" — and the editor
    /// made it anyway, because the value list did not know which key it was answering for.
    /// </para>
    /// <para>
    /// The quoted item comes first and is preselected, so the common motion (name a face)
    /// stays one keystroke; the redirect is a deliberate second choice.
    /// </para>
    /// </remarks>
    private static CompletionList? _fontValuesForRole;
    private static CompletionList? _fontValuesForFamily;

    internal static CompletionList GetFontRoleValueCompletions(string key = "")
    {
        var quoted = new CompletionItem
        {
            Label = "\"…\"",
            Kind = CompletionItemKind.Snippet,
            InsertTextFormat = InsertTextFormat.Snippet,
            InsertText = "\"$0\"",
            Preselect = true,
            SortText = "0",
            Detail = "Pick a bundled or installed, embeddable font",
            Command = new Command
            {
                Title = "Suggest font name",
                CommandIdentifier = "editor.action.triggerSuggest",
            },
        };

        // Is the key a generic family? Asked of TextRoles, the one home that decides it, so
        // a family added there needs no second edit here.
        bool isFamily = TextRoles.TryParseKey(key, out _, out _, out var family) && family != null;
        if (isFamily)
            return _fontValuesForFamily ??= new CompletionList { Items = [quoted] };

        return _fontValuesForRole ??= new CompletionList
        {
            Items =
            [
                quoted,
                new CompletionItem
                {
                    Label = "serif",
                    Kind = CompletionItemKind.Value,
                    SortText = "1",
                    Detail = "Follow whatever the serif family is bound to",
                },
                new CompletionItem
                {
                    Label = "sans",
                    Kind = CompletionItemKind.Value,
                    SortText = "1",
                    Detail = "Follow whatever the sans family is bound to",
                },
            ],
        };
    }

    /// <summary>
    /// Enumerates the installed font families and, for the embeddable ones (class
    /// <see cref="FontEmbedInfo.FontEmbedClass.Free"/> or
    /// <see cref="FontEmbedInfo.FontEmbedClass.Gray"/>), yields the family, its class,
    /// and whether it covers Japanese. Every SkiaSharp call is guarded so a font that
    /// fails to load or classify is simply skipped, never thrown out of completion.
    /// </summary>
    private static IEnumerable<(string Family, FontEmbedInfo.FontEmbedClass Cls, bool Cjk)>
        EnumerateInstalledEmbeddableFonts()
    {
        var result = new List<(string, FontEmbedInfo.FontEmbedClass, bool)>();
        string[] families;
        try
        {
            families = SKFontManager.Default.FontFamilies.ToArray();
        }
        catch
        {
            return result; // no font manager — offer nothing rather than throw
        }
        foreach (var family in families)
        {
            if (string.IsNullOrWhiteSpace(family))
                continue;
            try
            {
                var cls = FontEmbedInfo.Classify(family);
                if (cls is not (FontEmbedInfo.FontEmbedClass.Free or FontEmbedInfo.FontEmbedClass.Gray))
                    continue; // not installed-and-embeddable (Forbidden / NotFound)
                // Does the family cover Japanese? Probe 'か' (Hiragana KA, U+304B) —
                // a zero glyph id means the codepoint is not covered.
                bool cjk = false;
                var tf = SKTypeface.FromFamilyName(family);
                if (tf != null)
                    cjk = tf.GetGlyph(0x304B) != 0;
                result.Add((family, cls, cjk));
            }
            catch
            {
                // A font that fails to load / probe is skipped.
            }
        }
        return result;
    }

    /// <summary>
    /// Builds the <c>font "…"</c> completion items from a classified family list.
    /// Split from the system enumeration so it is unit-testable with a synthetic list.
    /// Keeps only the embeddable classes (<see cref="FontEmbedInfo.FontEmbedClass.Free"/>
    /// / <see cref="FontEmbedInfo.FontEmbedClass.Gray"/>); each item's detail states the
    /// license class and notes CJK coverage; the sort key floats Free before Gray and,
    /// within a class, CJK-capable families first.
    /// </summary>
    internal static CompletionList BuildFontNameCompletions(
        IEnumerable<(string Family, FontEmbedInfo.FontEmbedClass Cls, bool Cjk)> fonts)
    {
        var items = new List<CompletionItem>();
        foreach (var (family, cls, cjk) in fonts)
        {
            if (cls is not (FontEmbedInfo.FontEmbedClass.Free or FontEmbedInfo.FontEmbedClass.Gray))
                continue; // Forbidden (fsType blocks embedding) / NotFound — never offered
            // A family the bundle shadows is never offered off the machine: the engine
            // measures and draws these names from the bundled files no matter what is
            // installed (TextFontMetrics consults the bundle before the machine), so the
            // installed row would advertise a face the engine will silently not use — and
            // on a machine that installs TeX Gyre the same name appeared twice, with the
            // system row carrying the classification and the sort.
            if (TextFontMetrics.IsBundledFamilyName(family))
                continue;
            string detail = cls == FontEmbedInfo.FontEmbedClass.Free
                ? "embeddable (OFL/libre)"
                : "embeddable - license unverified";
            if (cjk)
                detail += " - CJK";
            items.Add(new CompletionItem
            {
                Label = family,
                Kind = CompletionItemKind.Value,
                Detail = detail,
                // Free before Gray; within a class, CJK-capable first; then by name.
                SortText = (cls == FontEmbedInfo.FontEmbedClass.Free ? "0" : "1")
                    + (cjk ? "0" : "1") + family,
            });
        }
        return new CompletionList { Items = items.ToArray() };
    }

    // The written tempo forms — a bare BPM, a marking text, a beat-unit equation, or a
    // swing feel. Completing the `tempo` keyword re-opens suggestions (Command) so these
    // forms enumerate right after it; the Insert holds each form's placeholder snippet.
    private static readonly (string Label, string Insert, string Detail)[] TempoForms =
    {
        ("120", "${1:120}", "Metronome mark: ♩ = 120"),
        ("\"Allegro\" 132", "\"${1:Allegro}\" ${2:132}", "Marking text + BPM: Allegro (♩ = 132)"),
        ("\"Grave\" 4 = 54", "\"${1:Grave}\" ${2:4} = ${3:54}", "Marking + beat unit = BPM (4. = dotted unit)"),
        ("120 swing", "${1:120} swing", "Swing feel (eighths; 'swing 16' for sixteenths)"),
    };

    /// <summary>The written tempo forms, as fill-in snippets — after <c>tempo</c>
    /// nothing else fits (a bare BPM, a marking text, a beat-unit equation, or
    /// a swing feel).</summary>
    internal static CompletionList GetTempoCompletions()
    {
        return new CompletionList
        {
            Items = TempoForms.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Snippet,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = t.Insert,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>Common meters offered after <c>time</c>.</summary>
    internal static CompletionList GetTimeCompletions()
    {
        var meters = new (string Label, string Detail)[]
        {
            ("4/4", "Common time (engraved as C)"),
            ("3/4", "Waltz / minuet"),
            ("2/4", "March / polka"),
            ("2/2", "Cut time (engraved as ¢)"),
            ("6/8", "Compound duple (jig)"),
            ("9/8", "Compound triple (slip jig)"),
            ("12/8", "Compound quadruple (shuffle)"),
            ("3/8", "Fast triple"),
            ("5/4", "Quintuple"),
            ("7/8", "Septuple"),
        };
        return new CompletionList
        {
            Items = meters.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = t.Detail,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>Common pickup lengths offered after <c>partial</c> (the note-
    /// duration grammar: number + optional dots).</summary>
    internal static CompletionList GetPartialCompletions()
    {
        var durations = new (string Label, string Detail)[]
        {
            ("4", "Quarter-note pickup"),
            ("8", "Eighth-note pickup"),
            ("2", "Half-note pickup"),
            ("4.", "Dotted-quarter pickup"),
            ("2.", "Dotted-half pickup (three quarters)"),
            ("8.", "Dotted-eighth pickup"),
        };
        return new CompletionList
        {
            Items = durations.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>The render-spec keywords valid inside a score / grandStaff body.</summary>
    internal static CompletionList GetScoreBlockCompletions()
    {
        // Retrigger = the item takes a part-name reference next, so re-open the
        // completion popup after inserting the keyword and list the declared parts.
        // A keyword that opens a BRACE body does not retrigger — the caret lands
        // inside the block, where the next completion request answers on its own.
        // ⚠️ Every keyword ParseRenderItem accepts belongs here. The four staff
        // GROUPS were missing, so the one list the writer sees inside `score { }`
        // did not mention the constructs the parser has always taken.
        var specs = new (string Label, string Insert, string Detail, bool Retrigger)[]
        {
            ("staff", "staff $0", "A staff rendering the named part", true),
            ("grandStaff", "grandStaff {\n\t$0\n}", "Braced staff group (piano)", false),
            ("staffGroup", "staffGroup {\n\t$0\n}", "Bracketed staff group (orchestral family)", false),
            ("choirStaff", "choirStaff {\n\t$0\n}", "Choir staff group (vocal ensemble)", false),
            ("condensedStaff", "condensedStaff {\n\t$0\n}",
                "One staff carrying several parts as voices — bare part names inside", false),
            ("combinedStaff", "combinedStaff {\n\t$0\n}",
                "Two parts merged onto one staff, a2 where they agree — bare part names inside", false),
            ("tab", "tab $0", "A tablature staff for the named part", true),
            ("ossia", "ossia $0", "An ossia staff (small alternative reading) for the named part", true),
            ("chords", "chords $0", "Chord row (no staff) for the named chord part", true),
            ("lyrics", "lyrics $0", "Lyrics row (no staff) for the named lyrics part", true),
            ("title", "title \"$0\"", "This score's own title, overriding the file's", false),
            ("composer", "composer \"$0\"", "This score's own composer, overriding the file's", false),
            ("fonts", "fonts $0", "This score's faces: reference a named top-level fonts block", true),
            ("paper", "paper $0", "This score's page: reference a named top-level paper block", true),
        };
        return new CompletionList
        {
            Items = specs.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = t.Insert,
                Detail = t.Detail,
                SortText = i.ToString(),
                Command = t.Retrigger
                    ? new Command { Title = "Suggest part name", CommandIdentifier = "editor.action.triggerSuggest" }
                    : null,
            }).ToArray()
        };
    }

    /// <summary>
    /// Inside <c>grandStaff</c> / <c>staffGroup</c> / <c>choirStaff</c>: the body is a
    /// run of <c>staff</c> items with <c>lyrics NAME</c> rows between them (a bound row
    /// is the staff above's verse — LYS6012 refuses any other), so that is the whole
    /// list. Anything else is LYS6011.
    /// </summary>
    internal static CompletionList GetStaffGroupBlockCompletions() => new()
    {
        Items =
        [
            new CompletionItem
            {
                Label = "staff",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "staff $0",
                Detail = "A staff of this group, rendering the named part",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest part name",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
            new CompletionItem
            {
                Label = "lyrics",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "lyrics $0",
                Detail = "A verse row under the staff above (the track must sing that staff's part)",
                SortText = "1",
                Command = new Command
                {
                    Title = "Suggest lyrics name",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        ]
    };

    /// <summary>After <c>staff NAME</c> / <c>ossia NAME</c>: the <c>as lines N</c>
    /// staff-line selector, then the ordinary render-item continuations so a
    /// following staff/chords/lyrics is not blocked — the same shape as
    /// <see cref="GetChordAttachNameCompletions"/>. The count moved OFF the part
    /// header (2026-08-19): it is a property of THIS rendering, so the same part
    /// can print five-lined in the full score and one-lined in a lead sheet.
    /// </summary>
    internal static CompletionList GetStaffAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem>
        {
            new CompletionItem
            {
                Label = "as lines",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "as lines $0",
                Detail = "Staff-line count for this staff - 1 is a one-line rhythm staff",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest line count",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        };
        // The next render item can also start here; keep those, sorted after.
        foreach (var it in GetScoreBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>After <c>staff NAME</c> INSIDE a staff group: the
    /// <c>as lines N</c> selector, then the group's own narrow continuations
    /// (<c>staff</c> / <c>lyrics</c> — a group refuses the wider score list,
    /// LYS6011), the group-body sibling of
    /// <see cref="GetStaffAttachNameCompletions"/>.</summary>
    internal static CompletionList GetGroupStaffAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem>
        {
            new CompletionItem
            {
                Label = "as lines",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "as lines $0",
                Detail = "Staff-line count for this staff - 1 is a one-line rhythm staff",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest line count",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        };
        foreach (var it in GetStaffGroupBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>The <c>sings</c> keyword item a lyrics ROW offers after its track
    /// name — the row spelling of the binding the definition states
    /// (<c>lyrics verse sings melody</c>). Shared by the score-body and
    /// group-body row contexts.</summary>
    private static CompletionItem RowSingsItem() => new()
    {
        Label = "sings",
        Kind = CompletionItemKind.Keyword,
        InsertTextFormat = InsertTextFormat.Snippet,
        InsertText = "sings $0",
        Detail = "Bind this track to the part it sings - the same binding the definition states",
        SortText = "0",
        Command = new Command
        {
            Title = "Suggest part name",
            CommandIdentifier = "editor.action.triggerSuggest",
        },
    };

    /// <summary>After <c>lyrics NAME</c> on a SCORE row: the <c>sings</c>
    /// binding, then the score's normal continuations — the lyrics-row sibling
    /// of <see cref="GetStaffAttachNameCompletions"/>.</summary>
    internal static CompletionList GetLyricsRowAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem> { RowSingsItem() };
        foreach (var it in GetScoreBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>After <c>lyrics NAME</c> INSIDE a staff group: the <c>sings</c>
    /// binding, then the group's own narrow continuations (never the score-wide
    /// list — LYS6011), the group-body sibling of
    /// <see cref="GetLyricsRowAttachNameCompletions"/>.</summary>
    internal static CompletionList GetGroupLyricsRowAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem> { RowSingsItem() };
        foreach (var it in GetStaffGroupBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>After <c>staff NAME as</c> / <c>ossia NAME as</c>: the one
    /// selector a staff takes — <c>lines</c>. The value is enumerated by the
    /// retrigger (<see cref="GetStaffLinesValueCompletions"/>).</summary>
    internal static CompletionList GetStaffLinesSelectorCompletions() => new()
    {
        Items =
        [
            new CompletionItem
            {
                Label = "lines",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "lines $0",
                Detail = "Staff-line count for this staff - 1 is a one-line rhythm staff",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest line count",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        ]
    };

    /// <summary>The staff-line counts, offered in the value slot of
    /// <c>as lines</c>. The range is the compiler's
    /// (<see cref="LanguageVocabulary.MinStaffLines"/>), never restated.</summary>
    internal static CompletionList GetStaffLinesValueCompletions() => new()
    {
        Items = System.Linq.Enumerable.Range(
                LanguageVocabulary.MinStaffLines,
                LanguageVocabulary.MaxStaffLines - LanguageVocabulary.MinStaffLines + 1)
            .Select(n => new CompletionItem
            {
                Label = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Kind = CompletionItemKind.Value,
                InsertText = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Detail = n == 1 ? "A one-line rhythm or percussion staff"
                    : n == LanguageVocabulary.MaxStaffLines ? $"{n} lines - the default"
                    : $"{n} lines",
                SortText = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }).ToArray()
    };

    /// <summary>After <c>chords NAME</c>: the chord DISPLAY
    /// selector (<c>as roman | as names</c>), then the ordinary render-item
    /// continuations so a following <c>staff</c>/<c>chords</c>/… is not blocked.</summary>
    /// <remarks>
    /// ⚠️ <c>as both</c> is NOT offered — it was retired 2026-08-23. To show a track both
    /// ways the writer places it twice, and the render-item continuations kept below are
    /// what makes that reachable from here: the next <c>chords</c> is one of them.
    /// </remarks>
    internal static CompletionList GetChordAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem>
        {
            AsItem("as roman", "Show chord symbols as Roman-numeral degrees (I, IIm7, V7)", "0"),
            AsItem("as names", "Show absolute chord names (C, Am7) — the default", "1"),
        };
        // The next render item can also start here; keep those, sorted after `as …`.
        foreach (var it in GetScoreBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };

        static CompletionItem AsItem(string label, string detail, string sort) => new()
        {
            Label = label,
            Kind = CompletionItemKind.Keyword,
            InsertText = label,
            Detail = detail,
            SortText = sort,
        };
    }

    /// <summary>After <c>… as</c>: the two chord display modes.</summary>
    internal static CompletionList GetChordDisplayModeCompletions()
    {
        var modes = new (string Label, string Detail)[]
        {
            ("roman", "Roman-numeral degrees for the key (I, IIm7, V7)"),
            ("names", "Absolute chord names (C, Am7)"),
        };
        return new CompletionList
        {
            Items = modes.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Keyword,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>After <c>tab … as</c>: the two tab display styles.</summary>
    /// <remarks>
    /// ⚠️ THE LABELS COME FROM THE COMPILER (<c>LanguageVocabulary.TabStyles</c>) and only the
    /// prose is written here. Until 2026-08-24 this list was the ONLY enumeration of the two
    /// words in the tree — the compiler tested <c>== "numbers"</c> and accepted anything else
    /// — so it could not drift, because there was nothing to drift from. Now that the
    /// compiler refuses an unknown style, an editor holding its own copy would be the
    /// "suggests a word the compiler rejects" defect that shipped twice in session 240.
    /// </remarks>
    internal static CompletionList GetTabDisplayModeCompletions()
    {
        var detail = new Dictionary<string, string>
        {
            ["numbers"] = "Fret digits only — no stems, dots or rests",
            ["full"] = "Full tablature staff with stems, dots and rests (the default)",
        };
        return new CompletionList
        {
            Items = LilySharp.Core.Semantics.LanguageVocabulary.TabStyles
                .Select((label, i) => new CompletionItem
                {
                    Label = label,
                    Kind = CompletionItemKind.Keyword,
                    Detail = detail.TryGetValue(label, out var d) ? d : null,
                    SortText = i.ToString(),
                }).ToArray()
        };
    }

    /// <summary>Names declared as <c>KEYWORD name {</c> anywhere in the document
    /// (parts, chord parts, lyrics parts), offered where a score references them.</summary>
    internal static CompletionList GetDeclaredNameCompletions(string text, string keyword, string detail)
    {
        var names = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in DeclaredNameRegex().Matches(text))
        {
            if (m.Groups[1].Value != keyword) continue;
            var name = m.Groups[2].Value;
            if (seen.Add(name))
                names.Add(name);
        }
        return new CompletionList
        {
            Items = names.Select((n, i) => new CompletionItem
            {
                Label = n,
                Kind = CompletionItemKind.Reference,
                Detail = detail,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>After <c>lyrics NAME</c> at a definition site: the binding
    /// keyword. <c>lyrics ja sings vocal { }</c> states at the DEFINITION which
    /// melody the track sings (a property of the track NAME, stated once; later
    /// same-name blocks may repeat it identically or omit it). A score then only
    /// PLACES the row — under the staff it sings, it is that staff's verse;
    /// anywhere else, words-only at the melody's rhythm.</summary>
    internal static CompletionList GetLyricsTrackNameCompletions() => new()
    {
        Items =
        [
            new CompletionItem
            {
                Label = "sings",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "sings $0",
                Detail = "Bind this track to the part it sings - a row under that staff is its verse",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest part name",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        ]
    };

    /// <summary>
    /// The names a <c>lyrics</c> track's optional voice-binding name can align to — the
    /// declared parts (<c>part NAME { … }</c>, the usual target) and any explicitly named
    /// voices (<c>voice NAME { … }</c>) — deduplicated, parts first.
    /// </summary>
    internal static CompletionList GetVoiceBindingNameCompletions(string text,
        string detail = "Voice / part to align the lyrics to")
    {
        var items = new System.Collections.Generic.List<CompletionItem>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in new[] { "part", "voice" })
            foreach (Match m in DeclaredNameRegex().Matches(text))
            {
                if (m.Groups[1].Value != keyword) continue;
                var name = m.Groups[2].Value;
                if (seen.Add(name))
                    items.Add(new CompletionItem
                    {
                        Label = name,
                        Kind = CompletionItemKind.Reference,
                        Detail = detail,
                        SortText = seen.Count.ToString("D2"),
                    });
            }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>
    /// After <c>section </c>, the section names known to the piece but not yet declared
    /// in this scope — so a section can be filled in with what is still missing. In a
    /// <c>part { }</c> / <c>lyrics { }</c> container the missing set is measured against
    /// the sections already in that container (part-major: <c>bass</c> already has
    /// <c>A</c>, so only <c>B</c> / <c>C</c> are offered); at the top level (section-major)
    /// it is measured against every declared section, so what remains is the sections the
    /// <c>form { }</c> references but that have not been written yet. The universe is every
    /// section NAME the document mentions — declarations AND form references (incl.
    /// <c>~silent</c> and volta alternatives). Picking one drops in the <c>{ }</c> body
    /// with the caret inside, unless a <c>{</c> already follows (then just the name is
    /// inserted). A brand-new name is typed freely; the list never blocks it.
    /// </summary>
    internal static CompletionList GetMissingSectionNameCompletions(string text, int offset)
    {
        // Every section NAME the document mentions — declaration names PLUS form
        // references (a section the piece plays but you may not have written yet) — in
        // document order, deduplicated. The parser resolves references robustly (bare,
        // ~silent, and [1. NAME] volta alternatives), which a text scan cannot.
        var known = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var root = SyntaxTree.Parse(text).GetRoot();
        foreach (var tok in SectionReferenceFinder.AllSectionNameTokens(root))
            if (seen.Add(tok.Text))
                known.Add(tok.Text);

        // What already fills this scope: inside a container, that container's sections;
        // at the top level, every declared section (so only form-only names remain).
        var here = IsInsideSectionContainer(text, offset)
            ? SectionsDeclaredInCurrentBlock(text, offset)
            : AllDeclaredSections(text, offset);

        // Completing the name opens the `{ }` body with the caret inside — UNLESS a `{`
        // already follows (the user is naming an existing braced section), in which case
        // just the name is inserted so no second body appears.
        bool hasBrace = SectionNameIsFollowedByBrace(text, offset);
        return new CompletionList
        {
            Items = known.Where(n => !here.Contains(n)).Select((n, i) => new CompletionItem
            {
                Label = n,
                Kind = CompletionItemKind.Reference,
                Detail = "Section not yet declared here",
                InsertTextFormat = hasBrace ? default : InsertTextFormat.Snippet,
                InsertText = hasBrace ? n : n + " {\n\t$0\n}",
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>
    /// Completions offered DIRECTLY inside a top-level <c>lyrics [name] { }</c> track: the
    /// document's section names not yet present in this track, each scaffolding a full
    /// <c>section NAME { … }</c> entry (a section-major lyrics track holds
    /// <c>section NAME { syllables }</c>). Unlike <see cref="GetMissingSectionNameCompletions"/>
    /// — offered AFTER the user types <c>section</c> — this fires before it, so the insert
    /// carries the <c>section</c> keyword. The grammar still allows a bare syllable stream
    /// here; this list is opt-in (Ctrl+Space) and never blocks typing lyrics.
    /// </summary>
    internal static CompletionList GetLyricsSectionCompletions(string text, int offset)
        => new() { Items = SectionScaffoldItems(text, offset, "Lyrics for this section").ToArray() };

    /// <summary>
    /// Section-name scaffold items — label <c>section NAME</c>, insert <c>section NAME { }</c>
    /// with the caret in the body — for the document's sections not yet present in the block
    /// at <paramref name="offset"/>. Shared by a top-level <c>lyrics { }</c> track and a
    /// <c>part { }</c> body, which both hold <c>section NAME { … }</c> entries. The
    /// <c>section</c> keyword is part of the LABEL (so the picker reads as <c>section A</c>,
    /// matching what is inserted), and sorts after a part's property list.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<CompletionItem> SectionScaffoldItems(
        string text, int offset, string detail, string nest = "\t", bool includeNewSection = true)
    {
        var known = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var root = SyntaxTree.Parse(text).GetRoot();
        foreach (var tok in SectionReferenceFinder.AllSectionNameTokens(root))
            if (seen.Add(tok.Text))
                known.Add(tok.Text);

        // Sections already written in THIS block are dropped, so the list is what is still
        // missing (the same measure as the after-`section` completion).
        var here = SectionsDeclaredInCurrentBlock(text, offset);
        bool hasBrace = SectionNameIsFollowedByBrace(text, offset);

        // Indent so the section never lands inline: on a fresh (whitespace-only) line the
        // plain snippet inherits that line's indent; when the caret sits after content (e.g.
        // right after `part melody {`), force the section onto its OWN new line one level
        // deeper (VS Code prepends the current line's indent to every following snippet line).
        bool freshLine = LineIsBlankBefore(text, WordStartBefore(text, offset));
        // head = the `section NAME` (or `section $1` for a new name); the tail is the body.
        // `nest` is one indent level for the enclosing block ("\t" inside part/lyrics; "" at
        // the top level, where a section sits at column 0).
        string Body(string head) => hasBrace ? head
            : freshLine ? head + " {\n\t$0\n}"
            : "\n" + nest + head + " {\n" + nest + "\t$0\n" + nest + "}";

        var items = known.Where(n => !here.Contains(n)).Select((n, i) => new CompletionItem
        {
            Label = "section " + n,
            Kind = CompletionItemKind.Reference,
            Detail = detail,
            InsertTextFormat = InsertTextFormat.Snippet,
            InsertText = Body("section " + n),
            SortText = "z" + i.ToString("D2"), // after a part { } body's properties (00..09)
        }).ToList();

        // A brand-NEW section: `section {}` with the caret first BETWEEN `section` and `{`
        // (the $1 name stop), then Tab drops into the body ($0). Offered even when every
        // known section is already present, so a fresh name is always one pick away. Excluded
        // where another `section` entry already exists (the top-level keyword list has one).
        if (includeNewSection)
            items.Add(new CompletionItem
            {
                Label = "section",
                Kind = CompletionItemKind.Keyword,
                Detail = "New section",
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = Body("section $1"),
                SortText = "zzz", // after the named scaffolds
            });
        return items;
    }

    /// <summary>The index where the identifier word at <paramref name="offset"/> begins
    /// (letters/digits), so indentation is judged from the line content BEFORE the partial
    /// word the completion will replace.</summary>
    private static int WordStartBefore(string text, int offset)
    {
        int i = offset;
        while (i > 0 && char.IsLetterOrDigit(text[i - 1])) i--;
        return i;
    }

    /// <summary>True when everything from the start of the line to <paramref name="pos"/> is
    /// whitespace — i.e. the caret is on its own (already-indented) line.</summary>
    private static bool LineIsBlankBefore(string text, int pos)
    {
        for (int i = pos - 1; i >= 0 && text[i] != '\n'; i--)
            if (!char.IsWhiteSpace(text[i])) return false;
        return true;
    }

    /// <summary>A <c>part { }</c> body's completions: its property names PLUS the document's
    /// section names as <c>section NAME { }</c> scaffolds — a part-major part holds properties
    /// AND inner sections. The bare <c>section</c> property is dropped: the scaffolds (and the
    /// "New section" item) are the one-step way in, so it would be a redundant second entry.</summary>
    internal static CompletionList GetPartBlockCompletions(string text, int offset)
    {
        var items = GetPartPropertyCompletions().Items
            .Where(p => p.Label != "section")
            .ToList();
        items.AddRange(SectionScaffoldItems(text, offset, "Section"));
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>Completions offered DIRECTLY inside a top-level <c>section { }</c> in a doc
    /// WITH parts: the declared part names as <c>NAME { }</c> cell scaffolds. A section-major
    /// section's body holds part blocks (<c>melody { … }</c>), not notes — so this replaces
    /// the pitch-letter list there. A section sits at column 0, so a cell nests one level in.</summary>
    internal static CompletionList GetSectionBlockCompletions(string text, int offset)
    {
        // In a PART-MAJOR file the music lives in `part X { section A { … } }`, so a top-level
        // `section A { }` is a standalone HEADER: it carries section-wide directives (a pickup,
        // key, time, tempo, a section-scoped grob override) that apply to every part of the
        // section — never part cells. Offer those directives, not part names.
        if (LilySharp.Core.Editing.PartSectionLayoutConverter.Detect(SyntaxTree.Parse(text).GetRoot())
            == LilySharp.Core.Editing.LayoutForm.PartMajor)
            return new CompletionList { Items = SectionHeaderDirectiveItems() };

        // Section-major (or a parts file not yet committed to a layout): the section body holds
        // one music cell per part. Offer the declared part names as `NAME { }` cell scaffolds.
        var parts = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in DeclaredNameRegex().Matches(text))
            if (m.Groups[1].Value == "part" && seen.Add(m.Groups[2].Value))
                parts.Add(m.Groups[2].Value);

        bool freshLine = LineIsBlankBefore(text, WordStartBefore(text, offset));
        string Body(string head) => freshLine ? head + " {\n\t$0\n}" : "\n\t" + head + " {\n\t\t$0\n\t}";
        return new CompletionList
        {
            Items = parts.Select((n, i) => new CompletionItem
            {
                Label = n,
                Kind = CompletionItemKind.Reference,
                Detail = "Part cell — this section's music for " + n,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = Body(n),
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The directives a top-level section HEADER may carry in a part-major file: a
    /// pickup and the section-wide key / time / tempo, plus a section-scoped grob override.
    /// They apply to every part of the section; clef is deliberately absent (it is per-part).</summary>
    private static CompletionItem[] SectionHeaderDirectiveItems() => new[]
    {
        new CompletionItem { Label = "partial", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "partial $0", Detail = "Pickup — shorten this section's first bar (applies to every part)" },
        new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "This section's key signature", Command = new Command { Title = "Suggest key tonic", CommandIdentifier = "editor.action.triggerSuggest" } },
        new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "This section's time signature", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
        new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tempo $0", Detail = "This section's tempo (BPM)" },
        new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $0", Detail = "Grob override — a default for this section on every staff", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } },
    };

    /// <summary>
    /// The section names already declared in the <c>part { }</c> / <c>lyrics { }</c>
    /// block that encloses <paramref name="offset"/> — EXCLUDING the (possibly
    /// incomplete) <c>section</c> declaration at the cursor itself, so the name being
    /// typed is never filtered out of <see cref="GetMissingSectionNameCompletions"/>.
    /// </summary>
    private static System.Collections.Generic.HashSet<string> SectionsDeclaredInCurrentBlock(string text, int offset)
    {
        var declared = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var mask = CodeMask(text, text.Length);

        // The innermost still-open '{' at the cursor is the enclosing container body.
        var stack = new System.Collections.Generic.List<int>();
        int limit = Math.Min(offset, text.Length);
        for (int i = 0; i < limit; i++)
        {
            if (!mask[i]) continue;
            if (text[i] == '{') stack.Add(i);
            else if (text[i] == '}' && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        }
        if (stack.Count == 0)
        {
            // Top level: the enclosing "block" is the whole file, so its own sections are
            // those declared at brace depth 0. A `section` nested in a part / lyrics track is
            // that container's cell, NOT a top-level section, so it must not count — else a
            // part-major `section B` would be treated as already present and never offered for
            // pulling up to the top level.
            int curTop = SectionKeywordStartBeforeCursor(text, offset);
            int d = 0;
            for (int i = 0; i < text.Length;)
            {
                if (!mask[i]) { i++; continue; }
                char c = text[i];
                if (c == '{') { d++; i++; continue; }
                if (c == '}') { if (d > 0) d--; i++; continue; }
                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    if (d == 0 && s != curTop && text[s..i] == "section")
                    {
                        int j = i;
                        while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                        int ns = j;
                        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                        if (j > ns) declared.Add(text[ns..j]);
                    }
                    continue;
                }
                i++;
            }
            return declared;
        }
        int open = stack[^1];

        // Its matching '}' (or end of document if the container is still unclosed).
        int depth = 0, close = text.Length;
        for (int i = open; i < text.Length; i++)
        {
            if (!mask[i]) continue;
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) { close = i; break; }
        }

        // The cursor's own `section` keyword start (skip the partial name + whitespace,
        // then the preceding word), excluded so an in-place edit of `section B {` still
        // offers B.
        int curKw = SectionKeywordStartBeforeCursor(text, offset);

        foreach (Match m in SectionDeclRegex().Matches(text[open..close]))
        {
            if (open + m.Index == curKw) continue;
            declared.Add(m.Groups[1].Value);
        }
        return declared;
    }

    /// <summary>
    /// True when the section declaration at the cursor ALREADY has an open body — the
    /// next non-whitespace character after the (possibly partial) name is <c>{</c>. Then
    /// completing the name must not add a second <c>{ }</c>; it inserts just the name.
    /// </summary>
    private static bool SectionNameIsFollowedByBrace(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = Math.Clamp(offset, 0, text.Length);
        while (i < text.Length && IsWordChar(text[i])) i++;        // rest of the partial name
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++; // whitespace to the brace
        return i < text.Length && text[i] == '{';
    }

    /// <summary>
    /// Every section declared anywhere in the document (section-major top-level or
    /// part-major inner), EXCLUDING the declaration at the cursor itself. At the top
    /// level this is the scope a new <c>section</c> joins, so subtracting it from the
    /// known universe leaves the form-referenced sections not yet written.
    /// </summary>
    private static System.Collections.Generic.HashSet<string> AllDeclaredSections(string text, int offset)
    {
        var declared = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        int curKw = SectionKeywordStartBeforeCursor(text, offset);
        foreach (Match m in SectionDeclRegex().Matches(text))
        {
            if (m.Index == curKw) continue;
            declared.Add(m.Groups[1].Value);
        }
        return declared;
    }

    /// <summary>Start index of the bare word two tokens before <paramref name="offset"/>
    /// (skip the partial word being typed, the whitespace, then the preceding word) —
    /// the <c>section</c> keyword in the after-<c>section</c> completion context.</summary>
    private static int SectionKeywordStartBeforeCursor(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = Math.Min(offset, text.Length);
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the partial name
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--; // whitespace
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the `section` keyword
        return i;
    }

    /// <summary>The octave-mode words valid right after <c>octave</c>. A bare
    /// integer (<c>octave 3</c>, the part-header base re-anchor) is also legal
    /// there but is typed, not completed.</summary>
    internal static CompletionList GetOctaveCompletions()
    {
        var modes = new (string Label, string Detail)[]
        {
            ("absolute", "Absolute octaves: bare c = C4; ' / , are absolute offsets per note"),
            ("relative", "Relative octaves (default): each note nearest the previous"),
        };
        return new CompletionList
        {
            Items = modes.Select((m, i) => new CompletionItem
            {
                Label = m.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = m.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>
    /// The grob-property targets the renderer actually CONSUMES: colouring and hiding
    /// note heads / stems — the same four rows as <c>SupportedGrobOverrides</c>, which
    /// LYS1029 enforces. Anything else parses and stores but is refused, so it is
    /// deliberately NOT offered — that would mislead (NoteColumn.force-hshift left this
    /// list 2026-08-23 together with its vocabulary row: its reader is disabled, see
    /// ElementCoordinator.ForceHshiftEnabled). Shared by
    /// <see cref="GetOverrideCompletions"/> (which appends <c>= value</c>) and
    /// <see cref="GetRevertCompletions"/> (which does not).
    /// </summary>
    private static readonly (string Grob, string Property, string Kind, string Detail)[] RenderedGrobProperties =
    {
        ("NoteHead", "color", "color", "Colour the note heads"),
        ("Stem", "color", "color", "Colour the stems"),
        ("NoteHead", "transparent", "bool", "Show or hide the note head"),
        ("Stem", "transparent", "bool", "Show or hide the stem"),
    };

    /// <summary>
    /// The grob-property overrides offered right after <c>override</c> (and
    /// <c>once override</c>). Each inserts <c>Grob.property = </c> and — for a property
    /// with an enumerable value (a colour, or true/false) — re-opens the suggest popup so
    /// the value list appears next, exactly like <c>key</c>/<c>clef</c>. No value is
    /// pre-filled. See <see cref="RenderedGrobProperties"/> for why the set is limited.
    /// </summary>
    internal static CompletionList GetOverrideCompletions()
    {
        return new CompletionList
        {
            Items = RenderedGrobProperties.Select((o, i) => new CompletionItem
            {
                Label = $"{o.Grob}.{o.Property}",
                Kind = CompletionItemKind.Property,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = $"{o.Grob}.{o.Property} = ",
                Detail = o.Detail,
                SortText = i.ToString(),
                // Colour / true-false enumerate, so they retrigger; a numeric kind (none
                // today — force-hshift was the one) would not.
                Command = o.Kind is "color" or "bool"
                    ? new Command { Title = "Suggest value", CommandIdentifier = "editor.action.triggerSuggest" }
                    : null,
            }).ToArray()
        };
    }

    /// <summary>
    /// The value forms offered after <c>override Grob.property = </c>, keyed by the
    /// property at the cursor: named colours for <c>color</c>, <c>true</c>/<c>false</c>
    /// for <c>transparent</c>. A numeric property (<c>force-hshift</c>) has no enumerable
    /// value, so nothing is offered (the user types the number).
    /// </summary>
    internal static CompletionList GetOverrideValueCompletions(string text, int offset)
        => OverrideValueProperty(text, offset) switch
        {
            "color" => GetColorCompletions(),
            "transparent" => GetBooleanCompletions(),
            _ => new CompletionList { Items = System.Array.Empty<CompletionItem>() },
        };

    /// <summary>The named colours <see cref="LilySharp.Core.Rendering.ColorParser"/>
    /// understands (a hex <c>#RRGGBB</c> is also valid, but typed, not listed).</summary>
    internal static CompletionList GetColorCompletions()
    {
        var colors = new[] { "red", "green", "blue", "orange", "purple", "brown",
            "yellow", "cyan", "magenta", "gray", "black", "white" };
        return new CompletionList
        {
            Items = colors.Select((c, i) => new CompletionItem
            {
                Label = c,
                Kind = CompletionItemKind.Color,
                InsertText = c,
                Detail = "Named colour",
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The two boolean values, for <c>transparent</c> (hide / show).</summary>
    internal static CompletionList GetBooleanCompletions()
    {
        var vals = new (string Label, string Detail)[]
        {
            ("true", "Hide the grob"),
            ("false", "Show the grob (default)"),
        };
        return new CompletionList
        {
            Items = vals.Select((v, i) => new CompletionItem
            {
                Label = v.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = v.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>
    /// The grob properties offered right after <c>revert</c> — the SAME targets as
    /// <see cref="GetOverrideCompletions"/> but WITHOUT a value, since <c>revert</c> takes
    /// just <c>Grob.property</c> (it undoes a prior override, restoring the default).
    /// </summary>
    internal static CompletionList GetRevertCompletions()
    {
        return new CompletionList
        {
            Items = RenderedGrobProperties.Select((o, i) => new CompletionItem
            {
                Label = $"{o.Grob}.{o.Property}",
                Kind = CompletionItemKind.Property,
                InsertText = $"{o.Grob}.{o.Property}",
                Detail = $"Restore {o.Grob}.{o.Property} to its default",
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>Tonic pitches offered right after <c>key</c>, in circle-of-fifths
    /// order (sharps up, then flats down) so related keys sit together.</summary>
    internal static CompletionList GetKeyTonicCompletions()
    {
        var tonics = new (string Label, string Detail)[]
        {
            ("c", "0 ♯/♭ (major)"), ("g", "1 ♯"), ("d", "2 ♯"), ("a", "3 ♯"),
            ("e", "4 ♯"), ("b", "5 ♯"), ("fis", "6 ♯"), ("cis", "7 ♯"),
            ("f", "1 ♭"), ("bes", "2 ♭"), ("ees", "3 ♭"), ("aes", "4 ♭"),
            ("des", "5 ♭"), ("ges", "6 ♭"), ("ces", "7 ♭"),
        };
        return new CompletionList
        {
            Items = tonics.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = $"Tonic — {t.Detail} signature",
                // Insert the tonic + a space and re-open suggestions, so picking a tonic
                // lands on `key TONIC ` with the scale list ENUMERATED (nothing pre-filled).
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = $"{t.Label} $0",
                Command = new Command { Title = "Suggest scale", CommandIdentifier = "editor.action.triggerSuggest" },
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    // The key modes. Picking a tonic re-opens suggestions (Command) so these modes
    // enumerate right after `key TONIC ` — nothing is pre-filled.
    private static readonly (string Label, string Detail)[] KeyModes =
    {
        ("major", "Major (ionian)"),
        ("minor", "Natural minor (aeolian): major − 3 sharps"),
        ("ionian", "Ionian (= major)"),
        ("dorian", "Dorian: major − 2 sharps"),
        ("phrygian", "Phrygian: major − 4 sharps"),
        ("lydian", "Lydian: major + 1 sharp"),
        ("mixolydian", "Mixolydian: major − 1 sharp"),
        ("aeolian", "Aeolian (= minor)"),
        ("locrian", "Locrian: major − 5 sharps"),
    };

    /// <summary>The modes valid after <c>key TONIC</c> — nothing else fits there.</summary>
    internal static CompletionList GetKeyModeCompletions()
    {
        return new CompletionList
        {
            Items = KeyModes.Select((m, i) => new CompletionItem
            {
                Label = m.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = m.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    // What each clef IS. Prose only — WHICH of these may be offered is decided by the
    // compiler's own vocabularies (LanguageVocabulary), never by this table's membership,
    // because that is precisely what drifted: the table held the five a music block takes
    // and was offered in the part header too, where eleven are legal.
    // The order is high → low sounding pitch, which is the order a reader expects and is
    // not alphabetical; SortText below preserves it against the client's own sorting.
    private static readonly string[] ClefOrder =
    {
        "treble", "treble^8", "treble_8", "soprano", "mezzosoprano",
        "alto", "tenor", "baritone", "bass", "bass_8", "percussion",
    };

    private static readonly System.Collections.Generic.Dictionary<string, string> ClefDetails = new()
    {
        ["treble"] = "Treble (G) clef",
        ["treble^8"] = "Treble clef sounding an octave higher",
        ["treble_8"] = "Treble clef sounding an octave lower (guitar/tenor)",
        ["soprano"] = "Soprano (C) clef",
        ["mezzosoprano"] = "Mezzo-soprano (C) clef",
        ["alto"] = "Alto (C) clef",
        ["tenor"] = "Tenor (C) clef",
        ["baritone"] = "Baritone (C) clef",
        ["bass"] = "Bass (F) clef",
        ["bass_8"] = "Bass clef sounding an octave lower",
        ["percussion"] = "Percussion clef (unpitched staff)",
    };

    /// <summary>
    /// The clef names legal at the caret. ONE production standing in two positions: a part
    /// header takes eleven, a <c>clef</c> directive inside music (and <c>staff</c>/<c>ossia</c>
    /// in a score) takes five.
    /// </summary>
    /// <param name="inPartHeader">
    /// True for the wider position. ⚠️ This argument is the whole fix: until 2026-08-19 there
    /// was no argument, and the five-name list was offered in BOTH positions — so an editor
    /// that never once suggested an illegal clef still hid six legal ones from every part
    /// header in the language. Measured the same day: all eleven compile in a header, and the
    /// six outside <c>ClefNames</c> are refused in music with "Expected clef name".
    /// </param>
    internal static CompletionList GetClefCompletions(bool inPartHeader = false)
    {
        var legal = inPartHeader
            ? LanguageVocabulary.PartClefNames
            : LanguageVocabulary.ClefNames;

        // Ordered by ClefOrder, then anything the compiler grew that this file has not been
        // told how to describe — such a word is still OFFERED (the compiler accepts it), just
        // without prose. Dropping it is what turned a missing description into a missing clef.
        var ordered = legal.OrderBy(
            n => System.Array.IndexOf(ClefOrder, n) is var i && i >= 0 ? i : int.MaxValue);

        return new CompletionList
        {
            // SortText keeps the high→low order (VS Code otherwise sorts by label).
            Items = ordered.Select((name, i) => new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.EnumMember,
                Detail = ClefDetails.TryGetValue(name, out var d) ? d : null,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The instrument-preset names valid right after the <c>instrument</c>
    /// part property (they set clef/octave/tuning defaults). Sourced from
    /// <see cref="InstrumentDefaults.KnownInstruments"/> so the list never drifts from
    /// what the compiler recognizes. When the request context is supplied, each item
    /// carries a TextEdit replacing the whole hyphenated token being typed: the
    /// client's default word range stops at '-', so accepting "piano-right" after
    /// typing "piano-" would otherwise leave the prefix in place
    /// ("piano-piano-right"); the explicit range also makes the client filter
    /// against the full hyphenated prefix.</summary>
    internal static CompletionList GetInstrumentCompletions(
        string? text = null, int offset = 0, Position? position = null)
    {
        LspRange? replaceRange = null;
        if (text != null && position != null)
        {
            int start = offset;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1])
                                 || text[start - 1] == '_' || text[start - 1] == '-'))
                start--;
            replaceRange = new LspRange
            {
                Start = new Position(position.Line, position.Character - (offset - start)),
                End = position,
            };
        }

        return new CompletionList
        {
            // SortText (zero-padded) preserves the family grouping — VS Code otherwise
            // sorts by label, which would scatter e.g. "double-bass" among the woodwinds.
            Items = InstrumentDefaults.KnownInstruments.Select((name, i) => new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.EnumMember,
                Detail = "Instrument preset (clef/octave defaults)",
                SortText = i.ToString("D2"),
                TextEdit = replaceRange == null
                    ? null
                    : new TextEdit { Range = replaceRange, NewText = name },
            }).ToArray()
        };
    }

    // ========== Score templates ==========
    // A template is a WHOLE FILE, not a fragment: it declares title/composer, the parts,
    // the form and the score. Landing one at the caret — what these items did until
    // 2026-08-23 — drops a complete second piece into whatever the writer already had,
    // and every one of those files is then a pile of duplicate globals (LYS8xxx). So the
    // item accepts as a NO-OP edit and hands `lilysharp.applyScoreTemplate` the text; the
    // editor asks before it clears the file. See ScoreTemplateItem.
    //
    // ⚠️ These strings are the ONE home for the template text — it travels to the editor
    // as a command argument, so adding a template here needs no change in the extension.
    // (`editors/vscode/src/extension.ts` has its OWN Twinkle in NEW_SCORE_TEMPLATE: that
    // one seeds an untitled document for "Lily#: New Score" and carries the teaching
    // comments this one does not. Two texts, two jobs — but they are two spellings of one
    // song, and the pair will drift.)
    //
    // ⚠️ No `$0` and no other snippet markers: the client applies these as plain text.
    //
    // ⚠️⚠️ …and for the same reason, NO TABS. Every OTHER item in this file is a snippet,
    // and VS Code re-indents snippet text to the editor's own insertSpaces/tabSize — so a
    // `\t` in a snippet is the PORTABLE spelling and those items keep theirs. These two are
    // the only completion items that are not snippets, so their whitespace is written into
    // the document verbatim. The corpus settles what to write instead: of the 548 tracked
    // `.lys` files that indent at all, 545 use TWO SPACES and NOT ONE uses a tab (measured
    // 2026-08-23). ⚠️ The formatter has no opinion to inherit — `Format` takes the indent
    // from the client's DocumentFormattingParams, so it would rewrite whatever went in.
    //
    // ⚠️ Written as joined lines rather than a raw string literal ("""…"""), whose newlines
    // are the ones in THIS file — CRLF on Windows, LF elsewhere. `.gitattributes` pins
    // `*.lys` to LF on every platform because snapshot data-pos offsets are byte offsets
    // into the source, so a template must not carry the .cs file's line endings. It also
    // makes the indentation VISIBLE: as one escaped line, `\t` read as "indent" for as long
    // as these were snippets and it was.

    private static readonly string TwinkleTemplate = string.Join("\n", [
        "// Twinkle, Twinkle, Little Star (public domain).",
        "title \"Twinkle, Twinkle, Little Star\"",
        "composer \"Jane Taylor\"",
        "",
        "tempo 100",
        "time 4/4",
        "key c major",
        "",
        "part melody {",
        "  clef treble",
        "  section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }",
        "  section B { g'4 g f f | e e d2 | }",
        "}",
        "",
        "// The track sings its melody; the score places its row under the staff.",
        "lyrics verse sings melody {",
        "  section A { Twin- kle twin- kle | lit- tle star | How I won- der | what you are | }",
        "  section B {",
        "    [~1. Up a- bove the | world so high |]",
        "    [~2. Like a dia- mond | in the sky |]",
        "  }",
        "}",
        "",
        "form main { A |: B :| A \"A2\" }",
        "",
        "score main {",
        "  staff melody",
        "  lyrics verse",
        "}",
        "",
    ]);

    private static readonly string TwinklePianoTemplate = string.Join("\n", [
        "// Twinkle, Twinkle, Little Star (public domain) — piano.",
        "title \"Twinkle, Twinkle, Little Star\"",
        "composer \"Jane Taylor\"",
        "",
        "tempo 100",
        "time 4/4",
        "key c major",
        "",
        "part rh { clef treble }",
        "part lh { clef bass }",
        "",
        "section A {",
        "  rh { c4 c g' g | a a g2 | f4 f e e | d d c2 | }",
        "  lh { c2 g | c2 c | f2 c | g2 c | }",
        "}",
        "",
        "form main { A }",
        "",
        "score main {",
        "  grandStaff {",
        "    staff rh",
        "    staff lh",
        "  }",
        "}",
        "",
    ]);

    /// <summary>The command identifier the two <c>template-…</c> items name. The VS Code
    /// extension registers it (see the note on <see cref="Command"/>); it takes the label,
    /// the template text, and — when the caret position was supplied — the four numbers of
    /// the range the identity edit below rewrote, so the editor can tell "the file is
    /// empty" from "the file holds the word the writer just typed".</summary>
    internal const string ApplyScoreTemplateCommand = "lilysharp.applyScoreTemplate";

    /// <summary>One <c>template-…</c> item: a no-op text edit plus the command that does
    /// the real work.
    /// <para>The edit re-types the word being completed <b>as it already stands</b>, so
    /// ACCEPTING THE ITEM CHANGES NOTHING. That is the point: the editor asks before it
    /// clears the file, and declining has to leave the document exactly as the writer left
    /// it — including the "temp" they typed to find this item. (Inserting <c>""</c> instead
    /// would delete that word before the question is asked, and a "No" would then have
    /// silently eaten it.)</para>
    /// <para>An explicit range is also what makes the client filter against the full
    /// hyphenated prefix, the same reason <see cref="GetInstrumentCompletions"/> carries
    /// one.</para>
    /// <para>With no <paramref name="position"/> (the unit tests call it that way) there is
    /// no range to build: the item then inserts the empty string rather than falling back
    /// to the label — a completion must never be able to type "template-twinkle" into a
    /// score.</para></summary>
    private static CompletionItem ScoreTemplateItem(
        string label, string filterText, string detail, string template,
        string? text, int offset, Position? position)
    {
        LspRange? wordRange = null;
        string typed = "";
        if (text != null && position != null)
        {
            int start = offset;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1])
                                 || text[start - 1] == '_' || text[start - 1] == '-'))
                start--;
            typed = text.Substring(start, offset - start);
            wordRange = new LspRange
            {
                Start = new Position(position.Line, position.Character - (offset - start)),
                End = position,
            };
        }

        return new CompletionItem
        {
            Label = label,
            FilterText = filterText,
            Kind = CompletionItemKind.Snippet,
            InsertTextFormat = InsertTextFormat.Plaintext,
            InsertText = "",
            TextEdit = wordRange == null
                ? null
                : new TextEdit { Range = wordRange, NewText = typed },
            Command = new Command
            {
                Title = "Apply score template",
                CommandIdentifier = ApplyScoreTemplateCommand,
                Arguments = wordRange == null
                    ? [label, template]
                    : [label, template,
                       wordRange.Start.Line, wordRange.Start.Character,
                       wordRange.End.Line, wordRange.End.Character],
            },
            Detail = detail,
        };
    }

    internal static CompletionList GetTopLevelCompletions(
        string? text = null, int offset = 0, Position? position = null)
    {
        var items = new System.Collections.Generic.List<CompletionItem>
        {
                new CompletionItem { Label = "part", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "part $1 {\n\t$0\n}", Detail = "Part declaration" },
                new CompletionItem { Label = "section", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "section $1 {\n\t$0\n}", Detail = "Section declaration" },
                new CompletionItem { Label = "phrase", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "phrase $1 {\n\t$0\n}", Detail = "Reusable phrase" },
                new CompletionItem { Label = "form", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "form main { $0 }", Detail = "Piece form (section play order)" },
                new CompletionItem { Label = "score", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "score main {\n\t$0\n}", Detail = "Printable score (visual layout)" },
                new CompletionItem { Label = "title", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "title \"$0\"", Detail = "Title metadata" },
                new CompletionItem { Label = "composer", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "composer \"$0\"", Detail = "Composer metadata" },
                // ⚠️ This inserted `font "$0"` until 2026-08-18 — the removed one-liner —
                // so completing the KEYWORD typed a diagnostic (LYS8007). It is the path a
                // writer actually takes, and it survived the removal because the removal
                // fixed the other three font contexts and not this one.
                // ⚠️ The body comes from FontBlockSnippet, the ONE home: written out here as
                // well, the two spellings drift and the keyword path is the one nobody looks
                // at — which is exactly how it came to be wrong in the first place.
                new CompletionItem { Label = "fonts", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "fonts " + FontBlockSnippet("$0"), Detail = "Text faces per role, pre-filled with the faces in use; add `embedded` to subset-embed them in the exported PDF" },
                // ⚠️ Pre-filled with the DEFAULTS (a4), the fonts snippet's rule: accepting
                // the completion and changing nothing does not move the page.
                new CompletionItem { Label = "paper", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "paper {\n\tpaperWidth ${1:210mm}\n\tpaperHeight ${2:297mm}$0\n}", Detail = "Page dimensions (paper size, margins, spacing), pre-filled with the a4 defaults" },
                new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tempo $0", Detail = "Tempo (BPM)", Command = new Command { Title = "Suggest tempo", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Time signature", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "Key signature", Command = new Command { Title = "Suggest key tonic", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "octave", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "octave $0", Detail = "Octave mode: absolute | relative (default)", Command = new Command { Title = "Suggest octave mode", CommandIdentifier = "editor.action.triggerSuggest" } },
                // `override` is a valid global default; `revert` / `once` are NOT offered at
                // the top level — they only work in a music stream (LYS1023 otherwise).
                // `partial` is likewise NOT offered here — a pickup belongs to a section, not
                // the piece (LYS1024); it appears in the section-level list instead.
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $0", Detail = "Override grob property (global default)", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } },
                ScoreTemplateItem(
                    "template-twinkle",
                    "template scoretemplate score twinkle new",
                    "Score template — REPLACES the whole file (it asks first): single-staff + lyrics (Twinkle, Twinkle, Little Star)",
                    TwinkleTemplate, text, offset, position),
                ScoreTemplateItem(
                    "template-twinkle-piano",
                    "template scoretemplate score twinkle piano",
                    "Score template — REPLACES the whole file (it asks first): piano / grand staff (Twinkle, Twinkle, Little Star)",
                    TwinklePianoTemplate, text, offset, position),
                // ⚠️ BOTH track items scaffold a `section` (reported 2026-08-23, session 240).
                // A top-level TRACK written flat has no section to anchor to, so its cells run
                // from bar 0 across whatever the form plays — an error in part-major layout
                // (LYS4002 for lyrics, LYS2011 for chords). The sectioned body is also right in
                // a SECTION-major file, measured rather than assumed: a track declaring its own
                // sections there places each cell on that section's bars. One body fits both
                // layouts, so neither item can teach the spelling the compiler rejects.
                // ⚠️ Only the CHORDS half had a net (TheChordTrackSnippet_IsWhatTheCompilerAccepts).
                // The lyrics item had been offering the flat body since LYS4002 shipped, and the
                // twin test was written in the same commit as this fix.
                new CompletionItem { Label = "lyrics", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "lyrics ${1:verse} sings ${2:part} {\n\tsection ${3:A} {\n\t\t$0\n\t}\n}", Detail = "Named lyrics track (sings its melody; a score places it with a `lyrics NAME` row)" },
                // ⚠️ The two track kinds are a pair and only one of them was here (reported
                // 2026-08-23): `chords` was offered in a SCORE body — the `chords NAME` row —
                // so it read as a known word, and the declaration that gives that row
                // something to name was the one nobody could reach from the top level.
                // ⚠️ The name is not optional. `chords { … }` is refused (LYS0032: "a
                // 'chords' block needs a name"), so the placeholder is ${1:prog} and not an
                // empty slot the writer might tab past — measured, like the body below,
                // rather than copied from the lyrics item beside it: a chord track takes NO
                // `sings` clause, and offering one would have taught a spelling that errors.
                new CompletionItem { Label = "chords", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "chords ${1:prog} {\n\tsection ${2:A} {\n\t\t$0\n\t}\n}", Detail = "Named chord track (a score places it with a `chords NAME` row)" },
        };

        // Drop the singleton globals (metadata + piece-wide defaults) already written at the
        // top level, so `title` / `composer` / `time` / `key` / … are not re-offered once the
        // file has them. `override` (many grobs), `part`, `section`, `score`, … stay.
        if (text != null)
            items.RemoveAll(it => GlobalSingletonKeywords.Contains(it.Label!)
                               && ExistsAtGlobalScope(text, it.Label!));

        // Offer the document's known section names — from the part cells and the form — as
        // section-major fill-ins, so a section can be pulled up to the top level. Sections
        // ALREADY declared at the top level are dropped by SectionScaffoldItems (its
        // `SectionsDeclaredInCurrentBlock` returns the depth-0 sections here), so writing
        // `section A {}` still leaves `section B` on offer. Top-level sections sit at column 0
        // (nest = ""); the new-section item is skipped (the top-level `section` keyword covers
        // a fresh name).
        if (text != null)
            items.AddRange(SectionScaffoldItems(text, offset, "Section", nest: "", includeNewSection: false));

        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>Top-level keywords that may appear only ONCE at the global scope — metadata
    /// (title/composer/font/paper) and the piece-wide defaults (time/key/tempo/octave).
    /// Completion drops them once present; duplicable keywords are NOT listed here.</summary>
    private static readonly System.Collections.Generic.HashSet<string> GlobalSingletonKeywords =
        new(StringComparer.Ordinal) { "title", "composer", "fonts", "paper", "tempo", "time", "key", "octave" };

    /// <summary>True when <paramref name="keyword"/> appears as a whole word at the GLOBAL
    /// scope (brace depth 0) in live code — not inside a block, a string, or a comment.</summary>
    private static bool ExistsAtGlobalScope(string text, string keyword)
    {
        var mask = CodeMask(text, text.Length);
        int depth = 0;
        for (int i = 0; i < text.Length;)
        {
            if (!mask[i]) { i++; continue; }            // inside a string / comment
            char c = text[i];
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { if (depth > 0) depth--; i++; continue; }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                if (depth == 0 && text[start..i] == keyword)
                    return true;
                continue;                                // i already past the word
            }
            i++;
        }
        return false;
    }

    /// <summary>The seven diatonic chords of the key in force at
    /// <paramref name="offset"/> (C major when none), each a chord-name symbol like
    /// C, Dm, Em, F, G, Am, Bdim — computed from the key's tonic + signature.</summary>
    /// <param name="degreesToo">Offer each degree's ROMAN entry (<c>I</c>, <c>IIm7</c>,
    /// <c>V7</c>) beside the absolute symbol. ⚠️ Only true inside a <c>chords { }</c>
    /// block: MEASURED that <c>@chord(V7)</c> is refused ("Unknown annotation '@chord(V7)'
    /// — it is ignored"), because the annotation reads
    /// <c>ChordStructure.TryParseChordEntry</c> alone. Offering degrees there would teach
    /// a spelling the compiler rejects — the failure two of this session's other islands
    /// already shipped once each.</param>
    internal static CompletionList GetDiatonicChordCompletions(
        string text, int offset, bool degreesToo = false)
    {
        var prefix = text.Substring(0, Math.Min(offset, text.Length));
        var matches = KeyDeclRegex().Matches(prefix);
        char tonic = 'c';
        int sharps = 0;
        if (matches.Count > 0)
        {
            var last = matches[^1];
            tonic = char.ToLowerInvariant(last.Groups[1].Value[0]);
            sharps = KeySpelling.SharpsFor(last.Groups[1].Value, last.Groups[2].Value) ?? 0;
        }

        // Each diatonic degree offers its triad, seventh, and suspended chords — the NAMES
        // first, all seven degrees of them, and then the DEGREES, all seven of those: within
        // each of the two blocks the order is scale order (degree), then
        // triad < 7th < sus4 < sus2 per root. The SYMBOL is both the label and the insert
        // (GRAMMAR_AUDIT 8.1: the entry is the printed form — "Dm7" — for @chord and
        // chords{} alike).
        // ⚠️ IT WAS GROUPED BY DEGREE UNTIL 2026-08-28, with each degree's four name forms
        // and its two degree forms sitting together under it — the reasoning being that the
        // writer chooses a harmonic function first and a spelling second. The owner asked
        // for the two spellings to be separated instead: the list read "C, Cmaj7, Csus4,
        // Csus2, I, Imaj7, Dm, …", so the two vocabularies interleaved all the way down and
        // neither could be scanned on its own. It now reads C D E F G A B … then I II III …
        // ⚠️ THE GROUP IS THE SORT KEY'S FIRST DIGIT AND THE EMIT ORDER MATCHES IT. A client
        // that honours sortText (VS Code does) needs only the key; one that falls back to
        // list order gets the same answer, which is why the two passes below are not one.
        const int NameGroup = 0;
        const int DegreeGroup = 1;
        static CompletionItem Item(string symbol, string detail, int group, int degree, int rank) => new()
        {
            Label = symbol,
            InsertText = symbol,
            Kind = CompletionItemKind.Value,
            Detail = detail,
            SortText = $"{group}{degree:D2}{rank}",
        };

        var chords = DiatonicChords.ForKey(tonic, sharps).ToArray();
        var items = new List<CompletionItem>();
        foreach (var c in chords)
        {
            items.Add(Item(c.Symbol, $"Diatonic triad ({c.Roman})", NameGroup, c.Degree, 0));
            items.Add(Item(c.SeventhSymbol, "Diatonic 7th", NameGroup, c.Degree, 1));
            items.Add(Item(c.SusFourthSymbol, "Suspended 4th", NameGroup, c.Degree, 2));
            items.Add(Item(c.SusSecondSymbol, "Suspended 2nd", NameGroup, c.Degree, 3));
        }
        if (degreesToo)
        {
            foreach (var c in chords)
            {
                // Only the SCALE's chords are offered as degrees, so no entry here ever
                // needs an accidental prefix and every one of them resolves to the absolute
                // symbol printed beside it. A chromatic degree (bVII, #IVm7-5) is writable
                // but is not a completion: it is not in this key.
                items.Add(Item(c.RomanSymbol,
                    $"Degree of the key — {c.Symbol}", DegreeGroup, c.Degree, 0));
                items.Add(Item(c.RomanSeventhSymbol,
                    $"Degree 7th — {c.SeventhSymbol}", DegreeGroup, c.Degree, 1));
            }
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>
    /// Drum-kit completions for a percussion part's music: the DrumNameRegistry
    /// vocabulary (aliases first — the idiomatic form), plus rests and the
    /// structural snippets that remain valid in drum music. No pitch letters.
    /// </summary>
    private static CompletionList GetDrumCompletions(bool insideVoice)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();
        foreach (var kv in LilySharp.Core.Syntax.DrumNameRegistry.AliasEntries)
        {
            LilySharp.Core.Syntax.DrumNameRegistry.TryGet(kv.Key, out var info);
            items.Add(new CompletionItem
            {
                Label = kv.Key,
                Kind = CompletionItemKind.Value,
                Detail = $"{kv.Value} (GM {info.GmKey})",
                SortText = "0" + kv.Key,
            });
        }
        foreach (var kv in LilySharp.Core.Syntax.DrumNameRegistry.CanonicalEntries)
        {
            items.Add(new CompletionItem
            {
                Label = kv.Key,
                Kind = CompletionItemKind.Value,
                Detail = $"GM {kv.Value.GmKey}",
                SortText = "1" + kv.Key,
            });
        }
        items.AddRange(new[]
        {
            new CompletionItem { Label = "r", Kind = CompletionItemKind.Value, Detail = "Rest", SortText = "2r" },
            new CompletionItem { Label = "s", Kind = CompletionItemKind.Value, Detail = "Spacer rest (invisible)", SortText = "2s" },
            new CompletionItem { Label = "R", Kind = CompletionItemKind.Value, Detail = "Full-measure rest", SortText = "2R" },
            new CompletionItem { Label = "repeat", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "repeat percent 2 {\n\t$0\n}", Detail = "Repeat block (percent/unfold/tremolo)", SortText = "3repeat" },
            new CompletionItem { Label = "tuplet", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tuplet 3/2 { $0 }", Detail = "Tuplet (e.g., triplet)", SortText = "3tuplet" },
            new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Change time signature", SortText = "4time", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
            new CompletionItem { Label = "break", Kind = CompletionItemKind.Keyword, InsertText = "break", Detail = "Force a line/system break here", SortText = "4break" },
            new CompletionItem { Label = "nobreak", Kind = CompletionItemKind.Keyword, InsertText = "nobreak", Detail = "Forbid a line break here (LilyPond \\noBreak)", SortText = "4nobreak" },
        });
        // voice { } is only meaningful directly in the part's music —
        // NESTED voice blocks silently become parallel siblings (verified),
        // so the snippet is withheld inside a voice wrapper.
        if (!insideVoice)
            items.Add(new CompletionItem { Label = "voice", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "voice { $0 }", Detail = "Voice (hats up / kick+snare down)", SortText = "3voice" });
        return new CompletionList { IsIncomplete = false, Items = [.. items] };
    }

    internal static CompletionList GetMusicCompletions(string word, int keySharps, bool contracted = false, bool insideVoice = false)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();

        // Pitches, spelled for the key in force at the cursor: in G major (one
        // sharp) the F row is offered as "fis", so accepting it writes the
        // sounding note. Filtering on the spelled form keeps the row visible
        // whether the user typed just "f" or the full "fis".
        foreach (char letter in "cdefgab")
        {
            int alt = LilySharp.Core.Music.KeySpelling.Alteration(
                LilySharp.Core.Music.KeySpelling.StepOf(letter), keySharps);
            string spelled = LilySharp.Core.Music.KeySpelling.SpellLetter(letter, keySharps);
            // lilysharp.completion.flatSpelling = "contracted": suggest the Dutch
            // contractions es/as instead of ees/aes. Only E-flat and A-flat have a
            // contraction; bes/des/ges/ces/fes have none and are left as-is.
            if (contracted)
                spelled = spelled switch { "ees" => "es", "aes" => "as", _ => spelled };
            string upper = char.ToUpperInvariant(letter).ToString();
            items.Add(new CompletionItem
            {
                Label = spelled,
                Kind = CompletionItemKind.Value,
                Detail = alt == 0
                    ? $"{upper} pitch"
                    : $"{upper}{(alt > 0 ? "♯" : "♭")} pitch (from key signature)",
                FilterText = spelled,
                InsertText = spelled,
                SortText = "0" + letter
            });
        }

        items.AddRange(new[]
        {
                // Rests
                new CompletionItem { Label = "r", Kind = CompletionItemKind.Value, Detail = "Rest", SortText = "1r" },
                new CompletionItem { Label = "s", Kind = CompletionItemKind.Value, Detail = "Spacer rest (invisible)", SortText = "1s" },
                new CompletionItem { Label = "R", Kind = CompletionItemKind.Value, Detail = "Full-measure rest", SortText = "1R" },

                // Structures
                new CompletionItem { Label = "|: :|", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "|: $0 :|", Detail = "Volta repeat (symbolic; add endings [1. …] [2. …])", SortText = "2repeat" },
                new CompletionItem { Label = "|: :| [1.][2.]", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "|: $1 [1. $2 ] :| [2. $0 ]", Detail = "Volta repeat with endings", SortText = "2repeatalt" },
                new CompletionItem { Label = "repeat", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "repeat unfold 2 {\n\t$0\n}", Detail = "Repeat block (unfold/percent/tremolo)", SortText = "2repeatkw" },
                new CompletionItem { Label = "tuplet", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tuplet 3/2 { $0 }", Detail = "Tuplet (e.g., triplet)", SortText = "2tuplet" },
                new CompletionItem { Label = "<< >>", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "<< $0 >>", Detail = "Arpeggio: sequential notes, octaves stacked above the first (like a chord). Add a duration after >> for an auto-tuplet.", SortText = "2arpeggio" },
                new CompletionItem { Label = "grace", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "grace { $0 }", Detail = "Grace notes", SortText = "2grace" },
                new CompletionItem { Label = "acciaccatura", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "acciaccatura { $0 }", Detail = "Slashed grace note", SortText = "2acciaccatura" },
                new CompletionItem { Label = "appoggiatura", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "appoggiatura { $0 }", Detail = "Unslashed grace note", SortText = "2appoggiatura" },
                new CompletionItem { Label = "break", Kind = CompletionItemKind.Keyword, InsertText = "break", Detail = "Force a line/system break here", SortText = "2break" },
                new CompletionItem { Label = "nobreak", Kind = CompletionItemKind.Keyword, InsertText = "nobreak", Detail = "Forbid a line break here (LilyPond \\noBreak)", SortText = "2nobreak" },

                // Mid-measure declarations
                new CompletionItem { Label = "clef", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "clef $0", Detail = "Change clef", SortText = "3clef", Command = new Command { Title = "Suggest clef", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "Change key signature", SortText = "3key", Command = new Command { Title = "Suggest key tonic", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Change time signature", SortText = "3time", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tempo $0", Detail = "Change tempo (BPM)", SortText = "3tempo", Command = new Command { Title = "Suggest tempo", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "octave", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "octave $0", Detail = "Octave mode (absolute / relative)", SortText = "3octave", Command = new Command { Title = "Suggest octave mode", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "partial", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "partial $0", Detail = "Pickup: the next measure is a partial of this length", SortText = "3partial" },

                // Grob overrides
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $0", Detail = "Override grob property", SortText = "4override", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "revert", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "revert $0", Detail = "Revert grob property", SortText = "4revert", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "once override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "once override $0", Detail = "One-time override", SortText = "4once", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } }
        });

        // Parallel voices (voice { } voice { }): only meaningful directly in the
        // part's music — nested voice blocks silently become siblings — so the
        // snippet is withheld once the cursor is already inside a voice wrapper.
        if (!insideVoice)
            items.Add(new CompletionItem { Label = "voice", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "voice { $0 }", Detail = "Parallel voice on this staff", SortText = "2voice" });

        // Chord note-expansion: a chord symbol the user is typing (cmaj7, am, g7)
        // offers to replace itself with the spelled note chord <c e g b> — the same
        // tone set that names and (later) voices the chord. The notes are bare so
        // relative mode voices them ascending. LILYPOND-REF: scm/chord-entry.scm.
        if (word.Length >= 2 && ChordStructure.TryParseSymbol(word, out var chord))
        {
            var notes = chord.ToNoteChord();
            items.Insert(0, new CompletionItem
            {
                Label = $"{word}  →  {notes}",
                Kind = CompletionItemKind.Snippet,
                FilterText = word,
                InsertText = notes,
                Detail = $"{chord.DisplayName} chord notes",
                SortText = "00chord",
            });
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>
    /// What to offer when the typed text happens to BE a complete annotation
    /// name: the placement qualifiers AND every name the text still matches.
    /// </summary>
    /// <remarks>
    /// Typing '@trill' used to replace the whole list with '.up'/'.down', so a
    /// search that read "tril → 4 names" turned into "trill → 2 placements" and
    /// then back into "trills → 2 names". A name being complete does not mean the
    /// user has finished typing it: 'trill' is also a prefix of nothing, but a
    /// substring of pralltriller, startTrillSpan and stopTrillSpan.
    ///
    /// The two kinds coexist because they edit different ranges: a placement item
    /// carries an explicit empty TextEdit range at the caret (it appends '.up'),
    /// while a name item has none and so replaces the typed word. That also keeps
    /// both visible in the editor's own filtering — an empty range matches any
    /// query.
    /// </remarks>
    internal static CompletionList PlacementAndStillMatchingNames(
        string text, int offset, Position? position, bool afterChord)
    {
        var placement = GetArticulationPlacementCompletions(text, offset, position);
        if (AfterPlacementDot(text, offset))
            return placement;

        var names = MatchAnywhere(
            GetArticulationCompletions(afterChord), PartialAnnotationName(text, offset));

        return new CompletionList
        {
            IsIncomplete = true,
            Items = [.. placement.Items, .. names.Items],
        };
    }

    internal static CompletionList GetArticulationPlacementCompletions(
        string text, int offset, Position? position = null)
    {
        int j = offset - 1;
        while (j >= 0 && char.IsLetter(text[j])) j--;
        bool afterDot = j >= 0 && text[j] == '.';
        string p = afterDot ? "" : ".";
        // Replace from the placement word (after the dot), or INSERT at the cursor
        // when there is no dot yet. Crucially the range must NOT reach back over the
        // 'fermata' NAME: VS Code filters the items against the text in this range —
        // 'fermata' matches neither '.up' nor '.down', so without an explicit range
        // the items are hidden and nothing appears (the '@fermata|' case).
        int replaceStart = afterDot ? j + 1 : offset;
        LspRange? range = position == null ? null : new LspRange
        {
            Start = new Position(position.Line, position.Character - (offset - replaceStart)),
            End = position,
        };

        CompletionItem Item(string word, string sort) => new()
        {
            Label = p + word,
            Kind = CompletionItemKind.EnumMember,
            Detail = word == "up"
                ? "Force this articulation ABOVE the note"
                : "Force this articulation BELOW the note",
            SortText = sort,
            FilterText = p + word,
            TextEdit = range == null ? null : new TextEdit { Range = range, NewText = p + word },
        };

        return new CompletionList
        {
            IsIncomplete = false,
            // '!' sorts before every digit, so when these are merged with the
            // annotation names (groups "0".."8") the placement stays on top —
            // it is the more specific continuation of what was just typed.
            Items = new[] { Item("up", "!0up"), Item("down", "!1down") },
        };
    }

    /// <summary>
    /// Words a user is likely to TYPE that do not appear in the annotation's own
    /// name. Everything else is derived from the name itself (see
    /// <see cref="WithSearchTerms"/>) — this table is only for the cases where no
    /// slicing of the name can produce the word.
    /// </summary>
    private static readonly Dictionary<string, string> ExtraSearchTerms = new(StringComparer.Ordinal)
    {
        // The pedals are named after the EVENT (LilyPond's names); a user reaches
        // for the printed marking or the instrument's word for it.
        ["sustain"] = "pedal ped",
        ["sostenuto"] = "pedal sost",
        ["unaCorda"] = "pedal soft",
        ["treCorde"] = "pedal soft release",
        // All-lowercase names cannot be split into words, so the part a user is
        // most likely to type has to be listed.
        ["shortfermata"] = "fermata short",
        ["longfermata"] = "fermata long",
        ["invertedturn"] = "turn inverted",
        ["pralltriller"] = "trill prall",
        ["staccatissimo"] = "staccato wedge",
        ["upbow"] = "bow up",
        ["downbow"] = "bow down",
        ["flageolet"] = "harmonic circle",
        ["harmonic"] = "flageolet circle",
        ["notehead"] = "head shape",
        ["fig"] = "figured bass continuo",
        ["snapPizz"] = "bartok pizzicato",
        ["dead"] = "mute muted",
        ["laissezVibrer"] = "lv tie",
        ["repeatTie"] = "tie",
        ["glissando"] = "gliss slide",
        ["mark"] = "rehearsal",
        ["text"] = "dolce expressive",
        ["ottava"] = "8va octave",
        ["quindicesima"] = "15ma octave",
    };

    /// <summary>
    /// Incremental search over the annotation list: an item survives if the typed
    /// text appears ANYWHERE in its name or search terms, so "ill" finds
    /// startTrillSpan and "corda" finds unaCorda.
    /// </summary>
    /// <remarks>
    /// The editor cannot do this. Its suggest widget matches at word starts, and
    /// there is no "match anywhere" switch, so a mid-word query would drop the
    /// item however the server labelled it. Hence the server filters, and each
    /// surviving item carries FilterText = the query itself so the client's own
    /// matcher keeps everything returned; the list is marked incomplete so the
    /// editor asks again on the next keystroke instead of re-filtering a cached
    /// list. SortText still decides the order.
    ///
    /// The cost is the widget's matched-character highlight: with FilterText set
    /// to the query it underlines the label's first characters rather than the
    /// part that actually matched.
    /// </remarks>
    internal static CompletionList MatchAnywhere(CompletionList list, string query)
    {
        // Incomplete even with nothing typed yet: otherwise the editor caches this
        // list and filters it ITSELF as the next characters arrive, which is
        // exactly the word-start matching being replaced here — '@' then "ill"
        // would silently drop everything.
        if (string.IsNullOrEmpty(query))
            return new CompletionList { IsIncomplete = true, Items = list.Items };

        var kept = new List<CompletionItem>();
        foreach (var item in list.Items)
        {
            var haystack = string.IsNullOrEmpty(item.FilterText) ? item.Label : item.FilterText;
            if (haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                item.FilterText = query;
                kept.Add(item);
            }
        }

        return new CompletionList { IsIncomplete = true, Items = [.. kept] };
    }

    /// <summary>
    /// Widens what <see cref="MatchAnywhere"/> searches: the label plus the words
    /// from <see cref="ExtraSearchTerms"/>. Nothing is derived from the label
    /// itself — a substring search already reaches every part of it ("ill" finds
    /// startTrillSpan), so only words that are NOT in the name need listing.
    /// Applied to the whole list rather than to chosen items: no annotation is a
    /// special case.
    /// </summary>
    private static CompletionList WithSearchTerms(CompletionList list)
    {
        foreach (var item in list.Items)
        {
            if (ExtraSearchTerms.TryGetValue(item.Label, out var extra))
                item.FilterText = item.Label + " " + extra;
        }
        return list;
    }

    internal static CompletionList GetArticulationCompletions(bool afterChord = false)
    {
        // Bare '@chord' on a chord auto-derives the symbol from its notes — no '(…)'.
        var chordItem = afterChord
            ? new CompletionItem
            {
                Label = "chord", Kind = CompletionItemKind.Value,
                Detail = "Auto chord name — derived from the chord's notes",
                InsertText = "chord", SortText = "8chord",
            }
            : new CompletionItem
            {
                Label = "chord", Kind = CompletionItemKind.Value,
                Detail = "Chord name — offers the current key's diatonic chords",
                InsertText = "chord($0)", InsertTextFormat = InsertTextFormat.Snippet,
                SortText = "8chord",
                Command = new Command
                {
                    Title = "Suggest chords",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            };

        return WithSearchTerms(new CompletionList
        {
            Items =
            [
                // Articulations
                new CompletionItem { Label = "staccato", Kind = CompletionItemKind.Value, Detail = "Staccato articulation", SortText = "0staccato" },
                new CompletionItem { Label = "accent", Kind = CompletionItemKind.Value, Detail = "Accent", SortText = "0accent" },
                new CompletionItem { Label = "tenuto", Kind = CompletionItemKind.Value, Detail = "Tenuto", SortText = "0tenuto" },
                new CompletionItem { Label = "marcato", Kind = CompletionItemKind.Value, Detail = "Marcato", SortText = "0marcato" },
                new CompletionItem { Label = "fermata", Kind = CompletionItemKind.Value, Detail = "Fermata", SortText = "0fermata" },
                new CompletionItem { Label = "portato", Kind = CompletionItemKind.Value, Detail = "Portato (tenuto + staccato)", SortText = "0portato" },
                new CompletionItem { Label = "staccatissimo", Kind = CompletionItemKind.Value, Detail = "Staccatissimo (wedge)", SortText = "0staccatissimo" },
                new CompletionItem { Label = "upbow", Kind = CompletionItemKind.Value, Detail = "Up-bow (V, above)", SortText = "0upbow" },
                new CompletionItem { Label = "downbow", Kind = CompletionItemKind.Value, Detail = "Down-bow (frog, above)", SortText = "0downbow" },
                new CompletionItem { Label = "harmonic", Kind = CompletionItemKind.Value, Detail = "Harmonic circle ○ (a.k.a. @flageolet)", SortText = "0harmonic" },
                new CompletionItem { Label = "flageolet", Kind = CompletionItemKind.Value, Detail = "Harmonic circle ○ (a.k.a. @harmonic)", SortText = "0flageolet" },
                new CompletionItem { Label = "shortfermata", Kind = CompletionItemKind.Value, Detail = "Short fermata (angular)", SortText = "0shortfermata" },
                new CompletionItem { Label = "longfermata", Kind = CompletionItemKind.Value, Detail = "Long fermata (square)", SortText = "0longfermata" },
                new CompletionItem { Label = "breath", Kind = CompletionItemKind.Value, Detail = "Breath mark after the note", SortText = "0breath" },
                new CompletionItem { Label = "caesura", Kind = CompletionItemKind.Value, Detail = "Caesura (railroad tracks) after the note", SortText = "0caesura" },
                new CompletionItem { Label = "stopped", Kind = CompletionItemKind.Value, Detail = "Stopped note + (brass hand-stop / left-hand pizz.)", SortText = "0stopped" },
                new CompletionItem { Label = "thumb", Kind = CompletionItemKind.Value, Detail = "Thumb position (cello)", SortText = "0thumb" },
                new CompletionItem { Label = "heel", Kind = CompletionItemKind.Value, Detail = "Organ pedal: heel", SortText = "0heel" },
                new CompletionItem { Label = "toe", Kind = CompletionItemKind.Value, Detail = "Organ pedal: toe", SortText = "0toe" },
                new CompletionItem { Label = "scoop", Kind = CompletionItemKind.Value, Detail = "Scoop (jazz articulation into the note)", SortText = "0scoop" },
                new CompletionItem { Label = "plop", Kind = CompletionItemKind.Value, Detail = "Plop (jazz articulation into the note)", SortText = "0plop" },
                new CompletionItem { Label = "fall", Kind = CompletionItemKind.Value, Detail = "Fall (jazz articulation off the note)", SortText = "0fall" },
                new CompletionItem { Label = "doit", Kind = CompletionItemKind.Value, Detail = "Doit (jazz articulation off the note)", SortText = "0doit" },

                // Fretted-instrument techniques. Each has a short spelling that
                // reads better mid-passage, so both are offered.
                new CompletionItem { Label = "hammerOn", Kind = CompletionItemKind.Value, Detail = "Hammer-on (a.k.a. @ho)", SortText = "0hammerOn" },
                new CompletionItem { Label = "ho", Kind = CompletionItemKind.Value, Detail = "Hammer-on, short spelling of @hammerOn", SortText = "0ho" },
                new CompletionItem { Label = "pullOff", Kind = CompletionItemKind.Value, Detail = "Pull-off (a.k.a. @po)", SortText = "0pullOff" },
                new CompletionItem { Label = "po", Kind = CompletionItemKind.Value, Detail = "Pull-off, short spelling of @pullOff", SortText = "0po" },
                new CompletionItem { Label = "tap", Kind = CompletionItemKind.Value, Detail = "Tapped note", SortText = "0tap" },
                new CompletionItem { Label = "snapPizz", Kind = CompletionItemKind.Value, Detail = "Snap (Bartók) pizzicato", SortText = "0snapPizz" },
                new CompletionItem { Label = "slide", Kind = CompletionItemKind.Value, Detail = "Slide to the next note", SortText = "0slide" },
                new CompletionItem { Label = "dead", Kind = CompletionItemKind.Value, Detail = "Dead (muted) note — × notehead", SortText = "0dead" },

                // Free expressive text
                new CompletionItem
                {
                    Label = "text", Kind = CompletionItemKind.Value,
                    Detail = "Free expressive text below the note (\"dolce\", \"pizz.\", …); .up for above",
                    InsertText = "text(\"$0\")", InsertTextFormat = InsertTextFormat.Snippet,
                    SortText = "0text",
                },

                // Ornaments
                new CompletionItem { Label = "trill", Kind = CompletionItemKind.Value, Detail = "Trill ornament", SortText = "1trill" },
                new CompletionItem { Label = "mordent", Kind = CompletionItemKind.Value, Detail = "Mordent ornament", SortText = "1mordent" },
                new CompletionItem { Label = "prall", Kind = CompletionItemKind.Value, Detail = "Inverted mordent (pralltriller)", SortText = "1prall" },
                new CompletionItem { Label = "turn", Kind = CompletionItemKind.Value, Detail = "Turn ornament", SortText = "1turn" },
                new CompletionItem { Label = "invertedturn", Kind = CompletionItemKind.Value, Detail = "Inverted turn", SortText = "1invertedturn" },
                new CompletionItem { Label = "pralltriller", Kind = CompletionItemKind.Value, Detail = "Prall-triller (trill with prall)", SortText = "1pralltriller" },

                // Dynamics (@ prefix style)
                new CompletionItem { Label = "p", Kind = CompletionItemKind.Value, Detail = "Piano (soft)", SortText = "2p" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "Forte (loud)", SortText = "2f" },
                new CompletionItem { Label = "pp", Kind = CompletionItemKind.Value, Detail = "Pianissimo", SortText = "2pp" },
                new CompletionItem { Label = "ff", Kind = CompletionItemKind.Value, Detail = "Fortissimo", SortText = "2ff" },
                new CompletionItem { Label = "mp", Kind = CompletionItemKind.Value, Detail = "Mezzo-piano", SortText = "2mp" },
                new CompletionItem { Label = "mf", Kind = CompletionItemKind.Value, Detail = "Mezzo-forte", SortText = "2mf" },
                new CompletionItem { Label = "ppp", Kind = CompletionItemKind.Value, Detail = "Pianississimo", SortText = "2ppp" },
                new CompletionItem { Label = "pppp", Kind = CompletionItemKind.Value, Detail = "Pianissississimo", SortText = "2pppp" },
                new CompletionItem { Label = "ppppp", Kind = CompletionItemKind.Value, Detail = "Five-p pianissimo", SortText = "2ppppp" },
                new CompletionItem { Label = "fff", Kind = CompletionItemKind.Value, Detail = "Fortississimo", SortText = "2fff" },
                new CompletionItem { Label = "ffff", Kind = CompletionItemKind.Value, Detail = "Fortissississimo", SortText = "2ffff" },
                new CompletionItem { Label = "fffff", Kind = CompletionItemKind.Value, Detail = "Five-f fortissimo", SortText = "2fffff" },
                new CompletionItem { Label = "sfz", Kind = CompletionItemKind.Value, Detail = "Sforzato accent dynamic", SortText = "2sfz" },
                new CompletionItem { Label = "sf", Kind = CompletionItemKind.Value, Detail = "Sforzando accent dynamic", SortText = "2sf" },
                new CompletionItem { Label = "sffz", Kind = CompletionItemKind.Value, Detail = "Heaviest sforzato accent dynamic", SortText = "2sffz" },
                new CompletionItem { Label = "fz", Kind = CompletionItemKind.Value, Detail = "Forzando accent dynamic", SortText = "2fz" },
                new CompletionItem { Label = "rf", Kind = CompletionItemKind.Value, Detail = "Rinforzando accent dynamic", SortText = "2rf" },
                new CompletionItem { Label = "rfz", Kind = CompletionItemKind.Value, Detail = "Rinforzando accent dynamic (rfz)", SortText = "2rfz" },
                new CompletionItem { Label = "fp", Kind = CompletionItemKind.Value, Detail = "Forte-piano accent dynamic", SortText = "2fp" },
                new CompletionItem { Label = "cresc", Kind = CompletionItemKind.Value, Detail = "Crescendo hairpin", SortText = "2cresc" },
                new CompletionItem { Label = "decresc", Kind = CompletionItemKind.Value, Detail = "Decrescendo hairpin", SortText = "2decresc" },
                new CompletionItem { Label = "dim", Kind = CompletionItemKind.Value, Detail = "Diminuendo", SortText = "2dim" },

                // Navigation signs (segno / coda / fine / D.S. / D.C. / to coda) are
                // NOT offered here: they are standalone BARE landmarks, not note
                // modifiers ('@'), so they come from the music / form completions.
                // Rehearsal mark: @mark("A") drops a boxed label. Shown as a bare
                // "mark" (like @text), but completes straight into the quotes so the
                // caret lands where the label is typed.
                new CompletionItem { Label = "mark", Kind = CompletionItemKind.Value, InsertText = "mark(\"$0\")", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Rehearsal mark (boxed label)", SortText = "3mark" },

                // Spanners and brackets
                // The text spanner: three sugar words plus the general spelling. A spanner
                // nobody ends draws NOTHING (LYS4018, LilyPond's own answer), so the
                // terminator is named in every Detail rather than left to be discovered.
                new CompletionItem { Label = "rit", Kind = CompletionItemKind.Value, Detail = "Ritardando text spanner (ends at @!rit)", SortText = "4rit" },
                new CompletionItem { Label = "accel", Kind = CompletionItemKind.Value, Detail = "Accelerando text spanner (ends at @!accel)", SortText = "4accel" },
                new CompletionItem { Label = "rall", Kind = CompletionItemKind.Value, Detail = "Rallentando text spanner (ends at @!rall)", SortText = "4rall" },
                new CompletionItem { Label = "textSpan", Kind = CompletionItemKind.Value, InsertText = "textSpan(\"$0\")", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Text spanner with your own word (ends at @!textSpan)", SortText = "4textSpan" },
                new CompletionItem { Label = "ottava", Kind = CompletionItemKind.Value, Detail = "Ottava bracket up (8va) - ends at @!ottava", SortText = "4ottava" },
                new CompletionItem { Label = "ottava(bassa)", Kind = CompletionItemKind.Value, Detail = "Ottava bracket down (8vb) - ends at @!ottava", SortText = "4ottava.bassa" },
                new CompletionItem { Label = "quindicesima", Kind = CompletionItemKind.Value, Detail = "Quindicesima bracket up (15ma) - ends at @!ottava", SortText = "4quindicesima" },
                new CompletionItem { Label = "quindicesima(bassa)", Kind = CompletionItemKind.Value, Detail = "Quindicesima bracket down (15mb) - ends at @!ottava", SortText = "4quindicesima.bassa" },
                // One word per end, as in LilyPond. (The '@trillSpan(start)'
                // spelling was a second way to say the same thing; it is gone.)
                new CompletionItem { Label = "startTrillSpan", Kind = CompletionItemKind.Value, Detail = "Start trill spanner", SortText = "4startTrillSpan" },
                new CompletionItem { Label = "stopTrillSpan", Kind = CompletionItemKind.Value, Detail = "Stop trill spanner", SortText = "4stopTrillSpan" },
                ArgumentStub("feather", "Feathered beam — offers right (accel.), left (rit.)", "4feather"),

                // Pedal markings
                // LilyPond's own names (ly/spanners-init.ly). One word each: the
                // pedal event carries only START/STOP, so there is no argument.
                // Each pedal is ONE span, closed by '@!' — the Detail names the end so a
                // reader meets it here rather than in LYS4018.
                new CompletionItem { Label = "sustain", Kind = CompletionItemKind.Value, Detail = "Sustain pedal down (Ped.) - ends at @!sustain", SortText = "5sustain" },
                new CompletionItem { Label = "sostenuto", Kind = CompletionItemKind.Value, Detail = "Sostenuto pedal down (Sost. Ped.) - ends at @!sostenuto", SortText = "5sostenuto" },
                new CompletionItem { Label = "unaCorda", Kind = CompletionItemKind.Value, Detail = "Una corda (soft pedal down) - ends at @!unaCorda", SortText = "5unaCorda" },
                new CompletionItem { Label = "treCorde", Kind = CompletionItemKind.Value, Detail = "Tre corde - the una corda release, same mark as @!unaCorda", SortText = "5treCorde" },

                // Notation marks
                new CompletionItem { Label = "glissando", Kind = CompletionItemKind.Value, Detail = "Glissando to next note", SortText = "6glissando" },
                new CompletionItem { Label = "arpeggio", Kind = CompletionItemKind.Value, Detail = "Arpeggiate chord", SortText = "6arpeggio" },
                new CompletionItem { Label = "courtesy", Kind = CompletionItemKind.Value, Detail = "Force courtesy accidental", SortText = "6courtesy" },
                new CompletionItem { Label = "editorial", Kind = CompletionItemKind.Value, Detail = "Editorial (suggestion) accidental above the note", SortText = "6editorial" },
                new CompletionItem { Label = "cross", Kind = CompletionItemKind.Value, Detail = "Cross-staff note (moves to the other staff of the pair)", SortText = "6cross" },
                new CompletionItem { Label = "laissezVibrer", Kind = CompletionItemKind.Value, Detail = "Laissez vibrer tie (hanging, no destination)", SortText = "6laissezVibrer" },
                new CompletionItem { Label = "repeatTie", Kind = CompletionItemKind.Value, Detail = "Repeat tie (hanging tie into a repeat)", SortText = "6repeatTie" },
                new CompletionItem { Label = "rest", Kind = CompletionItemKind.Value, Detail = "Print this note as a rest at its own pitch (a4@rest)", SortText = "6rest" },
                new CompletionItem { Label = "stemUp", Kind = CompletionItemKind.Value, Detail = "Force the stem up", SortText = "6stemUp" },
                new CompletionItem { Label = "stemDown", Kind = CompletionItemKind.Value, Detail = "Force the stem down", SortText = "6stemDown" },

                ArgumentStub("notehead", "Notehead shape — offers x, cross, diamond, triangle, slash, xcircle", "6notehead"),

                ArgumentStub("finger", "Left-hand fingering — offers 0-5", "6finger"),
                ArgumentStub("pluck", "Right-hand (plucking) finger — offers p, i, m, a", "6pluck"),
                ArgumentStub("bend", "String bend — offers half, full", "6bend"),

                // Guitar chord frame: one character per string, low to high —
                // x = muted, o = open, digit = fret.
                new CompletionItem { Label = "frame(x32010)", Kind = CompletionItemKind.Value, Detail = "Chord frame (x = muted, o = open, digit = fret)", SortText = "6frame" },

                // Figured bass — parenthesised, figures space-separated: @fig(6 4).
                ArgumentStub("fig", "Figured bass — offers 6, 6 4, 7, 6 5, 4 3, … (space-separated)", "7fig"),

                // Chord name — on a note the '(…)' form (offers the key's diatonic
                // chords); on a chord the bare auto-derive form. Built above.
                chordItem
            ]
        });
    }

    /// <summary>
    /// An '@' entry whose argument comes from a second list: it inserts
    /// <c>name()</c> with the caret between the parens and asks the editor to
    /// suggest again, so a family's members (six notehead shapes, six
    /// fingerings, …) never crowd the annotation list itself.
    /// </summary>
    private static CompletionItem ArgumentStub(string name, string detail, string sortText) => new()
    {
        Label = name,
        Kind = CompletionItemKind.Value,
        Detail = detail,
        InsertText = $"{name}($0)",
        InsertTextFormat = InsertTextFormat.Snippet,
        SortText = sortText,
        Command = new Command
        {
            Title = $"Suggest {name} arguments",
            CommandIdentifier = "editor.action.triggerSuggest",
        },
    };

    /// <summary>
    /// The argument vocabulary of an <c>@name(…)</c> annotation, or null when the
    /// annotation takes free-form text (<c>@text</c>, <c>@mark</c>, <c>@frame</c>)
    /// or has its own key-dependent list (<c>@chord</c>, handled separately).
    /// This is the second half of the two-step completion: the '@' list offers the
    /// bare name, and the argument is picked from here.
    /// </summary>
    internal static CompletionList? GetAnnotationArgumentCompletions(string annotation) =>
        annotation.ToLowerInvariant() switch
        {
            "notehead" => GetNoteheadCompletions(),
            "finger" => GetFingerCompletions(),
            "pluck" => GetPluckCompletions(),
            "bend" => GetBendCompletions(),
            "feather" => GetFeatherCompletions(),
            "fig" => GetFiguredBassCompletions(),
            _ => null
        };

    /// <summary>One argument item; the list order is the order given.</summary>
    private static CompletionItem Argument(string label, string detail, int rank) => new()
    {
        Label = label,
        Kind = CompletionItemKind.Value,
        Detail = detail,
        SortText = $"{rank}{label}",
    };

    /// <summary>
    /// The notehead shapes, offered inside <c>@notehead(…)</c>. Sorted with the
    /// two percussion/rhythm shapes first, since those are what a user reaches
    /// for most; the rest follow in the order the collector documents them.
    /// </summary>
    internal static CompletionList GetNoteheadCompletions() => new()
    {
        Items =
        [
            Argument("x", "× notehead (dead/muted, percussion)", 0),
            Argument("cross", "Cross notehead", 1),
            Argument("diamond", "Diamond notehead ◇ (harmonic)", 2),
            Argument("triangle", "Triangle notehead", 3),
            Argument("slash", "Slash notehead (rhythm notation)", 4),
            Argument("xcircle", "Circled-× notehead", 5),
        ]
    };

    /// <summary>
    /// Left-hand fingering, inside <c>@finger(…)</c>. Any non-negative number
    /// parses; 0-5 (open string / thumb through little finger) is the range a
    /// score actually uses, so it is what the list offers.
    /// </summary>
    internal static CompletionList GetFingerCompletions() => new()
    {
        Items =
        [
            Argument("0", "Open string (or no finger)", 0),
            Argument("1", "Index finger (piano: thumb)", 1),
            Argument("2", "Middle finger", 2),
            Argument("3", "Ring finger", 3),
            Argument("4", "Little finger", 4),
            Argument("5", "Fifth finger (piano)", 5),
        ]
    };

    /// <summary>
    /// Right-hand (plucking) fingering, inside <c>@pluck(…)</c> — the Spanish
    /// guitar names.
    /// </summary>
    internal static CompletionList GetPluckCompletions() => new()
    {
        Items =
        [
            Argument("p", "Thumb (pulgar)", 0),
            Argument("i", "Index (índice)", 1),
            Argument("m", "Middle (medio)", 2),
            Argument("a", "Ring (anular)", 3),
        ]
    };

    /// <summary>String bend amounts, inside <c>@bend(…)</c>.</summary>
    internal static CompletionList GetBendCompletions() => new()
    {
        Items =
        [
            Argument("half", "Bend up a semitone", 0),
            Argument("full", "Bend up a whole tone", 1),
        ]
    };

    /// <summary>
    /// Figured bass, inside <c>@fig(…)</c>. The figures are space-separated and
    /// stack top to bottom, so the vocabulary is not a fixed set — what is
    /// offered is the continuo shorthand a score actually uses, most frequent
    /// first, plus the two non-numeric atoms (bare accidental, held line).
    /// Alterations are written after their figure: 6 s = 6♯, 4 f = 4♭, 7 n = 7♮.
    /// </summary>
    internal static CompletionList GetFiguredBassCompletions() => new()
    {
        Items =
        [
            Argument("6", "First inversion (6/3)", 0),
            Argument("6 4", "Second inversion", 1),
            Argument("7", "Seventh chord", 2),
            Argument("6 5", "Seventh, first inversion", 3),
            Argument("4 3", "Seventh, second inversion", 4),
            Argument("4 2", "Seventh, third inversion", 5),
            Argument("5 3", "Root position, written out", 6),
            Argument("9", "Ninth (9-8 suspension)", 7),
            Argument("4", "Fourth (4-3 suspension)", 8),
            Argument("2", "Second", 9),
            Argument("6 s", "6♯ — an alteration follows its figure (s/f/n)", 10),
            Argument("#", "Bare sharp — raises the third above the bass", 11),
            Argument("_", "Held figure — continuation line from the previous bass note", 12),
        ]
    };

    /// <summary>
    /// Feathered-beam directions, inside <c>@feather(…)</c>. The beam opens
    /// toward the side named, so right = getting faster.
    /// </summary>
    internal static CompletionList GetFeatherCompletions() => new()
    {
        Items =
        [
            Argument("right", "Opening right — accelerando", 0),
            Argument("left", "Opening left — ritardando", 1),
        ]
    };

    private static CompletionList GetDynamicCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem { Label = "ppp", Kind = CompletionItemKind.Value, Detail = "Pianississimo" },
                new CompletionItem { Label = "pp", Kind = CompletionItemKind.Value, Detail = "Pianissimo" },
                new CompletionItem { Label = "p", Kind = CompletionItemKind.Value, Detail = "Piano" },
                new CompletionItem { Label = "mp", Kind = CompletionItemKind.Value, Detail = "Mezzo-piano" },
                new CompletionItem { Label = "mf", Kind = CompletionItemKind.Value, Detail = "Mezzo-forte" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "Forte" },
                new CompletionItem { Label = "ff", Kind = CompletionItemKind.Value, Detail = "Fortissimo" },
                new CompletionItem { Label = "fff", Kind = CompletionItemKind.Value, Detail = "Fortississimo" },
                new CompletionItem { Label = "cresc", Kind = CompletionItemKind.Value, Detail = "Crescendo" },
                new CompletionItem { Label = "decresc", Kind = CompletionItemKind.Value, Detail = "Decrescendo" },
                new CompletionItem { Label = "dim", Kind = CompletionItemKind.Value, Detail = "Diminuendo" }
            ]
        };
    }

}

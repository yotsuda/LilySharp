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

// Chord tracks: the \chordmode variables of chord parts (EmitChordTracks), their FretBoards,
// the chord list, and the spelling of a \chordmode entry under key and capo. Split out of
// LilyPondExporter.cs as a partial class (2026-10-02); same instance state, no behavior change.
public sealed partial class LilyPondExporter
{
    /// <summary>Under a capo with <c>chordNames both</c>, the ChordNames track names each chord
    /// "sounding (pressed)" as the page does (<see cref="Music.ChordStructure.PrintedSymbol"/>):
    /// LilyPond has no capo naming, so each entry carries its pressed chord (<see cref="TwinEntry"/>).</summary>
    private bool NamesBoth => _layoutPlan.Chords.Capo > 0 && _layoutPlan.Chords.Names == Semantics.ChordNameMode.Both;

    /// <summary>
    /// The chordNameFunction <see cref="TwinEntry"/> sets once per chord under <see cref="NamesBoth"/>:
    /// LilyPond's own <c>ignatzek-chord-names</c> twice — on the sounding chord it is handed, and
    /// on the pressed chord the entry carries — "E♭m7 (Cm7)". Until 2026-09-30 the twin named
    /// the sounding chord alone and warned.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm:1530-1557 Current_chord_text_engraver — the
    ///   pressed chord's note events are read the way the engraver reads its own: a <c>bass</c>
    ///   note is the bass, an <c>inversion</c> note is named at its <c>octavation</c>-restored
    ///   pitch and is the inversion, the rest sorted by <c>ly:pitch&lt;?</c>.
    /// </remarks>
    private static readonly string CapoBothNamer = """
        #(define (lysCapoBoth pressed)
           (lambda (pitches bass inversion context)
             (let ((ps '()) (b '()) (inv '()))
               (for-each
                (lambda (m)
                  (let ((p (ly:music-property m 'pitch)))
                    (if (ly:music-property m 'bass #f)
                        (set! b p)
                        (let ((oct (ly:music-property m 'octavation)))
                          (set! ps (cons (if (integer? oct) (ly:pitch-transpose p (ly:make-pitch (- oct) 0)) p) ps))
                          (if (ly:music-property m 'inversion #f) (set! inv p))))))
                (extract-typed-music pressed 'note-event))
               (make-concat-markup
                (list (ignatzek-chord-names pitches bass inversion context)
                      " ("
                      (ignatzek-chord-names (sort ps ly:pitch<?) b inv context)
                      ")")))))


        """.ReplaceLineEndings("\n");

    /// <summary>A chord's <c>\chordmode</c> entry as the twin writes it (<see cref="TwinChord"/>);
    /// under <see cref="NamesBoth"/> a ChordNames entry first sets the naming that prints its
    /// pressed name after it (<see cref="CapoBothNamer"/>).</summary>
    private string TwinEntry(Music.ChordStructure chord, int keySharps, string duration)
    {
        string spelled = TwinChord(chord, keySharps).ToChordMode(duration);
        if (!NamesBoth || _fretTrack)
            return spelled;
        return "\\once \\set chordNameFunction = #(lysCapoBoth #{ \\chordmode { "
            + chord.Pressed(_layoutPlan.Chords.Capo, keySharps).ToChordMode("") + " } #}) " + spelled;
    }

    /// <summary>
    /// The chord list as a top-level <c>\markup</c> before the score — the page's own rows
    /// (<see cref="Svg.Layout.ChordListBand"/>, <see cref="Svg.Layout.HeaderBand.WithChordList"/>
    /// on the page's metrics and paper), each a <c>\fill-line</c> holding one centred line of
    /// <c>\center-column { "NAME" \fret-diagram-terse "…" }</c> cells, so LilyPond pages the
    /// list where the page does: under the title, above the first system.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: LilyPond has no chord list. The names are plain markup text here
    /// (<c>Dm7</c>, not the raised <c>Dm⁷</c> the page and a ChordNames context print — a name
    /// is formatted only inside that context); the diagrams are the page's pressed shapes.
    /// </remarks>
    private void EmitChordList()
    {
        if (!_layoutPlan.ChordList || _page is not { } page)
            return;
        var entries = Svg.Layout.ChordListBand.EntriesOf(page);
        var band = Svg.Layout.HeaderBand.WithChordList(null, entries, page.TextMetrics,
            page.Paper.PageWidth, page.Paper.MarginLeft, page.Paper.ContentWidth);
        if (band?.ChordList is not { } list)
            return;
        foreach (var row in list.Cells.GroupBy(c => c.NameBaseline).OrderBy(g => g.Key))
        {
            _sb.Append("\\markup \\fill-line { \\line {");
            foreach (var cell in row.OrderBy(c => c.X))
            {
                _sb.Append(" \\center-column { \"").Append(Escape(cell.Entry.Text)).Append('"');
                if (cell.Entry.Spec is { } spec)
                    _sb.Append(" \\fret-diagram-terse #\"").Append(TerseOf(spec)).Append('"');
                _sb.Append(" }");
            }
            _sb.Append(" } }\n");
        }
        _sb.Append('\n');
    }

    /// <summary>
    /// A page diagram spec as LilyPond's terse string — one entry a string, low string first:
    /// <c>x</c>, <c>o</c>, or the fret, then (with <paramref name="fingers"/>, a FretBoard's)
    /// <c>-N</c> the finger, then <c>-(</c> / <c>-)</c> where a barre of the spec's detail
    /// suffix starts / ends (<c>1-1-(;3-3;3-4;2-2;1-1;1-1-);</c> is LilyPond's own F).
    /// LILYPOND-REF: scm/fret-diagrams.scm:1003-1066 fret-parse-terse-definition-string — the
    ///   items split on <c>;</c>, an item's parts on <c>-</c>: fret, finger, and a last <c>(</c>
    ///   or <c>)</c> for the barre (barre-start-list).
    /// </summary>
    private static string TerseOf(string spec, bool fingers = false)
    {
        var terse = new StringBuilder();
        var barres = Svg.Layout.FretFrameGeometry.Barres(spec);
        int n = Svg.Layout.FretFrameGeometry.Strings(spec);
        for (int i = 0; i < n; i++)
        {
            int fret = Svg.Layout.FretFrameGeometry.FretAt(spec, i);
            terse.Append(fret < 0 ? "x" : fret == 0 ? "o" : fret.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (fingers && fret > 0 && Svg.Layout.FretFrameGeometry.FingerAt(spec, i) is > 0 and var finger)
                terse.Append('-').Append(finger.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var b in barres)
            {
                if (b.From == i)
                    terse.Append("-(");
                if (b.To == i)
                    terse.Append("-)");
            }
            terse.Append(';');
        }
        return terse.ToString();
    }

    // ---- Score / staff / tab ----------------------------------------------

    /// <summary>
    /// One <c>\chordmode</c> variable per chord part a score row names, written before the
    /// <c>\score</c> like the part variables — the ChordNames context the row becomes
    /// (see <see cref="EmitScore"/>). The track is flattened through the SAME form walk as
    /// the music (<see cref="AppendFormItems"/>, with <see cref="AppendSection"/> taking its
    /// chord arm), so a repeat, an ending and a reprise fall where the music's do, and the
    /// bars are spelled as LilyPond's chord entries (<see cref="ChordBarText"/>) so LilyPond
    /// realizes and names them itself.
    /// </summary>
    /// <remarks>
    /// Owner decision 2026-09-08 (HANDOFF §3): the twin hands LilyPond <c>\chordmode</c>
    /// entries — LilyPond's own Ignatzek names then stand on the twin, not Lily#'s display
    /// strings — and every Lily# spelling is rewritten into one LilyPond accepts.
    /// ⚠️ Inline <c>@chord</c> marks on notes are NOT here: they ride the music stream, where
    /// <see cref="EmitMark"/> still reports them — a ChordNames stream for them would have to
    /// be re-timed off the note durations, which is a different reader.
    /// </remarks>
    private void EmitChordTracks(CompilationUnitSyntax root, RenderDeclarationSyntax? render,
        FormDeclarationSyntax? form, List<SectionDeclarationSyntax> allSections)
    {
        if (render == null)
            return;
        foreach (var item in RenderRows(render))
        {
            if (item is not ChordRowRenderSyntax row || row.PartName.Length == 0
                || _chordVars.ContainsKey(row.PartName))
                continue;
            var blocks = root.KindSites(SyntaxKind.ChordPartBlock).OfType<ChordPartBlockSyntax>()
                .Where(b => b.PartName == row.PartName).ToList();
            if (blocks.Count == 0)
                continue;   // EmitScore reports the row
            var items = OrderedChordItems(blocks, form, allSections);
            if (items.Count == 0)
                continue;
            string varName = VarName(row.PartName + "Chords");
            _chordVars[row.PartName] = varName;
            _sb.Append(varName).Append(" = \\chordmode {\n");
            _chordTrack = true;
            EmitMusicStream(items, indent: "  ");
            _chordTrack = false;
            _sb.Append("}\n\n");

            // The diagrams under the names: the idiomatic LilyPond is a FretBoards context over
            // the SAME chord music (EmitScore places it under the ChordNames row) — the same
            // bars, spelled again with the diagram of each chord the page draws
            // (FretBoardPrefix). The context appears only when some entry DRAWS on the row's
            // tuning (the layout's, else the instrument of the staff the row stands over, else
            // the guitar): a written shape (owner's decision 2026-09-28), a chord the layout's
            // shape table lists (2026-09-29), or — in a `chordDiagrams … all` score — every
            // entry. The track is spelled first and kept only when a prefix was written
            // (_fretPrefixes): a FretBoards context of silent slots alone would be an empty band.
            var rowStaffWord = _shared.Root is { } fileRoot && _renderSpec != null
                ? Semantics.ChordDiagramScores.RowStaffWord(fileRoot, _renderSpec, row.PartName) : null;
            if (_layoutPlan.ChordDiagramTuningFor(rowStaffWord is { } rw ? Tablature.Tunings.Parse(rw) : null)
                    is { } diagramTuning)
            {
                _fretTuning = diagramTuning;
                _fretTrack = true;
                _fretPrefixes = 0;
                var fretItems = OrderedChordItems(blocks, form, allSections);
                _fretTrack = false;
                if (_fretPrefixes > 0)
                {
                    _sb.Append(_fretDefinitions);
                    _fretDefinitions.Clear();
                    string fretVar = VarName(row.PartName + "Frets");
                    _fretVars[row.PartName] = fretVar;
                    _fretTunings[row.PartName] = diagramTuning;
                    _sb.Append(fretVar).Append(" = \\chordmode {\n");
                    _chordTrack = true;
                    EmitMusicStream(fretItems, indent: "  ");
                    _chordTrack = false;
                    _sb.Append("}\n\n");
                }
            }
        }
    }

    // ---- Chord diagrams under a row (a written shape, HANDOFF §2 K) -----------------------

    /// <summary>The FretBoards variable of each chord part a row places, when the score draws
    /// chord diagrams (<see cref="EmitChordTracks"/>).</summary>
    private readonly Dictionary<string, string> _fretVars = new(StringComparer.Ordinal);

    /// <summary>True while <see cref="EmitChordTracks"/> spells a row's bars for its
    /// FretBoards context (<see cref="ChordBarText"/> then prefixes each entry).</summary>
    private bool _fretTrack;

    /// <summary>How many entries of the row being spelled got a prefix (<see cref="FretBoardPrefix"/>)
    /// — zero: no diagram draws under it, and the FretBoards context is left out.</summary>
    private int _fretPrefixes;

    /// <summary>The tuning each row's FretBoards context draws on (<see cref="EmitChordTracks"/>).</summary>
    private readonly Dictionary<string, TuningType> _fretTunings = new(StringComparer.Ordinal);

    /// <summary>The tuning of the FretBoards variable being spelled (<see cref="FretBoardPrefix"/>).</summary>
    private TuningType _fretTuning = TuningType.Guitar;

    /// <summary>The one-shape fretboard tables already defined, by (chord entry, terse
    /// string), and the Scheme name each got.</summary>
    private readonly Dictionary<string, string> _fretTables = new(StringComparer.Ordinal);

    /// <summary>The table definitions the current FretBoards variable needs, written before it.</summary>
    private readonly StringBuilder _fretDefinitions = new();

    /// <summary>
    /// What a FretBoards entry needs before it so LilyPond draws the diagram the page draws:
    /// <c>\once \set predefinedDiagramTable = #lysFrets…</c>, a one-shape table holding the
    /// shape WRITTEN for the row's tuning — in a <c>chordDiagrams … all</c> score, else the
    /// default the page draws (<see cref="Music.ChordShapes.Drawn"/>); null when the page draws
    /// none (owner's decision 2026-09-28) — the FretBoards track then writes a silent
    /// <c>s</c> in its place, since LilyPond would compute a diagram of its own for any chord it
    /// is given.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE-SHAPE TABLES IN AN <c>all</c> SCORE TOO, not LilyPond's predefined tables
    /// <c>\include</c>d: those would reproduce Lily#'s choice only for the chords they hold, and
    /// for the rest LilyPond computes a shape of its own where Lily# draws the first of its order
    /// (or nothing, on the ukulele) — so every entry keeps the table that holds exactly the shape
    /// the page draws (with the predefined entry's fingers, when that is its source).
    /// LILYPOND-REF: scm/translation-functions.scm:798-822 get-predefined-fretboard — the
    ///   FretBoards context looks a chord up in its predefinedDiagramTable by (stringTunings .
    ///   pitches), an octave either way, and computes a shape of its own when the table has
    ///   none (:861-877) — which would draw a diagram the page does not, hence the one-shape
    ///   table rather than trusting LilyPond's lookup.
    /// LILYPOND-REF: ly/predefined-fretboards-init.ly storePredefinedDiagram (lines 65-82).
    /// </remarks>
    private string? FretBoardPrefix(ChordEntrySyntax entry, (int TonicStep, int Sharps) key)
    {
        var tuningType = _fretTuning;
        string symbol = entry.SymbolText;
        Music.ChordStructure? chord =
            Music.ChordStructure.TryParseChordEntry(symbol, out var parsed) ? parsed
            : Music.ChordStructure.TryParseRomanEntry(symbol, key.TonicStep, key.Sharps, out var degree) ? degree
            : null;
        var shapes = Semantics.ChordDiagramScores.ShapesOf(entry).Shapes;
        var table = _layoutPlan.ChordDiagramTable;
        var chosen = Music.ChordShapes.Drawn(tuningType, shapes, _layoutPlan.ChordDiagramsAll, chord,
            table, table != null ? Semantics.ChordDiagramScores.SectionNameOf(entry) : null,
            _layoutPlan.Chords.Capo);
        if (chosen == null || chord == null)
            return null;
        _fretPrefixes++;

        // The FretBoards track spells the PRESSED chords under a capo (ChordBarText), so the
        // one-shape table is keyed by the pressed chord too.
        string chordEntry = chord.Pressed(_layoutPlan.Chords.Capo, key.Sharps).ToChordMode("");
        // A FretBoard: the fingers and the barre of a predefined shape are LilyPond's own
        // (finger-code below-string draws the fingers; the page's row diagram draws both too).
        string terse = TerseOf(chosen.FrameSpec, fingers: true);
        string tableKey = chordEntry + "|" + terse;
        if (!_fretTables.TryGetValue(tableKey, out var name))
        {
            name = "lysFrets" + ColumnLetters(_fretTables.Count);
            _fretTables[tableKey] = name;
            _fretDefinitions.Append("#(define ").Append(name).Append(" (make-fretboard-table))\n")
                .Append("\\storePredefinedDiagram #").Append(name)
                .Append(" \\chordmode { ").Append(chordEntry).Append(" } #")
                .Append(Tablature.Tunings.LilyPondName(tuningType))
                .Append(" \"").Append(terse).Append("\"\n");
        }
        return "\\once \\set predefinedDiagramTable = #" + name + " ";
    }

    /// <summary>A, B, … Z, AA, AB, … — a Scheme-safe suffix for the n-th table.</summary>
    private static string ColumnLetters(int n)
    {
        var s = new StringBuilder();
        n++;
        while (n > 0)
        {
            n--;
            s.Insert(0, (char)('A' + n % 26));
            n /= 26;
        }
        return s.ToString();
    }

    /// <summary>The <c>\with { }</c> a row's FretBoards context takes: its tuning, unless it is
    /// the guitar's (FretBoards' own default).</summary>
    private string FretBoardsWith(string rowName)
    {
        var tuningType = _fretTunings.GetValueOrDefault(rowName, TuningType.Guitar);
        return tuningType == TuningType.Guitar
            ? ""
            : "\\with { stringTunings = #" + Tablature.Tunings.LilyPondName(tuningType) + " } ";
    }

    /// <summary>
    /// The ChordNames context for a part's INLINE <c>@chord</c> marks, placed over the first
    /// row that shows the part (a staff, a full-notation tab, a group's staff) — once.
    /// </summary>
    private void AddInlineChordRow(List<string> rows, string? partName, string indent)
    {
        if (partName != null && _inlineChordVars.TryGetValue(partName, out var v)
            && _inlineChordPlaced.Add(partName))
            rows.Add(indent + "\\new ChordNames " + ChordNamesWith() + "\\" + v + "\n");
    }

    /// <summary>
    /// The <c>\with { }</c> a ChordNames context carries when the score spelled its chords
    /// differently — today only <c>minorChords lower</c>, which is LilyPond's own property.
    /// Empty (and no braces at all) for a score that wrote nothing, so every existing twin
    /// is unchanged.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly chordNameLowercaseMinor (line 948) — the ChordNames
    ///   property, <c>##f</c> by default, that lowercases a minor chord's root and drops its
    ///   minorChordModifier. (The line is in prose: see Semantics.ChordQualityStyle's remark.)
    /// ⚠️ <c>layout { chordQualities … }</c> reaches NOTHING here, and that is the 2026-09-08
    /// owner decision rather than a gap: the twin hands LilyPond <c>\chordmode</c> entries
    /// and LilyPond names them by its OWN rules, which are already the symbols — so under
    /// <c>chordQualities symbols</c> the page and the twin agree about those four qualities for
    /// the first time, and under the default <c>words</c> they diverge exactly as they
    /// always have (<see cref="Semantics.ChordQualityStyle"/>'s remark, and
    /// <c>ChordNameGlyphRun</c>'s).
    /// </remarks>
    private string ChordNamesWith()
        => _layoutPlan.Chords.LowercaseMinor
            ? "\\with { chordNameLowercaseMinor = ##t } "
            : "";

    /// <summary>One placed inline symbol as its entry: the model's resolved structure spelled
    /// by <see cref="Music.ChordStructure.ToChordMode"/>; a symbol with no tone set (an
    /// unregistered quality, or a literal the page could not parse) goes out as its root or
    /// as silence, and the twin says so once.</summary>
    /// <summary>
    /// The chord a <c>\chordmode</c> entry spells under the score's capo: the PRESSED chord
    /// (<see cref="Music.ChordStructure.Pressed"/>) in the FretBoards track (its diagrams are
    /// pressed shapes) and, under <c>chordNames shape</c> — the default — in the ChordNames
    /// track too, so LilyPond prints the name the page prints; the sounding chord under
    /// <c>sounding</c> and <c>both</c> (LilyPond has no capo naming; <see cref="EmitHeader"/> warns
    /// for <c>both</c>). Without a capo, the chord itself.
    /// </summary>
    private Music.ChordStructure TwinChord(Music.ChordStructure chord, int keySharps)
        => _layoutPlan.Chords.Capo > 0 && (_fretTrack || _layoutPlan.Chords.Names == Semantics.ChordNameMode.Shape)
            ? chord.Pressed(_layoutPlan.Chords.Capo, keySharps)
            : chord;

    private string InlineChordEntry(Svg.Model.ChordNameItem c, string duration)
    {
        if (c.Structure is { } s)
        {
            string spelled = TwinChord(s, _keySharps).ToChordMode(duration);
            if (s.RawSuffix != null && _chordWarned.Add(c.ChordText))
                _warnings.Add($"@chord '{c.ChordText}': the quality '{s.RawSuffix}' has no \\chordmode spelling — "
                    + $"the twin writes the root alone ({spelled})");
            return TwinEntry(s, _keySharps, duration);
        }
        if (_chordWarned.Add(c.ChordText))
            _warnings.Add($"@chord '{c.ChordText}' is not a chord symbol the twin can spell — written as a silent slot");
        return "s" + duration;
    }

    /// <summary>
    /// A chord part's bars in playing order — the chord twin of <see cref="OrderedMusic"/>.
    /// A by-part track (<c>chords P { section A { … } }</c>) registers each inner section
    /// under its name; a flat block registers under the section that encloses it; a flat
    /// block at top level plays once, first (the collector anchors it at bar 0).
    /// </summary>
    private List<SyntaxNode> OrderedChordItems(List<ChordPartBlockSyntax> blocks,
        FormDeclarationSyntax? form, List<SectionDeclarationSyntax> allSections)
    {
        _sectionHeaders = Semantics.SectionHeaders.Read(allSections);
        _allSections = allSections;
        var byName = new Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)>(
            StringComparer.Ordinal);
        var inOrder = new List<(SectionDeclarationSyntax Section, SyntaxNode Container)>();
        var loose = new List<ChordPartBlockSyntax>();
        void Register(SectionDeclarationSyntax section, SyntaxNode container)
        {
            byName[section.SectionName] = (section, container);
            inOrder.Add((section, container));
        }
        foreach (var block in blocks)
        {
            if (block.HasSections)
            {
                foreach (var s in block.Sections)
                    Register(s, s);
                continue;
            }
            SectionDeclarationSyntax? enclosing = null;
            for (var n = block.Parent; n != null; n = n.Parent)
                if (n is SectionDeclarationSyntax s) { enclosing = s; break; }
            if (enclosing != null)
                Register(enclosing, block);
            else
                loose.Add(block);
        }

        var result = new List<SyntaxNode>();
        _repeatTiePlays = new HashSet<int>(); // a chord track carries no ties
        _chordTrack = true;
        foreach (var block in loose)
            result.AddRange(ChordBars(block, (_homeTonic.Step, _homeKeySharps)));
        if (form != null)
            AppendFormItems(FormWalk.Read(form), byName, result);
        else
            foreach (var entry in inOrder)
            {
                result.AddRange(ChordBars(entry.Container, ChordKeyFor(entry.Section.SectionName),
                    meters: SectionBarMeters(entry.Section, entry.Section.SectionName)));
                result.AddRange(PaddingBars(entry.Container));
            }
        _chordTrack = false;
        return result;
    }

    /// <summary>The key a scale-degree entry of the named section stacks in: the section's
    /// own header key, else the score's home key — the two keys the collector's timeline
    /// holds at a section's opening bar (a key written INSIDE a part cell is a modulation
    /// this reader does not see; the page's degrees follow it, the twin's do not).</summary>
    private (int TonicStep, int Sharps) ChordKeyFor(string sectionName)
    {
        var key = _sectionHeaders.Keys.GetValueOrDefault(sectionName);
        if (key == null || key.IsCustom)
            return (_homeTonic.Step, _homeKeySharps);
        return (KeyTonic.Of(key).Step, KeySpelling.SharpsFor(key.Pitch.PitchName, key.Mode.Text) ?? 0);
    }

    /// <summary>The pickup a section's header declares (<c>partial 4</c>), or null: the
    /// length of the FIRST bar of that section's play, which the chord track has to write
    /// as short as the music does or every later chord lands late.</summary>
    private Fraction? ChordPickupFor(string sectionName)
        => _sectionHeaders.Partials.GetValueOrDefault(sectionName)?.ToFraction();

    /// <summary>
    /// The meter of each bar of a section's play, as its MUSIC writes it — the length a chord
    /// row's bar must have to stay with it. The section starts on its header's <c>time</c>, or
    /// the score's (a section resets the meter, as the page's collector does), and each
    /// <c>time</c> inside the section's first music part moves it from the bar it stands in.
    /// Null when the section has no music part to read.
    /// </summary>
    /// <remarks>
    /// Read off the first part block that is not a chord row, in document order, one voice
    /// branch only (a <c>voice { } { }</c> writes each bar once per voice). A <c>time</c>
    /// written only inside a phrase the part references is not seen — the section keeps the
    /// meter it had — and a <c>repeat</c>'s body counts its bars once; past the bars read, the
    /// last meter read holds.
    /// </remarks>
    private List<Fraction>? SectionBarMeters(SectionDeclarationSyntax section, string sectionName)
    {
        var start = _bars.HomeMeter.Length;
        if (_sectionHeaders.Times.GetValueOrDefault(sectionName) is { IsSenzaMisura: false } headerTime)
            start = new Fraction(headerTime.Beats, headerTime.BeatType);

        // The music of the name: in the section given, else in any declaration of the name
        // — a by-part chord track (`chords prog { section Bridge { … } }`) registers its OWN
        // inner section, which holds no part block, and until 2026-10-03 that read as "no
        // meters" and the bars fell back to the SCORE's: a row under a 3/4 Bridge wrote
        // `d1:m` and LilyPond's bar check failed there (Lab corpora/dogfood/collide,
        // sessions/p767/twin). With no music to read at all, the header's meter holds.
        PartBlockSyntax? part = FirstPartBlock(section);
        if (part is null)
            foreach (var declaration in _allSections)
                if (declaration.SectionName == sectionName && FirstPartBlock(declaration) is { } other)
                {
                    part = other;
                    break;
                }
        if (part is null)
            return [start];

        var meters = new List<Fraction>();
        var current = start;
        bool pendingNotes = false;
        void Walk(SyntaxNode node)
        {
            // A voice span: its first branch only.
            if (node.SlotCount > 0 && node.GetChild(0) is SyntaxTokenNode { Kind: SyntaxKind.VoiceKeyword })
            {
                for (int i = 1; i < node.SlotCount; i++)
                    if (node.GetChild(i) is MusicBlockSyntax branch) { Walk(branch); return; }
                return;
            }
            for (int i = 0; i < node.SlotCount; i++)
            {
                switch (node.GetChild(i))
                {
                    case TimeSignatureSyntax { IsSenzaMisura: false } t:
                        current = new Fraction(t.Beats, t.BeatType);
                        break;
                    case BarlineSyntax:
                        meters.Add(current);
                        pendingNotes = false;
                        break;
                    case SyntaxTokenNode:
                        break;
                    case SyntaxNode child:
                        pendingNotes = true;
                        Walk(child);
                        break;
                }
            }
        }
        Walk(part);
        if (pendingNotes)
            meters.Add(current);
        return meters.Count > 0 ? meters : [start];
    }

    private static PartBlockSyntax? FirstPartBlock(SectionDeclarationSyntax section)
    {
        foreach (var node in section.DescendantNodes())
            if (node is PartBlockSyntax pb)
                return pb;
        return null;
    }

    /// <summary>Every section declaration of the book in document order (the caller's
    /// <c>allSections</c>), for <see cref="SectionBarMeters"/> to find the music of a name
    /// whose chord row was registered under its own inner section.</summary>
    private List<SectionDeclarationSyntax> _allSections = new();

    /// <summary>
    /// One chord container's bars as stream items: each bar's slots pre-spelled into ONE
    /// <see cref="ChordBarMarker"/>, its written bar lines kept as the nodes they are — so
    /// <see cref="EmitMusicStream"/> groups an inline <c>|: … :|</c> exactly as it does for
    /// music, and a form ending rebuilt by <see cref="CreateEnding"/> carries the bars in
    /// its green. A bar line with nothing before it closes an EMPTY bar (the page's
    /// bare-barline rule, ChordNameCollector.CollectPart's ProcessRun: the leading <c>|</c>
    /// of a pickup book included), written as a silent bar of the meter — or of the
    /// <paramref name="pickup"/> when it is the play's first.
    /// </summary>
    private IEnumerable<SyntaxNode> ChordBars(SyntaxNode container, (int TonicStep, int Sharps) key,
        Fraction? pickup = null, IReadOnlyList<Fraction>? meters = null)
    {
        IEnumerable<SyntaxNode> items = container switch
        {
            ChordPartBlockSyntax block => block.Items,
            SectionDeclarationSyntax section => Svg.Collector.ChordNameCollector.SectionItems(section),
            _ => Enumerable.Empty<SyntaxNode>(),
        };
        var result = new List<SyntaxNode>();
        var pending = new List<SyntaxNode>();
        bool firstBar = true;
        int barIndex = 0;
        void Flush()
        {
            // The bar is as long as the MUSIC's bar at this point of the section (see
            // SectionBarMeters): a chord row written under a `time 7/8` bar still said `a1:m`,
            // a whole 4/4 bar, and LilyPond's bar check failed there (Lab probes/complex-lys/06).
            var home = _bars.HomeMeter.Length;
            var barMeter = meters is { Count: > 0 }
                ? meters[Math.Min(barIndex, meters.Count - 1)] : home;
            barIndex++;
            var barLength = firstBar && pickup is { } p ? p : barMeter;
            result.Add(new ChordBarMarker(pending.Count == 0
                ? "s" + ChordModeDuration(barLength)
                : ChordBarText(pending, key, barLength)));
            pending.Clear();
            firstBar = false;
        }
        foreach (var item in items)
        {
            if (item is BarlineSyntax)
            {
                Flush();
                result.Add(item);
            }
            else if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
            {
                pending.Add(item);
            }
        }
        if (pending.Count > 0)
            Flush();
        return result;
    }

    /// <summary>
    /// One bar's slots as <c>\chordmode</c> entries: the page's own grouping
    /// (<see cref="Svg.Collector.ChordNameCollector.SlotGroups"/> — an entry with its
    /// trailing '.' extensions is one entry of the merged length, a bar-head '.' a silent
    /// <c>s</c>), each with its duration written out. <c>r</c> / <c>R</c> go out as <c>r</c>
    /// — LilyPond prints its <c>noChordSymbol</c> there, "N.C." by default
    /// (LILYPOND-REF: ly/engraver-init.ly:943-952 chordNameFunction = #ignatzek-chord-names …
    /// noChordSymbol = "N.C."), the page's own text. (A
    /// chord row has no <c>s</c> slot since 2026-09-04, LYS1028.) The grid is the score
    /// meter's, as the page reads it.
    /// </summary>
    private string ChordBarText(List<SyntaxNode> slots, (int TonicStep, int Sharps) key, Fraction barLength)
    {
        // The page grids every bar on the meter (ChordRhythm); a PICKUP bar is shorter, and
        // its slots are scaled to it so the chords keep their share of the bar they stand in.
        var meter = _bars.HomeMeter.Length;
        var sb = new StringBuilder();
        foreach (var (node, _, gridDur) in Svg.Collector.ChordNameCollector.SlotGroups(
                     slots, _bars.HomeMeter.Beats, _bars.HomeMeter.BeatType, out _))
        {
            var dur = barLength.Equals(meter) ? gridDur : gridDur * barLength / meter;
            if (sb.Length > 0)
                sb.Append(' ');
            string d = ChordModeDuration(dur);
            switch (node)
            {
                case ChordEntrySyntax entry:
                    // In the FretBoards track a chord the page draws no diagram for is a
                    // silent slot: LilyPond would compute a diagram of its own for a chord.
                    if (!_fretTrack)
                        sb.Append(ChordModeEntry(entry.SymbolText, d, key));
                    else if (FretBoardPrefix(entry, key) is { } prefix)
                        sb.Append(prefix).Append(ChordModeEntry(entry.SymbolText, d, key));
                    else
                        sb.Append('s').Append(d);
                    break;
                case RestSyntax:
                    sb.Append('r').Append(d);
                    break;
                default:
                    sb.Append('s').Append(d);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// A written chord symbol as LilyPond's entry, read through the SAME three parses the
    /// page uses (ChordNameCollector.ResolveChordEntry): the absolute symbol, then a roman
    /// degree of the key in force, then a root with an unregistered quality. The last has
    /// no tone set to hand LilyPond, so the root goes out alone and the twin says so — a
    /// symbol LilyPond names differently from the page is the point of the twin, a symbol
    /// silently reduced is not.
    /// </summary>
    private string ChordModeEntry(string symbol, string duration, (int TonicStep, int Sharps) key)
    {
        if (Music.ChordStructure.TryParseChordEntry(symbol, out var parsed))
            return TwinEntry(parsed, key.Sharps, duration);
        if (Music.ChordStructure.TryParseRomanEntry(symbol, key.TonicStep, key.Sharps, out var degree))
            return TwinEntry(degree, key.Sharps, duration);

        int slash = symbol.IndexOf('/');
        string main = slash >= 0 ? symbol[..slash] : symbol;
        string? bassText = slash >= 0 ? symbol[(slash + 1)..] : null;
        if (Music.ChordStructure.TryParseSymbolPitch(main, out int step, out int alter, out string qual))
        {
            int? bassStep = null, bassAlter = null;
            if (bassText != null
                && Music.ChordStructure.TryParseSymbolPitch(bassText, out int bs, out int ba, out string rest)
                && rest.Length == 0)
            {
                bassStep = bs;
                bassAlter = ba;
            }
            var raw = new Music.ChordStructure(step, alter, Music.ChordQuality.Major,
                bassStep, bassAlter, RawSuffix: qual);
            string spelled = TwinChord(raw, key.Sharps).ToChordMode(duration);
            if (_chordWarned.Add(symbol))
                _warnings.Add($"chord '{symbol}': the quality '{qual}' has no \\chordmode spelling — "
                    + $"the twin writes the root alone ({spelled})");
            return TwinEntry(raw, key.Sharps, duration);
        }

        if (_chordWarned.Add(symbol))
            _warnings.Add($"chord '{symbol}' is not a chord symbol the twin can spell — written as a silent slot");
        return "s" + duration;
    }

    /// <summary>
    /// A slot length as a LilyPond duration: a plain value (<c>1 2 4 8 …</c>), a dotted or
    /// double-dotted one, else the whole note scaled (<c>1*5/4</c>) — LilyPond's own
    /// spelling for a length no single value has.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/parser.yy:3532-3536 duration — steno_duration multipliers: a
    ///   written duration may carry <c>*N/M</c> multipliers.
    /// </remarks>
    private static string ChordModeDuration(Fraction f)
    {
        int n = f.Numerator, d = f.Denominator;
        static bool Pow2(int x) => x > 0 && (x & (x - 1)) == 0;
        if (n == 1 && Pow2(d))
            return d.ToString();
        if (n == 3 && d >= 2 && Pow2(d))
            return (d / 2).ToString() + ".";
        if (n == 7 && d >= 4 && Pow2(d))
            return (d / 4).ToString() + "..";
        return d == 1 ? "1*" + n : "1*" + n + "/" + d;
    }
}

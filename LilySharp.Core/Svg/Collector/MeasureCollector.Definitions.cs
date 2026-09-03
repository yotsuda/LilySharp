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

using System.Collections;
using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Svg.Collector;


public sealed partial class MeasureCollector
{
    /// <summary>
    /// Collects a part's body-level grob directives (<c>part melody { override … }</c>) as
    /// staff-scoped defaults at (0,0): they apply to this part's staff for the whole part,
    /// persisting across its sections. Only DIRECT children of the part declaration are
    /// taken (a directive inside a section is walked as music instead). Runs once per part
    /// during the voice loop, where the staff index is known.
    /// </summary>
    private void CollectPartBodyOverrides(SyntaxNode root, string partName, int staffIndex)
    {
        foreach (var partDecl in root.ChildNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;
            foreach (var node in partDecl.ChildNodes())
            {
                // Direct children only; section-internal directives are walked as music.
                // Only a plain `override` is a valid part default; `revert` / `once` in a
                // part header are positional and meaningless (flagged by the validator).
                if (node is OverrideDeclarationSyntax od)
                    CollectOverride(od, 0, 0, isOnce: false, staffIndex: staffIndex);
            }
        }
    }

    /// <remarks>
    /// ⚠️ The RETURNED <c>octave</c> AND <c>explicitOctave</c> ARE NOT THE SAME
    /// QUANTITY and the caller must not use one for the other. <c>octave</c> is the RELATIVE
    /// mode's anchor and folds in the instrument preset (explicit &gt; preset &gt; clef, the
    /// chain InstrumentDefaults.AnchorOctave spells); <c>explicitOctave</c> is only what the
    /// part WROTE, and it is all that ABSOLUTE mode may see
    /// (InstrumentDefaults.AbsoluteBaseOctave). Folding them was a real defect until
    /// 2026-08-02: the preset's octave reached the absolute base, so `octave absolute` was
    /// not absolute at all. MEASURED then, one `c4` per part:
    ///   instrument bass   drew C3 and sounded C3 — the preset's −1 octave silently CANCELLED
    ///                     the instrument's own −12, so a bass sounded what it printed.
    ///   instrument flute  drew C5 and sounded C4 — a −12 on a non-transposing instrument.
    ///   instrument tuba   drew C2 and sounded C4 — the two shifts ADDED, to +24.
    ///   instrument guitar drew C4 and sounded C3 — the only correct one, and correct because
    ///                     its octave rides a treble_8 CLEF and never went through here.
    /// The written→sounding shift is one mode-independent quantity
    /// (PartHeaderDefaults.SoundingShiftSemitones); only the ANCHOR is per-mode, which is what
    /// the two modes are for. See AbsoluteModeAnchorTests.
    /// </remarks>
    /// <param name="scoreConcert">Whether the score being collected prints at concert pitch
    /// (<see cref="ScoreConcert"/>): its shift is composed onto the part's transpose here,
    /// beside the part's own, so ApplyTranspose sees one interval.</param>
    // NOTE (cross-edit resume): the part-level config reads that seed a walk's entry
    // state — GetPartDefaults (clef/instrument/octave/transpose/header key) and
    // CollectPartBodyOverrides — are plan-time-checkable constants, verified by
    // CollectResumePlanner.WindowRespectsTopLevel (every part declaration's
    // non-section direct children must be content- and position-stable across the
    // edit), NOT folded into MaxSourceRead. See ProcessSection's matching note.
    private static (string? clef, int? octave, int? explicitOctave, (int step, int alt, int oct)? transpose, int clefPos, KeySignatureSyntax? key) GetPartDefaults(SyntaxNode root, string partName, bool scoreConcert)
    {
        foreach (var partDecl in root.ChildNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;

            string? clef = null;
            string? instrument = null;
            int? octave = null;
            int clefPos = 0;
            (int step, int alt, int oct)? transpose = null;

            // A part-header key (`part p { key bes major … }`) is this part's default
            // key — applied per-part below, not folded into the global (file) key.
            KeySignatureSyntax? partKey = partDecl.ChildNodes()
                .OfType<KeySignatureSyntax>().FirstOrDefault();

            // Check properties for clef, instrument, octave, and transpose
            foreach (var prop in partDecl.Properties)
            {
                var propName = prop.NameToken.Text.ToLowerInvariant();
                var valueToken = prop.GetChild(2) as SyntaxTokenNode;
                if (valueToken == null) continue;

                if (propName == "clef")
                {
                    clef = valueToken.Text.ToLowerInvariant();
                    // The VALUE, not the property name: a clicked clef puts the caret
                    // on what it says (`clef: |bass`), the same rule the top-level
                    // `clef` and the time signature follow.
                    clefPos = valueToken.Span.Start;
                }
                else if (propName == "instrument")
                {
                    // Join ALL value tokens — a hyphenated preset ("electric-bass")
                    // is word+minus+word in the green tree, so child(2) alone is just
                    // "electric" and would fall through to the default treble clef.
                    var texts = new List<string>();
                    for (int vi = 2; vi < prop.SlotCount; vi++)
                        if (prop.GetChild(vi) is SyntaxTokenNode vt)
                            texts.Add(vt.Text);
                    instrument = InstrumentDefaults.SplitInstrument(texts).Preset.ToLowerInvariant();
                }
                else if (propName == "octave" && prop.Value?.AsInt is int oct)
                    // Read off the typed value instead of reparsing the first token —
                    // the three branches of this loop used to interpret the same node
                    // three different ways (docs/VALUE_SITE_AUDIT.md §1.1 A3).
                    octave = oct;
            }

            // The part's transpose (own, else the file default, with a concert-pitch FILE's
            // instrument shift inside it — PartTranspose.Read), then a concert-pitch SCORE's
            // shift back to sounding pitch on top: with both, a transposing part prints
            // what the letters say, as ConcertPitch's table spells out.
            transpose = PitchTransposer.NullIfIdentity(PitchTransposer.Compose(
                PartTranspose.Read(root, partName),
                ConcertPitch.OutputShift(scoreConcert, partDecl)));

            // Resolve clef: explicit > instrument > null
            string? resolvedClef = clef;
            int? resolvedOctave = octave;

            if (instrument != null)
            {
                var (defaultClef, defaultOctave) = InstrumentDefaults.GetDefaults(instrument);
                resolvedClef ??= InstrumentDefaults.ClefWord(defaultClef);
                resolvedOctave ??= defaultOctave;
            }

            return (resolvedClef, resolvedOctave, octave, transpose, clefPos, partKey);
        }

        return (null, null, null, null, 0, null);
    }

    // Applies a part-header key as THIS part's written key: mirrors the global-key
    // walk (see the KeySignatureSyntax case in CollectDefinitions) but scoped to the
    // part being collected. Returns the written (pre-transpose) sharp count so the
    // caller can transpose it like it would the global key.
    private void ApplyPartHeaderKey(KeySignatureSyntax key)
    {
        _meta.KeySharps = key.IsCustom ? 0 : CalculateKeySharps(key);
        if (!key.IsCustom)
        {
            _meta.KeyTonicStep = Math.Max(0,
                LilySharp.Core.Music.KeySpelling.StepOf(key.Pitch.PitchName[0]));
            _meta.KeyTonicAlter = key.Pitch.AccidentalOffset;
        }
        _meta.KeyCustom = key.IsCustom ? KeySignature.EncodeCustom(key.CustomAlterations) : null;
        _meta.KeyPosition = KeyDataPos(key);
    }

    private void CollectDefinitions(SyntaxNode root)
    {
        _root = root;
        List<DrummapDeclarationSyntax>? drummaps = null;

        // A top-level `clef`/`key`/`time`/`tempo` is unconditionally the FILE DEFAULT.
        // It used to depend on whether bare music had already streamed past (the whole
        // point of the retired `topLevelMusicSeen` guard): music at the top level meant a
        // later directive was that stream's mid-music change, and the same spelling
        // therefore meant "default" or "change" by position alone. Top-level music is now
        // a parse error (LYS0020), so the ambiguity — and the four ways it was got wrong —
        // cannot arise: the only mid-music directives left are inside a part/section/
        // phrase, which IsInsideMusicContent already separates.
        foreach (var node in DefinitionSites(root))
        {
            switch (node)
            {
                case DrummapDeclarationSyntax dm:
                    // Gathered here (document order) instead of a second whole-tree
                    // walk in DrumOverrides.Build(root); built after the loop — the
                    // map's readers are all in the music walk, which runs later.
                    (drummaps ??= new List<DrummapDeclarationSyntax>()).Add(dm);
                    break;

                case MetadataDeclarationSyntax metadata:
                    // A `title` / `composer` written inside a `score { … }` belongs to
                    // THAT score, not the file: it is applied below, for the score being
                    // collected only. Reading it here would make one score's header the
                    // file's and leak it into every other score (last one wins).
                    if (!IsInsideRenderDeclaration(metadata))
                        CollectMetadata(metadata);
                    break;

                case FontDeclarationSyntax font:
                    // `fonts { role "FACE" … }` binds a face
                    // per kind of text. The reading is shared with the validator
                    // (FontPlanReader) so the two cannot disagree about what is legal;
                    // the problems it reports are that validator's job, not the
                    // collector's, and an entry it refused is simply absent from the plan.
                    // ⚠️ Only the UNNAMED top-level block is the file default: a named
                    // block is a declaration (read when a score references it, below),
                    // and a node inside a score is that score's reference.
                    if (font.NameToken == null && !IsInsideRenderDeclaration(font))
                        _meta.Fonts = Semantics.FontPlanReader.Read(font, out _);
                    break;

                case PaperDeclarationSyntax paper:
                    // `paper { KEY VALUE… }` sets the page's dimensions. Same contract
                    // as fonts: the reading is shared with PaperValidator, the problems
                    // are that validator's job, and a refused entry is simply absent
                    // from the overlay. Same named/reference guard as fonts.
                    if (paper.NameToken == null && !IsInsideRenderDeclaration(paper))
                        _meta.Paper = Semantics.PaperPlanReader.Read(paper, out _);
                    break;

                case TempoDeclarationSyntax tempoDecl:
                    // Only the top-level (initial) tempo sets the score default;
                    // mid-music tempo changes are handled in the music stream
                    // (a Tempo MusicMark at the change point).
                    if (!IsInsideMusicContent(tempoDecl))
                        CollectTempo(tempoDecl);
                    break;

                case TimeSignatureSyntax timeSig:
                    // Only the top-level (initial) time sets the global default;
                    // mid-music changes are handled in the music stream (a
                    // TimeSignatureChangeItem re-arms the per-measure length).
                    if (!IsInsideMusicContent(timeSig))
                    {
                        _meta.TimeBeats = timeSig.Beats;
                        _meta.TimeBeatsText = timeSig.BeatsText;
                        _meta.TimeSenzaMisura = timeSig.IsSenzaMisura;
                        _meta.TimeBeatType = timeSig.BeatType;
                        _meta.TimePosition = TimeDataPos(timeSig);
                    }
                    break;

                case KeySignatureSyntax key:
                    // Only process top-level key declarations (not inside phrases/sections).
                    if (!IsInsideMusicContent(key))
                    {
                        _meta.KeySharps = key.IsCustom ? 0 : CalculateKeySharps(key);
                        if (!key.IsCustom)
                        {
                            _meta.KeyTonicStep = Math.Max(0,
                                LilySharp.Core.Music.KeySpelling.StepOf(key.Pitch.PitchName[0]));
                            _meta.KeyTonicAlter = key.Pitch.AccidentalOffset;
                        }
                        _meta.KeyCustom = key.IsCustom
                            ? KeySignature.EncodeCustom(key.CustomAlterations)
                            : null;
                        _meta.KeyPosition = KeyDataPos(key);
                    }
                    break;

                case ClefDeclarationSyntax clef:
                    // Only a TOP-LEVEL `clef` declares the file default. A `clef` written
                    // inside a phrase / section is a mid-music change, engraved from its
                    // own position by the music walk (MeasureCollector.MusicWalk) — letting
                    // it land here made it the file default too, so a part that declared no
                    // clef of its own started in the CHANGED clef (wrong system-start glyph
                    // and wrong default octave, since Phase 1.5 derives both from _meta.Clef).
                    // The neighbouring key / octave / partial cases already guard this way.
                    if (!IsInsideMusicContent(clef))
                    {
                        _meta.Clef = clef.ClefName.Text.ToLowerInvariant();
                        _meta.ClefPosition = clef.ClefName.Span.Start;
                    }
                    break;

                case OctaveDirectiveSyntax octaveDir:
                    // A top-level `octave absolute/relative` sets the file default;
                    // mid-music switches are handled in the music stream.
                    if (!IsInsideMusicContent(octaveDir))
                        _octave.OctaveAbsolute = octaveDir.IsAbsolute;
                    break;

                case PartialDeclarationSyntax partialDecl:
                    // A top-level `partial N` declares the pickup once for every
                    // part (grammar feedback: writing it in each voice repeated a
                    // fact of the piece). Mid-music `partial` stays per voice.
                    if (!IsInsideMusicContent(partialDecl))
                        _filePartial = partialDecl.ToFraction();
                    break;

                case SectionDeclarationSyntax section:
                    // A section INSIDE a `chords` / `lyrics` block is that track's cell,
                    // not a structure section: it must not become a structure
                    // ordering/label rep or a part cell (its body is chord entries or
                    // syllables, not music). The chord/lyric collectors read it via
                    // ChordPartBlockSyntax.Sections / LyricsBlockSyntax.Sections.
                    if (IsInsidePartMajorTrack(section))
                        break;
                    // First declaration of a name wins as the order/label
                    // representative (source order), so a name appearing in both
                    // forms stays stable.
                    if (!_sectionState.Sections.ContainsKey(section.SectionName))
                        _sectionState.Sections[section.SectionName] = section;
                    // Part-major: an inner section binds its music to the part it
                    // lives in. Record the (section, part) cell for voice lookup.
                    var owningPart = EnclosingPartName(section);
                    if (owningPart != null)
                        _sectionState.PartMajorCells[(section.SectionName, owningPart)] = section;
                    // A section that carries its own key / time / tempo but no inline
                    // music applies those to every part of the section: section-major
                    // (`section A { key g major  melody { … } }`) or a standalone
                    // part-major header (`section A { key g major }`). An inline-music
                    // section walks the directives as music, so it is excluded to avoid a
                    // double application. First one wins.
                    if (!SectionHasInlineMusic(section))
                    {
                        var nm = section.SectionName;
                        if (FirstDirect<KeySignatureSyntax>(section) is { } hk && !_sectionHeaderKeys.ContainsKey(nm))
                            _sectionHeaderKeys[nm] = hk;
                        if (FirstDirect<TimeSignatureSyntax>(section) is { } ht && !_sectionHeaderTimes.ContainsKey(nm))
                            _sectionHeaderTimes[nm] = ht;
                        if (FirstDirect<TempoDeclarationSyntax>(section) is { } htp && !_sectionHeaderTempos.ContainsKey(nm))
                            _sectionHeaderTempos[nm] = htp;
                        if (FirstDirect<PartialDeclarationSyntax>(section) is { } hp && !_sectionHeaderPartials.ContainsKey(nm))
                            _sectionHeaderPartials[nm] = hp;
                    }
                    break;

                case FormDeclarationSyntax form:
                    // A score binds its form by name (from the RenderSpec). When a
                    // path doesn't specify one (single-staff Collect, exporters),
                    // fall back to the PRIMARY form: `main` if present, else the
                    // first declared. (`main` is matched case-sensitively.)
                    if (form.NameText == "main" || _form == null)
                        _form = form;
                    break;

                case VariableDeclarationSyntax varDecl:
                    _variables[varDecl.Name.Text] = varDecl.Expression;
                    break;

                case PhraseDeclarationSyntax phraseDecl:
                    _variables[phraseDecl.Name.Text] = phraseDecl.Body;
                    break;
            }
        }

        _drumOverrides = drummaps == null ? null : DrumOverrides.Build(drummaps);

        // A STRUCTURED file (a form or sections) can carry a top-level override / revert /
        // once (grammar §2.1 lists them as TopLevelItems). Such a directive sits OUTSIDE
        // the music stream — the per-voice walk runs through sections and never reaches a
        // root-level directive — so it is a document-wide default: seed it here at the
        // first item (measure 0, item 0) so it is active from the first note of every
        // voice. A BARE-music file has no such outer scope: there the overrides ARE the
        // music stream (a mid-stream override's position matters) and the fallback walk in
        // CollectMeasures collects them, so this is skipped to avoid double-counting.
        if (_form != null || _sectionState.Sections.Count > 0)
        {
            foreach (var node in root.ChildNodes())
            {
                // Only true top-level items; in-section overrides are walked.
                // Only a plain `override` is a valid global default. `revert` / `once` here
                // are positional and have no effect at the structural top level — flagged by
                // RevertContextValidator — so they are not collected.
                if (node is OverrideDeclarationSyntax od)
                    CollectOverride(od, 0, 0, isOnce: false, staffIndex: null); // global = all staves
            }
        }

        // LAST: the score being collected restates the header for itself
        // (`score sub { title "Violin I" … }`). After the file-level walk so it WINS,
        // and only for this render — the walk above skipped every render-scoped
        // metadata, so no score's header can reach another. A score that restates
        // one of the two keeps the file's other.
        if (!HeaderOverrides.IsDefaultOrEmpty)
            foreach (var meta in HeaderOverrides)
                CollectMetadata(meta);

        // The score's fonts/paper references ride the same road, and RESOLVED values
        // land in _meta — deliberately, for the incremental gate: MetaMatchesShifted
        // compares _meta, so an edit inside a referenced named block changes what is
        // compared and forces the recollect it needs. A reference REPLACES the file's
        // unnamed default (the audit's decision: no hidden three-layer chain); one
        // that resolves to nothing keeps it, refused all the way through.
        if (FontsOverride is { } fontsRef)
            _meta.Fonts = Semantics.FontPlanReader.ReadReference(root, fontsRef, _meta.Fonts);
        if (PaperOverride is { } paperRef)
            _meta.Paper = Semantics.PaperPlanReader.ReadReference(root, paperRef, _meta.Paper);
    }

    /// <summary>True for exactly the node kinds <see cref="CollectDefinitions"/>'s
    /// switch consumes — a kind missing here silently skips its case, so the list
    /// must track the switch (the full suite plus the snapshot books are the net:
    /// every fixture book exercises the file defaults).</summary>
    private static bool IsDefinitionKind(SyntaxKind kind) => kind is
        SyntaxKind.MetadataDeclaration or SyntaxKind.FontDeclaration
        or SyntaxKind.PaperDeclaration
        or SyntaxKind.TempoDeclaration or SyntaxKind.TimeSignature
        or SyntaxKind.KeySignature or SyntaxKind.ClefDeclaration
        or SyntaxKind.OctaveDirective or SyntaxKind.PartialDeclaration
        or SyntaxKind.SectionDeclaration or SyntaxKind.FormDeclaration
        or SyntaxKind.VariableDeclaration or SyntaxKind.PhraseDeclaration
        or SyntaxKind.DrummapDeclaration;

    /// <summary>
    /// The definitions walk's node source: every node of exactly the kinds the
    /// <see cref="CollectDefinitions"/> switch consumes, in the same pre-order
    /// <see cref="SyntaxNode.DescendantNodes()"/> yields them. Walks the GREEN
    /// tree and materializes a red node only at a match — through the parent
    /// chain's <see cref="SyntaxNode.GetChild"/>, so the yielded node carries its
    /// full Parent chain and every ancestor guard the case bodies run
    /// (IsInsideMusicContent, IsInsideRenderDeclaration, IsInsidePartMajorTrack,
    /// EnclosingPartName) works unchanged.
    /// </summary>
    /// <remarks>
    /// WHY (session 152, red-creation counters in HANDOFF §1): after the splice
    /// machinery this walk was the keystroke path's first whole-tree RED walk —
    /// materializing every red wrapper of the edited tree just to visit nodes the
    /// switch immediately ignores. The green walk visits the SAME node set (every
    /// green, tokens included, in the same order — there is no pruning decision
    /// to drift, HANDOFF §2C ⑴'s skip-list lesson) and pays a red spine only per
    /// match. ⚠️ The red-materialization cost this stops paying does not vanish
    /// for free: the next whole-tree red walker (the music walk's flat-list
    /// gather, ProcessMusicContainer) inherits first-touch creation for whatever
    /// it enumerates — measured and priced in HANDOFF §1 session 152.
    /// </remarks>
    private static IEnumerable<SyntaxNode> DefinitionSites(SyntaxNode root)
        => root.GreenSites(static g => (IsDefinitionKind(g.Kind), Descend: true));

    private void CollectMetadata(MetadataDeclarationSyntax metadata)
    {
        var keyword = metadata.Keyword.ToLowerInvariant();
        var values = metadata.Values.ToList();

        switch (keyword)
        {
            case "title":
                if (values.Count > 0 && values[0] is SyntaxTokenNode titleToken)
                {
                    _meta.Title = titleToken.Text.Trim('"');
                    _meta.TitlePosition = titleToken.Span.Start;
                }
                break;
            case "composer":
                if (values.Count > 0 && values[0] is SyntaxTokenNode composerToken)
                {
                    _meta.Composer = composerToken.Text.Trim('"');
                    _meta.ComposerPosition = composerToken.Span.Start;
                }
                break;
        }
    }

    /// <summary>
    /// Where a <c>tempo</c> declaration's metronome mark points its data-pos: at the
    /// declaration's FIRST VALUE, so clicking the mark in the preview lands on the
    /// thing worth editing rather than on the keyword —
    /// <c>tempo "|Moderato" 4 = 92</c> and <c>tempo |4 = 92</c>.
    /// </summary>
    /// <remarks>
    /// The caret lands INSIDE a marking's quotes for free: a string value's own span
    /// starts at the opening quote, and the editor's jump steps over one (the same
    /// rule that puts the caret inside a title's string). Falls back to the keyword
    /// for a declaration with no values at all.
    /// <para>
    /// ⚠️ A TOKEN's Span.Start, never the declaration's Position or Span. Trivia hangs
    /// off the TOKEN here, so the declaration's own span still starts at the newline
    /// in front of it — measured: `tempo` sits at 111 in test/notes.lys and both
    /// Position and Span.Start reported 110. The editor's jump steps over spaces and
    /// tabs but deliberately never crosses a newline, so that landed a line short.
    /// </para>
    /// </remarks>
    private static int TempoDataPos(TempoDeclarationSyntax tempoDecl)
        => tempoDecl.Values.FirstOrDefault()?.Span.Start
           ?? tempoDecl.TempoKeyword.Span.Start;

    /// <summary>
    /// Where a <c>time</c> declaration's meter points its data-pos: at the NUMERATOR,
    /// so clicking the time signature in the preview lands on the value —
    /// <c>time |4/4</c>. Same rule as <see cref="TempoDataPos"/>, and the same reason
    /// for reading a TOKEN's span: the declaration's own span starts at the trivia in
    /// front of it, which would put the caret a line short.
    /// </summary>
    private static int TimeDataPos(TimeSignatureSyntax timeSig)
        => timeSig.Numerator.Span.Start;

    /// <summary>
    /// Where a <c>key</c> declaration's signature points its data-pos: at the TONIC
    /// (<c>key |f major</c>), or at the <c>custom</c> word for a custom signature.
    /// Same rule as <see cref="TempoDataPos"/> and <see cref="TimeDataPos"/>, and the
    /// same reason for reading a TOKEN's span rather than the declaration's.
    /// </summary>
    private static int KeyDataPos(KeySignatureSyntax key)
        => key.GetChild(1) switch
        {
            PitchSyntax pitch => pitch.PitchToken.Span.Start,  // key f major
            SyntaxTokenNode word => word.Span.Start,           // key custom …
            _ => key.KeyKeyword.Span.Start,
        };

    private void CollectTempo(TempoDeclarationSyntax tempoDecl)
    {
        // Every written form reaches the opening mark: `tempo 120`,
        // `tempo "Grave"`, `tempo "Grave" 120`, `tempo "Grave" 4 = 54`,
        // `tempo "Lively" 4. = 116`. The text form used to be dropped
        // silently (only a bare leading integer was read).
        //
        // ⚠️ Read the run ONCE. This method used to hold a SIXTH reading of it — a
        // step back from the `=` over the dot tokens plus a regex on the token before
        // them — beside the five on the syntax node. The two beat-unit readings
        // disagreed: on `tempo "x" = 90` this one matched nothing and silently left
        // whatever the PREVIOUS tempo had put in _meta, while TempoValue.BeatUnit says
        // a quarter, which is what the '=' with no unit means.
        var tempo = tempoDecl.Value;
        if (tempo.Bpm is int bpm)
            _meta.Tempo = bpm;
        if (tempo.Marking is string marking)
            _meta.TempoText = marking;
        _meta.TempoPosition = TempoDataPos(tempoDecl);
        // No '=' means no beat unit was written, so the standing one stays.
        if (tempo.BeatUnit is int unit)
        {
            _meta.TempoBeatUnit = unit;
            _meta.TempoDots = tempo.BeatDots;
        }
        if (tempo.SwingSubdivision != 0)
            _meta.SwingSubdivision = tempo.SwingSubdivision;
    }

    private int CalculateKeySharps(KeySignatureSyntax key)
    {
        // PitchName already includes accidental suffix (e.g., "bes", "fis")
        return LilySharp.Core.Music.KeySpelling.SharpsFor(
            key.Pitch.PitchName, key.Mode.Text) ?? 0;
    }

    /// <summary>
    /// Gets the expected alteration for a pitch step based on the current key signature.
    /// </summary>
    private int GetKeySignatureAlteration(int step)
    {
        if (_meta.KeyCustom != null)
        {
            foreach (var (s, a) in KeySignature.DecodeCustom(_meta.KeyCustom))
                if (s == step)
                    return a;
            return 0;
        }
        return LilySharp.Core.Music.KeySpelling.Alteration(step, _meta.KeySharps);
    }

    /// <summary>The accidental glyph name ("doubleSharp" / "sharp" / "natural" / "flat" /
    /// "doubleFlat") the current key signature dictates for diatonic <paramref name="step"/>.
    /// Forced onto a note that shows none when it is made a courtesy or editorial accidental.</summary>
    private string KeySignatureAccidentalName(int step) => GetKeySignatureAlteration(step) switch
    {
        >= 2 => "doubleSharp", 1 => "sharp", <= -2 => "doubleFlat", -1 => "flat", _ => "natural"
    };

    /// <summary>
    /// Determines the displayed accidental for a pitch using LilyPond's default
    /// accidental style: an accidental is printed when the pitch's alteration
    /// differs from the one currently IN EFFECT for that (step, octave) within
    /// the measure. The in-effect value starts at the key signature each measure
    /// and is updated by every engraved note, so a sharp/flat persists to the
    /// barline (a later same-pitch note in the measure needs no repeat, and a
    /// return to the key value prints a cancelling natural). Memory is
    /// octave-specific and resets at the barline (MeasureBuilder.MeasureCompleted).
    /// Explicit @courtesy is layered on at the call site. Verified against
    /// LilyPond 2.24.4.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/accidental-engraver.cc — default style.</remarks>
    // Takes the DISPLAY pitch (post-transpose): diatonic step (0–6), its
    // accidental in semitones, and octave.
    private string? GetDisplayAccidental(int step, int actual, int octave)
    {
        var key = (step, octave);
        // In effect: a prior accidental on this exact pitch this measure, else
        // the key signature. A mid-measure key change updates the latter for
        // pitches not yet altered this measure (GetKeySignatureAlteration reads
        // the live key) without disturbing remembered alterations.
        int inEffect = _measureAccidentals.TryGetValue(key, out int remembered)
            ? remembered
            : GetKeySignatureAlteration(step);

        // Remember this pitch's alteration for the rest of the measure.
        _measureAccidentals[key] = actual;

        if (actual == inEffect)
            return null;

        // RESTORE-FIRST: stepping DOWN within the same sign (𝄪→♯, 𝄫→♭) prepends a
        // natural to the printed accidental. The default accidental style reads
        // extraNatural = #t, which is what gates the restore onto the grob — Lily#
        // ports only that default style, so the gate is constant here.
        // LILYPOND-REF: scm/music-functions.scm:1746-1752 check-pitch-against-signature —
        //   need-restore = this-alt ≠ 0 ∧ |this-alt| < |prev-alt| ∧ prev-alt·this-alt > 0;
        // LILYPOND-REF: scm/music-functions.scm:1909-1911 accidental-styles `default`
        //   (extraNatural #t); lily/accidental-engraver.cc:272-275 — restore-first is set
        //   only when extraNatural holds.
        // The composite travels as a NAME ("naturalSharp"/"naturalFlat") so every box,
        // skyline and draw consumer reads the composed stencil through the same pipes a
        // plain glyph takes — see GlyphMetrics.RestoreMainOf.
        bool restore = actual != 0
            && Math.Abs(actual) < Math.Abs(inEffect)
            && inEffect * actual > 0;

        return actual switch
        {
            2 => "doubleSharp",
            1 => restore ? "naturalSharp" : "sharp",
            0 => "natural",
            -1 => restore ? "naturalFlat" : "flat",
            -2 => "doubleFlat",
            _ => null
        };
    }

}

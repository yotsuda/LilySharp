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

// The parts: which names a score plays, the per-part switches (drums, string numbers, stroke
// fingers, pedals, ottava), a part's music variable (EmitPartVariable), and the part-level
// readings (clef word, preset, transposition, tuning). Split out of LilyPondExporter.cs as a
// partial class (2026-10-02); same instance state, no behavior change.
public sealed partial class LilyPondExporter
{
    /// <summary>
    /// What a part is transposed by, asked of the readers the page asks: the part's own
    /// <c>transpose</c> option or the file-level default
    /// (<see cref="Semantics.PartTranspose.Read(SyntaxNode, string)"/>), composed under the
    /// exported score's own <c>transpose</c> the way the collector composes it
    /// (MeasureCollector.ComposeTranspose — the part's is the INNER one).
    /// </summary>
    /// <remarks>
    /// ⚠️ "The exported score" is the FIRST one, which is what this transpiler writes; a file
    /// whose second score transposes differently has never been visible in its twin, and that
    /// is a property of exporting one score rather than of this line.
    /// ⚠️ Read through the same three houses rather than re-derived, because the three
    /// spellings of <c>transpose</c> disagreeing is a defect this repository has already had:
    /// a render block's own transpose used to be counted as the file default as well, so one
    /// construct gave three answers (PartTranspose.ReadScoreDefault's remarks).
    /// </remarks>
    private static (int step, int alt, int oct)? EffectiveTranspose(
        CompilationUnitSyntax root, string partName, RenderDeclarationSyntax? render)
    {
        var scoreTranspose = render?.Transpose is { } t
            ? Semantics.PartTranspose.ReadProperty(t)
            : null;
        // A concert-pitch FILE's instrument shift is inside PartTranspose.Read; a
        // concert-pitch SCORE's shift back to sounding pitch composes on top, as the
        // collector's GetPartDefaults composes it (Semantics.ConcertPitch).
        var scoreConcert = Semantics.ConcertPitch.OutputShift(
            Semantics.ConcertPitch.ScoreIsConcert(render),
            Semantics.ConcertPitch.FindPart(root, partName));
        return Semantics.PitchTransposer.Compose(
            Semantics.PitchTransposer.NullIfIdentity(Semantics.PitchTransposer.Compose(
                Semantics.PartTranspose.Read(root, partName), scoreConcert)),
            scoreTranspose);
    }

    /// <summary>
    /// Every part this file has music for: the declared parts, then any part a score
    /// NAMES but never declares.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <c>part</c> declaration is not what makes a part — the SCORE is. The collector
    /// takes its voice names from the render items (RenderSpec.GetVoiceNames) and looks each
    /// one up as a <c>PartBlock</c> inside the sections; a part with nothing to declare (no
    /// clef, no instrument) is simply never written down. This walked the declarations only,
    /// so such a file fell to the "no explicit part" branch below and exported the FILE-level
    /// music stream — which holds the key and the meter and no notes at all. That is the same
    /// silent shape as the loose-section hole: a valid <c>.ly</c> with a blank staff, and a
    /// twin sweep reads it as layout divergence (docs/HANDOFF.md §1 gate list ⑶,
    /// <c>test/ossia-beams</c>).
    /// </remarks>
    private static List<string> PartNames(
        List<PartDeclarationSyntax> parts, RenderDeclarationSyntax? render)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
            if (seen.Add(part.Name.Text))
                names.Add(part.Name.Text);
        if (render == null)
            return names;
        foreach (var item in RenderRows(render))
            foreach (string? name in RowPartNames(item))
                if (name != null && seen.Add(name))
                    names.Add(name);
        return names;
    }

    /// <summary>
    /// The parts a render row puts MUSIC on: a group's every staff, a staff/tab/ossia's own.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <c>chords</c> / <c>lyrics</c> row names a part too, and it is deliberately NOT
    /// here: its body is a chord or lyric block, not a music stream, so a music variable for
    /// it would be empty and the score would grow a <c>\new Staff</c> for a row this
    /// transpiler cannot write at all (test/lead-sheet has nothing else, and would export a
    /// staff of its chord part). EmitScore reports those rows instead.
    /// </remarks>
    private static IEnumerable<string?> RowPartNames(SyntaxNode item) => item switch
    {
        GrandStaffRenderSyntax group => group.Members.SelectMany(RowPartNames),
        CondensedStaffRenderSyntax condensed => SharedStaffPartNames(condensed.PartNames),
        CombinedStaffRenderSyntax combined => SharedStaffPartNames(combined.PartNames),
        OssiaRenderSyntax ossia => new[] { OssiaPartName(ossia) },
        StaffRenderSyntax or TabRenderSyntax => new[] { RenderPartName(item) },
        _ => Enumerable.Empty<string?>(),
    };

    /// <summary>The written part names of a condensed or combined staff, empty tokens
    /// (a missing name the parser recovered from) left out — RenderSpecParser's filter.</summary>
    private static IEnumerable<string?> SharedStaffPartNames(IEnumerable<string> names) =>
        names.Where(n => n.Length > 0);

    /// <summary>
    /// The score's render items, in source order — the same walk
    /// <see cref="Svg.Collector.RenderSpecParser.Parse"/> makes.
    /// </summary>
    /// <remarks>
    /// ⚠️ DESCENDANTS, not direct children. A <c>grandStaff { staff a staff b }</c> holds its
    /// staves one level down, so scanning the score's own children found NO staff in such a
    /// book and the fallback emitted a single staff for the first part — a twin missing a
    /// whole staff, which the sweep then read as layout divergence rather than as a different
    /// score (docs/HANDOFF.md §1 gate list ⑵). A staff INSIDE a group is emitted by that
    /// group, so it drops out here exactly as RenderSpecParser's
    /// <c>IsInsideGrandStaff</c> drops it.
    /// </remarks>
    private static IEnumerable<SyntaxNode> RenderRows(RenderDeclarationSyntax render)
    {
        foreach (var child in render.DescendantNodes())
        {
            switch (child)
            {
                // ⚠️ A NESTED grandStaff is emitted by the bracket that holds it (EmitStaffGroup
                // recurses), so it drops out here as a nested staff does — without this guard
                // the twin would write the grand staff twice, the second time loose.
                case GrandStaffRenderSyntax when !IsInsideGrandStaff(child):
                // ⚠️ A condensed or combined staff at the top level used to fall through here
                // and vanish WITHOUT a warning — the twin of a `combinedStaff { a b }` score
                // held its chord row and no staff at all (session 380, bench.lys).
                case CondensedStaffRenderSyntax when !IsInsideGrandStaff(child):
                case CombinedStaffRenderSyntax when !IsInsideGrandStaff(child):
                case TabRenderSyntax:
                case OssiaRenderSyntax:
                case ChordRowRenderSyntax:
                case LyricsRowRenderSyntax:
                    yield return child;
                    break;
                case StaffRenderSyntax staff when !IsInsideGrandStaff(staff):
                    yield return staff;
                    break;
            }
        }
    }

    private static bool IsInsideGrandStaff(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is GrandStaffRenderSyntax)
                return true;
        return false;
    }

    // ---- Phrases -----------------------------------------------------------

    /// <summary>
    /// Whether a part's music is DRUM music — it names drum instruments rather than pitches.
    /// </summary>
    /// <remarks>
    /// LilyPond's two vocabularies do not mix in one stream: inside <c>\drummode</c> a
    /// <c>c</c> is not a pitch, and outside it <c>hh</c> is not a drum. Lily# has no such
    /// mode — a bare identifier is a drum name wherever the registry knows it — so a part
    /// that writes both cannot be spelled at all, and saying so is better than writing a
    /// <c>.ly</c> LilyPond refuses to read.
    /// </remarks>
    /// <summary>Whether any note of the part's music carries a <c>\N</c> string number
    /// (see <see cref="_stringNumberParts"/>).</summary>
    private static bool HasStringNumbers(List<SyntaxNode> music)
    {
        foreach (var item in music)
            foreach (var n in item.DescendantNodes().Prepend(item))
                // A chord(…) item writes a string number on every note (EmitShapeChord).
                if (n is StringNumberAnnotationSyntax or ChordSyntax { IsShapeChord: true })
                    return true;
        return false;
    }

    /// <summary>Whether the part's music writes any right-hand finger (<see cref="StrokeFinger"/>).</summary>
    private static bool HasStrokeFingers(List<SyntaxNode> music)
    {
        foreach (var item in music)
            foreach (var n in item.DescendantNodes().Prepend(item))
                if (n is MusicMarkSyntax mk && StrokeFinger(mk) != null)
                    return true;
        return false;
    }

    /// <summary>Whether the part's music writes any ottava (<see cref="OttavaCommand"/>) — the
    /// argument forms and the terminator are marks, the bare word an articulation.</summary>
    private static bool HasOttavaMarks(List<SyntaxNode> music)
    {
        foreach (var item in music)
            foreach (var n in item.DescendantNodes().Prepend(item))
                if ((n is MusicMarkSyntax mk && OttavaCommand(mk.MarkName, mk.IsSpanEnd) != null)
                    || (n is ArticulationSyntax art && OttavaCommand(art.NameToken.Text, spanEnd: false) != null))
                    return true;
        return false;
    }

    /// <summary>Whether the part's music writes any piano pedal (<see cref="PedalPostEvent"/>).</summary>
    private static bool HasPedalMarks(List<SyntaxNode> music)
    {
        foreach (var item in music)
            foreach (var n in item.DescendantNodes().Prepend(item))
            {
                // A START is a bare word (an articulation), a terminator a MusicMarkSyntax.
                if (n is MusicMarkSyntax mk && PedalPostEvent(mk) != null)
                    return true;
                if (n is ArticulationSyntax art
                    && art.NameToken.Text is "sustain" or "sostenuto" or "unaCorda" or "treCorde")
                    return true;
            }
        return false;
    }

    /// <summary>
    /// The pedal-style <c>\set</c>s a pedalling part's staff opens with, or "" for a part that
    /// never pedals. ALL THREE pedals take the part's one style: Lily#'s <c>pedal</c> property
    /// is one value for sustain, sostenuto and una corda alike, where LilyPond defaults each
    /// separately — sustain and una corda to <c>'text</c>, sostenuto to <c>'mixed</c>.
    /// LILYPOND-REF: ly/engraver-init.ly, the Staff context — pedalSustainStyle (line 895),
    ///   pedalUnaCordaStyle (897), pedalSostenutoStyle (904): the defaults.
    /// </summary>
    private string PedalStyleSet(string? partName)
        => partName != null && _pedalParts.TryGetValue(partName, out var style)
            ? "\\set Staff.pedalSustainStyle = #'" + style
              + " \\set Staff.pedalSostenutoStyle = #'" + style
              + " \\set Staff.pedalUnaCordaStyle = #'" + style + " "
            : "";

    /// <summary>
    /// The <c>\set</c> a plucking part's notation staff opens with, or "" for a part that
    /// never plucks (<see cref="_strokeFingerParts"/>). Not written on a TabStaff: LilyPond's
    /// TabVoice removes New_fingering_engraver, so no StrokeFinger is ever made there
    /// (measured on 2.26.0 — the tab of test/tab-technique-letters prints no letter) where
    /// the page letters its tab too. Self-acknowledged.
    /// LILYPOND-REF: ly/engraver-init.ly:1172-1182 TabVoice — \remove New_fingering_engraver.
    /// </summary>
    private string StrokeFingerSet(string? partName)
        => partName != null && _strokeFingerParts.Contains(partName)
            ? "\\set Staff.strokeFingerOrientations = #'(down) "
            : "";

    private bool IsDrumPart(string partName, List<SyntaxNode> music)
    {
        bool drums = false, pitched = false;
        foreach (var item in music)
        {
            foreach (var n in item.DescendantNodes().Prepend(item))
            {
                switch (n)
                {
                    case DrumNoteSyntax: drums = true; break;
                    case NoteSyntax: pitched = true; break;
                    case ChordSyntax c:
                        if (c.DrumNames.Any()) drums = true;
                        if (c.Pitches.Any() || c.Degrees.Any()) pitched = true;
                        break;
                }
            }
        }
        if (drums && pitched)
        {
            _warnings.Add(
                $"part '{partName}' writes drum names and pitches in one stream, which "
                + "LilyPond's \\drummode cannot hold — the drum notes are dropped");
            return false;
        }
        return drums;
    }

    /// <summary>
    /// The effective <c>transpose</c> for the part being written: its own option, else the
    /// file-level default, composed under the exported score's own — the same question the
    /// collector asks (MeasureCollector's PartTranspose.Read composed with ScoreTranspose),
    /// asked of the same readers so there is no second spelling of the rule.
    /// </summary>
    private (int step, int alt, int oct)? _partTranspose;

    private void EmitPartVariable(string varName, List<SyntaxNode> music, CompilationUnitSyntax root)
    {
        // ⚠️ The two modes anchor DIFFERENTLY on purpose. Absolute octave is middle C
        //   whatever the clef (OctaveContext: "clef default is deliberately NOT used here"),
        //   so \fixed is always c'; relative follows the part's own default octave.
        // A drum part has no octave to anchor at all — its notes are names.
        string wrapper = _drumMode
            ? "\\drummode"
            : _octaveAbsolute
            ? "\\fixed " + AnchorPitch(_absoluteBaseOctave)
            : "\\relative " + AnchorPitch(_anchorOctave);
        // A transpose wraps the frame rather than sitting inside it: LilyPond resolves the
        // relative octaves of the WRITTEN pitches and shifts the result, which is the order
        // Lily# uses too (the collector transposes what the octave context has resolved).
        // ⚠️ It goes on the variable, not the \score, because it is per PART.
        if (TransposeTarget() is { } target)
            wrapper = "\\transpose c " + target + " " + wrapper;
        _sb.Append(varName).Append(" = ").Append(wrapper).Append(" {\n");

        // Each part starts from Lily#'s own default duration, as the collector does
        // (MeasureCollector resets _defaultDuration to a quarter per part) — but LilyPond's
        // does NOT: default_duration_ is parser state, carried from the last duration READ
        // anywhere above, the previous part's variable included. So the part's first
        // unwritten event writes its value out, as a section boundary does (below, the
        // prologue's twin of this). MEASURED (session 658, the reader's beam-slur.lys through
        // LilyPond 2.26.0): the second part's `e g b r` after the first's `… d d d d d ]`
        // (eighths) read as four EIGHTHS in the twin, where Lily# engraves quarters.
        // LILYPOND-REF: lily/parser.yy:3503-3515 optional_notemode_duration.
        _lastWrittenValue = "4";
        _lastWrittenDots = 0;
        _forceNextDuration = true;
        // Each part's music variable is its own scope - a slash run cannot stay
        // open across the boundary (the next part opened with a stray
        // improvisationOff when it did).
        _improvisationOpen = false;

        // …and from its own octave frame and the score's home key, for the same reason
        // (MeasureCollector.cs sets LastPitchName = 'c' and re-arms the ambient tonic per
        // voice). The wrapper this method just wrote IS the frame: `\relative c'` starts
        // both sides on c at the anchor octave.
        _lysStep = _lyStep = 0;
        _lysOctave = _lyOctave = _anchorOctave;
        _frameTracked = true;
        // The running key starts at LilyPond's C major; EmitScoreSettings then writes the file's
        // key and the part header's (R12⒞: _homeKeySharps counts the part header, so it is not
        // the file-level start).
        _keySharps = 0;
        _tonic = _homeTonic;
        _timeBeats = _homeTimeBeats;
        _timeBeatType = _homeTimeBeatType;
        _timeSenza = _homeTimeSenza;
        _timeText = _homeTimeNode is { IsSenzaMisura: false } homeTime ? TimeText(homeTime) : "\\time 4/4";
        _sectionHead = null;
        _heldTimeRestore = _heldKeyRestore = _heldMark = null;

        // Score-level settings (tempo/key/time live at file scope in Lily#), then the part
        // header's own key.
        EmitScoreSettings(root);
        // What a section boundary restores: the part header's key, else the file's.
        _restoreKeySharps = _keySharps;

        EmitMusicStream(music, indent: "  ");
        _sb.Append("}\n\n");
    }

    /// <summary>
    /// <see cref="_partTranspose"/> written the way LilyPond's <c>\transpose</c> takes it:
    /// the target of an interval FROM <c>c</c>, which is how Lily# spells it too. Null when
    /// the part is not transposed.
    /// </summary>
    /// <remarks>
    /// Both languages anchor the target on a bare <c>c</c>, so the octave marks carry over
    /// unchanged and nothing here does interval arithmetic: <c>transpose bes,</c> is
    /// <c>\transpose c bes,</c>, down a major second on both sides. (The usual "Lily#'s
    /// <c>c'</c> is LilyPond's <c>c''</c>" does not apply — that is about where a written
    /// pitch LANDS, and this is a difference between two pitches.)
    /// <para>
    /// MEASURED on LilyPond 2.26.0, 2026-08-17, because "the twin drops transpose" had been
    /// filed with the spelling left open (wrap, or write the sounding pitches?). Wrapping
    /// test/transpose's twin in <c>\transpose c d</c> makes LilyPond read exactly the ten
    /// pitches <c>lysc check --pitches</c> resolves for the page — D5 E5 F#5 G5 A5 B5 C#6 D7
    /// D#8 E9 — and moves the key signature with them: the KeySignature grob's
    /// alteration-alist goes from <c>()</c> to <c>((0 . 1/2) (3 . 1/2))</c>, C major to D
    /// major, which is what test/transpose's own header claims. So the spelling was not a
    /// decision; it was LilyPond's, and one command asked it.
    /// </para>
    /// ⚠️ Wrapping a <c>\drummode</c> body is a no-op rather than a hazard — MEASURED, the
    /// same drum book with and without the wrapper renders to a byte-identical SVG — so drum
    /// parts need no special case, and neither engine moves a drum name.
    /// LILYPOND-REF: ly/music-functions-init.ly:2437-2441 transpose, a define-music-function
    ///   taking (from to music) that wraps it in TransposedMusic via ly:music-transpose with
    ///   the interval (- to from) — which is why writing <c>c</c> on the left makes the target
    ///   the whole interval, the same way Lily#'s own target is measured from c.
    /// </remarks>
    private string? TransposeTarget()
        => _partTranspose is { } t
            ? SpellPitch(t.step, t.alt) + OctaveMarks(t.oct)
            : null;

    /// <summary>Arms the part header's key for the part about to be emitted (null part = the
    /// file's one music stream, which has no header).</summary>
    private void ArmPartHome(PartDeclarationSyntax? part)
    {
        _partHeaderKeyNode = ScoreHomeKey.PartHeaderDeclaration(part);
    }

    /// <summary>
    /// The tuning Lily# frets this part against: its explicit <c>tuning</c> property, else
    /// the one its <c>instrument</c> preset implies, else guitar.
    /// </summary>
    /// <remarks>
    /// The page's own precedence (RenderSpecParser.ParseTab → <c>explicit ?? property ??
    /// InstrumentDefaults.GetTuning(preset)</c>, unknown/none = guitar), asked of the same
    /// table. ⚠️ It used to fall back to BASS while the page fell back to GUITAR, so a part
    /// naming neither tuning nor instrument got a four-string bass in the twin and six
    /// strings on the page (<c>test/tab-part-key</c>) — and after the twin started writing
    /// the tab's transposition, the same wrong default moved its pitches too.
    /// ⚠️ STILL NOT READ: the render item's own tuning modifier (<c>tab bass melody</c>),
    /// which outranks both on the page. No fixture writes it, and reading it here means
    /// re-deriving the token stripping ParseTab does (<c>as numbers</c>, <c>with chords</c>);
    /// it is a known gate, not an oversight.
    /// </remarks>
    private static Syntax.TuningType TabTuningType(PartDeclarationSyntax? part)
        => Tablature.Tunings.Parse(
            (part != null ? PartProperty(part, "tuning")?.ToLowerInvariant() : null)
            ?? InstrumentDefaults.GetTuning(InstrumentPresetOf(part)));

    // The LilyPond predefined tuning name for that tuning — read from the same table that
    // holds the strings, so the twin cannot name one tuning and fret another.
    private static string TabTuning(PartDeclarationSyntax? part)
        => Tablature.Tunings.LilyPondName(TabTuningType(part));

    /// <summary>
    /// Writes the written→sounding transposition the tab frets against, so the twin's fret
    /// numbers are the page's. Asked of <see cref="Tablature.Tunings"/> — the same table
    /// <c>TabResolver</c> reads — rather than restated here.
    /// </summary>
    /// <remarks>
    /// Only whole octaves are written (<c>\transpose c c,</c>), which is every shift the
    /// tunings and clefs produce. Anything else is REPORTED rather than dropped: a twin
    /// silently fretting other pitches is the shape that hid this hole in the first place.
    /// </remarks>
    private void AppendTabTranspose(StringBuilder sb, PartDeclarationSyntax? part, string? partName)
    {
        var clef = ClefFromName(PartClefWord(part));
        int shift = Tablature.Tunings.ClefOctaveShift(clef)
                    + PartTransposition(part);
        if (shift == 0)
            return;
        if (shift % 12 != 0)
        {
            _warnings.Add($"tab part '{partName ?? "?"}' sounds {shift} semitones from what is "
                          + "written, which is not a whole octave — the twin frets the written "
                          + "pitch and its fret numbers will not be the score's");
            return;
        }
        sb.Append("\\transpose c ").Append('c')
          .Append(new string(shift < 0 ? ',' : '\'', Math.Abs(shift) / 12)).Append(' ');
    }

    /// <summary>
    /// The part's <c>instrument</c> PRESET — the bare words, lowercased — or null when the
    /// part declares no instrument (or only a quoted display label).
    /// </summary>
    /// <remarks>
    /// ⚠️ Every value token is joined, not just the first: a hyphenated preset
    /// (<c>piano-left</c>) is word+minus+word in the green tree, so
    /// <see cref="PartProperty"/> alone would read "electric" and fall through to the
    /// defaults. This is the reading MeasureCollector.GetPartDefaults takes, through the
    /// same <c>SplitInstrument</c>.
    /// </remarks>
    private static string? InstrumentPresetOf(PartDeclarationSyntax? part)
    {
        if (part == null) return null;
        foreach (var prop in part.Properties)
        {
            if (!prop.NameToken.Text.Equals("instrument", StringComparison.OrdinalIgnoreCase))
                continue;
            var texts = new List<string>();
            for (int vi = 2; vi < prop.SlotCount; vi++)
                if (prop.GetChild(vi) is SyntaxTokenNode vt)
                    texts.Add(vt.Text);
            if (texts.Count == 0) return null;
            string preset = InstrumentDefaults.SplitInstrument(texts).Preset;
            return preset.Length == 0 ? null : preset.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>
    /// The clef word this part reads in: its explicit <c>clef</c> property, else the clef its
    /// <c>instrument</c> preset implies, else null (nothing to write — LilyPond's own default
    /// is treble, and so is Lily#'s).
    /// </summary>
    /// <remarks>
    /// The page's precedence, through the same table (RenderSpecParser.GetPartClef,
    /// MeasureCollector.GetPartDefaults → <c>resolvedClef ??=
    /// InstrumentDefaults.ClefWord(GetDefaults(preset).Clef)</c>).
    /// <para>
    /// ⚠️ Reading <c>instrument</c> is still a TRANSPILATION, not the re-derivation this file
    /// refuses. The distinction is whether the <c>.lys</c> HOLDS the thing: an instrument
    /// preset is written down in the source, LilyPond simply has no spelling for it, so it is
    /// expanded into the spellings LilyPond does have — the same move degree chords needed
    /// (ChordDegrees.Resolve). What stays refused is inventing what the source never said: a
    /// phrase's auto-transpose, an interval argument, a hand <c>.ly</c>'s comments.
    /// </para>
    /// <para>
    /// ⚠️ Read WHOLE: the clef here, the octave in <see cref="AnchorOctaveOf"/>, the tuning in
    /// <see cref="TabTuningType"/> and the sounding shift in <see cref="PartTransposition"/>
    /// all come off the same preset. Any one of them alone makes the twin wrong in a way that
    /// looks right. Until this was read, ten fixtures declaring <c>instrument bass</c> and no
    /// <c>clef</c> exported a treble twin against a bass page (docs/HANDOFF.md gate ⑹).
    /// </para>
    /// <para>
    /// Neither → the file's top-level <c>clef</c> (<see cref="_fileClef"/>), the page's last
    /// step too. Until 2026-09-27 the twin never read it: seven audit books that declare
    /// <c>clef bass</c> at the top exported a treble twin (Lab sessions/p648).
    /// </para>
    /// </remarks>
    private string? PartClefWord(PartDeclarationSyntax? part)
    {
        if (part != null)
        {
            if (PartProperty(part, "clef") is string clef) return clef;
            if (InstrumentPresetOf(part) is string preset)
                return InstrumentDefaults.ClefWord(InstrumentDefaults.GetDefaults(preset).Clef);
        }
        return _fileClef;
    }

    // The file's top-level `clef` word (null = none), read once per export.
    private string? _fileClef;

    /// <summary>
    /// The part's SOUNDING transposition in semitones, excluding the octave the clef itself
    /// carries: an explicit <c>transposition</c> property (<c>8vb</c> …) &gt; the instrument
    /// preset's default (bass = −12, piccolo = +12) &gt; the tuning's default (bass tunings =
    /// −12). RenderSpecParser.ResolvePartTransposition, on the same two tables.
    /// </summary>
    private static int PartTransposition(PartDeclarationSyntax? part)
    {
        if (part == null) return 0;
        if (PartProperty(part, "transposition") is string text
            && InstrumentDefaults.ParseTranspositionSemitones(text) is int explicitShift)
            return explicitShift;
        return InstrumentPresetOf(part) is string preset
            ? InstrumentDefaults.GetTransposition(preset)
            : Tablature.Tunings.TuningTransposition(TabTuningType(part));
    }

    /// <summary>The clef word of a part header as the model's clef, for the octave it carries
    /// (<c>treble_8</c> sounds 8vb). Unknown or absent reads as treble, which carries none.</summary>
    private static Svg.Model.ClefType ClefFromName(string? name) => name?.ToLowerInvariant() switch
    {
        "bass" => Svg.Model.ClefType.Bass,
        "alto" => Svg.Model.ClefType.Alto,
        "tenor" => Svg.Model.ClefType.Tenor,
        "treble_8" => Svg.Model.ClefType.Treble8Below,
        "bass_8" => Svg.Model.ClefType.Bass8Below,
        "treble^8" => Svg.Model.ClefType.Treble8Above,
        _ => Svg.Model.ClefType.Treble,
    };

    // The first value token of a part-header property (`clef bass` → "bass").
    private static string? PartProperty(PartDeclarationSyntax part, string name)
    {
        foreach (var prop in part.Properties)
            if (prop.NameToken.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                return (prop.GetChild(2) as SyntaxTokenNode)?.Text
                       ?? prop.Values.OfType<SyntaxTokenNode>().FirstOrDefault()?.Text;
        return null;
    }

    // ---- Helpers -----------------------------------------------------------
}

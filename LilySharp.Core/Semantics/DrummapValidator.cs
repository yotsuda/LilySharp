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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports the parts of a <c>drummap { }</c> that are silently ignored: an unknown drum
/// name, an unknown key, a value outside its range, and a value word the reader does not
/// know.
/// </summary>
/// <remarks>
/// <para>
/// ★ This validator exists because the block had NO observer of any kind. Measured
/// 2026-08-15: not one of the 308 .lys on disk writes a <c>drummap</c>, and the string
/// "drummap" does not occur anywhere in the test project — while the feature itself is
/// live (a valid block moves 164 lines of a drum score's SVG). A sub-language that
/// nothing writes, nothing tests, and that answers every mistake with silence cannot be
/// given types safely: there would be no falsifier for "the accepted spelling changed".
/// So the observers come first (HANDOFF §5.0 「点が先」), and typing it is a later
/// decision (VALUE_SITE_AUDIT §9, the ⒞ item).
/// </para>
/// <para>
/// ⚠️ NOTHING is accepted or refused differently here. Every entry the reader used to
/// apply it still applies, and every entry it used to drop it still drops — the drop is
/// merely no longer silent. A block whose every part was wrong used to render exactly as
/// if it were absent, and report "No errors found"; that is the whole defect.
/// </para>
/// <para>
/// ⚠️ It reports what <see cref="DrummapDeclarationSyntax.Entries"/> surfaces, which is
/// deliberately the same walk the reader consumes (one question, one walk). Two shapes
/// therefore stay unreported, and are recorded here rather than left to be rediscovered:
/// a key written with no value after it (<c>drummap { hh: position }</c>) and tokens
/// before the first <c>name :</c> (<c>drummap { 6 hh: position 3 }</c>) are dropped by
/// that walk, so neither reaches this pass. Both are measured to change nothing today.
/// Reporting them means giving the walk a way to say "these tokens reached no entry",
/// which is a change to the reader, not to this pass.
/// </para>
/// </remarks>
internal sealed class DrummapValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>The keys <c>DrumOverrides.Build</c> reads. Anything else is dropped.</summary>
    private static readonly string[] KnownKeys = ["position", "notehead", "midi", "mark"];

    /// <summary>The notehead words it maps; anything else leaves the notehead alone.</summary>
    private static readonly string[] KnownNoteheads =
        ["x", "cross", "diamond", "triangle", "slash", "xcircle", "default"];

    public void Validate(SyntaxTree tree)
    {
        foreach (var dm in tree.GetRoot().DescendantNodes().OfType<DrummapDeclarationSyntax>())
        {
            foreach (var (name, nameSpan, settings) in dm.Entries)
            {
                if (!DrumNameRegistry.TryGet(name, out _))
                {
                    // The drum vocabulary is static: a drummap OVERRIDES the built-in
                    // table, it cannot add an instrument to it.
                    _diagnostics.Warning(nameSpan, DiagnosticCodes.DrummapEntryIgnored,
                        $"'{name}' is not a drum name — the drummap entry is ignored. "
                        + "A drummap overrides the built-in drum table, so the name must "
                        + "already be in it (e.g. 'hh', 'bd', 'sn').");
                    continue;
                }

                foreach (var (key, value) in settings)
                {
                    if (!KnownKeys.Contains(key))
                    {
                        _diagnostics.Warning(value.KeySpan, DiagnosticCodes.DrummapEntryIgnored,
                            $"'{key}' is not a drummap setting — it is ignored. "
                            + $"The settings are: {string.Join(", ", KnownKeys)}.");
                        continue;
                    }

                    switch (key)
                    {
                        case "position" when !IsIntInRange(value.Text, -9, 9):
                            _diagnostics.Warning(value.ValueSpan, DiagnosticCodes.DrummapEntryIgnored,
                                $"'position {value.Text}' is ignored — a staff position is a whole "
                                + "number from -9 to 9 (0 is the middle line, 2 the line above).");
                            break;
                        case "midi" when !IsIntInRange(value.Text, 0, 127):
                            _diagnostics.Warning(value.ValueSpan, DiagnosticCodes.DrummapEntryIgnored,
                                $"'midi {value.Text}' is ignored — a General MIDI key is a whole "
                                + "number from 0 to 127.");
                            break;
                        case "notehead" when !KnownNoteheads.Contains(value.Text.ToLowerInvariant()):
                            _diagnostics.Warning(value.ValueSpan, DiagnosticCodes.DrummapEntryIgnored,
                                $"'notehead {value.Text}' is ignored — the notehead styles are: "
                                + $"{string.Join(", ", KnownNoteheads)}.");
                            break;
                        case "mark" when value.Text.ToLowerInvariant() is not ("stopped" or "open"):
                            // ⚠️ Not merely ignored: an unrecognised word CLEARS the mark
                            // this drum already had, so the message says so. Kept as it is,
                            // because nothing was decided about changing it — see the net
                            // DrummapTests.AnUnknownMarkWord_ClearsTheMark.
                            _diagnostics.Warning(value.ValueSpan, DiagnosticCodes.DrummapEntryIgnored,
                                $"'mark {value.Text}' is not a drum mark, so the drum is left with "
                                + "no mark at all. The marks are: stopped, open.");
                            break;
                    }
                }
            }
        }
    }

    private static bool IsIntInRange(string text, int low, int high)
        => int.TryParse(text, out int n) && n >= low && n <= high;
}

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

using System;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// <c>a4@rest</c> — a REST that takes its vertical place from a written pitch, spelled as a
/// note carrying the <c>@rest</c> post-event. The one reader of that spelling.
/// </summary>
/// <remarks>
/// It is a rest EVENT that happens to carry a pitch, not a note: LilyPond's Rest_engraver
/// reads the pitch only to set the grob's staff-position, so nothing sounds and no accidental
/// prints. What it still does is exactly what the note it replaces would — move the
/// relative-octave frame on and carry the duration to the next item.
/// <para>
/// ⚠️ ONE HOUSE because the four outputs disagreed. MEASURED 2026-08-17 on
/// <c>a'4@rest c'4 r4 g'4@rest</c>: the page drew rests and the twin wrote
/// <c>a'4\rest</c>, but MusicXML emitted <c>&lt;note&gt;&lt;pitch&gt;A4</c> — a SOUNDING NOTE
/// where the page draws a rest — and the MIDI played 3 note-ons where the control (the same
/// book with plain rests) played 1. HANDOFF §2F had this filed as "MusicXML drops the
/// height", which named the smaller half of it. Both walk the syntax tree rather than the
/// collector's items, so each had to know the spelling, and neither did.
/// </para>
/// LILYPOND-REF: lily/rest-engraver.cc:62-80 process_music — the pitch sets staff-position
///   and nothing else.
/// </remarks>
public static class PitchedRest
{
    /// <summary>Whether a post-event is the <c>@rest</c> marker.</summary>
    public static bool IsMarker(SyntaxNode articulation)
        => articulation is ArticulationSyntax { Type: ArticulationType.None } named
           && named.NameToken.Text.Equals("rest", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a written note is really a pitched rest.</summary>
    public static bool Is(NoteSyntax note)
    {
        foreach (var art in note.Articulations)
            if (IsMarker(art))
                return true;
        return false;
    }
}

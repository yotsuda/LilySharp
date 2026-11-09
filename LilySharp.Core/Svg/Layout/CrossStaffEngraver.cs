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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Which staff an <c>@cross</c> moves a note to. The moving itself is
/// <c>MeasureCollector.MoveCrossStaffNotes</c>.
/// </summary>
public static class CrossStaffEngraver
{
    /// <summary>
    /// The staff an <c>@cross</c> moves a note to, among <paramref name="staffCount"/> staves: the
    /// next one down, and from the last the one above it — so of two, the other. A hand in a
    /// two-staff grand staff is not asked this: it crosses to the other hand, whatever stands
    /// beside the piano (OMR's report, REQUESTS #24: a song's left hand went to the voice's staff).
    /// </summary>
    public static int TargetStaffOf(int voiceStaffIndex, int staffCount) =>
        voiceStaffIndex + 1 < staffCount ? voiceStaffIndex + 1 : voiceStaffIndex - 1;
}

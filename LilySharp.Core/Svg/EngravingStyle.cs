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

using LilySharp.Core.Svg.Layout;

namespace LilySharp.Core.Svg;

/// <summary>
/// The line thicknesses and the stem length a score is engraved with — LilyPond's own
/// properties, at LilyPond's defaults unless <c>lysc --set</c> says otherwise
/// (LilySharp-Omr's proposal of 2026-10-02 P4, the owner's decision 2026-10-05: LilyPond's
/// names and units, settings only, not the language).
/// </summary>
/// <remarks>
/// <para>
/// ONE STYLE PER LAYOUT AND RENDER, held on the thread (<see cref="Use"/>) by
/// <c>LayoutEngine.Layout</c> and <c>SharedRenderer.RenderTo</c> from the score's paper, and
/// read through <see cref="EngravingDefaults"/> — the constants every engraver already reads
/// became properties over <see cref="Current"/>. At the default every one of them is the
/// number it was, computed the same way.
/// </para>
/// <para>
/// LilyPond's structure is kept: nearly every line reads the STAFF's line thickness,
/// <c>StaffSymbol.thickness × line-thickness</c>
/// (lily/staff-symbol.cc:330-336 Staff_symbol::get_line_thickness, through Staff_symbol_referencer::line_thickness), and
/// states its own thickness as a multiple of it — so <see cref="LineThickness"/> and
/// <see cref="StaffSymbolThickness"/> move every line together, and the others move one kind.
/// The bar lines (their kern too) and a multi-measure rest's block read the PAPER's
/// line-thickness instead (scm/bar-line.scm:227-244 make-simple-bar-line / make-thick-bar-line
/// through layout-line-thickness; lily/multi-measure-rest.cc:195-204 big_rest), so
/// StaffSymbol.thickness leaves them alone, as there.
/// ⚠️ APPROXIMATION, one corner: LilyPond's markup lines (a rehearsal mark's box) read the
/// paper's line-thickness too; here they follow the staff's.
/// </para>
/// </remarks>
public sealed record EngravingStyle
{
    /// <summary>LilyPond's defaults.</summary>
    public static EngravingStyle Default { get; } = new();

    /// <summary>The paper's <c>line-thickness</c>, in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:52-66 calc-line-thickness — 0.5pt at the default
    /// 20pt staff, a tenth of its 5pt staff space.</remarks>
    public double LineThickness { get; init; } = 0.1;

    /// <summary><c>StaffSymbol.thickness</c>, in line-thicknesses: the staff's lines, and the
    /// unit every other line here is stated in.</summary>
    /// <remarks>LILYPOND-REF: lily/staff-symbol.cc:330-336 Staff_symbol::get_line_thickness —
    /// <c>thickness × line-thickness</c>, the property's default 1.0.</remarks>
    public double StaffSymbolThickness { get; init; } = 1.0;

    /// <summary><c>StaffSymbol.ledger-line-thickness</c>'s first term, in staff line thicknesses.</summary>
    /// <remarks>LILYPOND-REF: lily/staff-symbol.cc:338-344 Staff_symbol::get_ledger_line_thickness —
    /// <c>z.x × line thickness + z.y × staff space</c>; scm/define-grobs.scm StaffSymbol
    /// (ledger-line-thickness . (1.0 . 0.1)).</remarks>
    public double LedgerLineThicknessLines { get; init; } = 1.0;

    /// <summary><c>StaffSymbol.ledger-line-thickness</c>'s second term, in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: lily/staff-symbol.cc:338-344 Staff_symbol::get_ledger_line_thickness.</remarks>
    public double LedgerLineThicknessSpaces { get; init; } = 0.1;

    /// <summary><c>LedgerLineSpanner.length-fraction</c>: how far a ledger line reaches past its
    /// head on each side, in that head's widths.</summary>
    /// <remarks>LILYPOND-REF: lily/ledger-line-spanner.cc:205-230 Ledger_line_spanner::print —
    /// <c>ledger_extent.widen (length_fraction * head_extent.length ())</c>; scm/define-grobs.scm
    /// LedgerLineSpanner (length-fraction . 0.25). The spacing rods read the separate
    /// <c>minimum-length-fraction</c>, so this moves no column, as there.</remarks>
    public double LedgerLengthFraction { get; init; } = 0.25;

    /// <summary><c>Stem.thickness</c>, in staff line thicknesses.</summary>
    /// <remarks>LILYPOND-REF: lily/stem.cc:908-913 Stem::thickness, through Staff_symbol_referencer::line_thickness —
    /// <c>thickness × line_thickness</c>; scm/define-grobs.scm Stem (thickness . 1.3).</remarks>
    public double StemThickness { get; init; } = 1.3;

    /// <summary><c>Stem.length-fraction</c>: every stem's length, free and beamed, times this.</summary>
    /// <remarks>LILYPOND-REF: lily/stem.cc:557 internal_calc_stem_end_position and
    /// :1158-1161 calc_stem_info read it, default 1.0. A grace or a cue states its own
    /// (scm/music-functions.scm general-grace-settings, ly/engraver-init.ly CueVoice), which
    /// replaces this one there, as an override in a lower context does in LilyPond.</remarks>
    public double StemLengthFraction { get; init; } = 1.0;

    /// <summary><c>Beam.beam-thickness</c>, in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm Beam (beam-thickness . 0.48), read by
    /// lily/beam.cc:130-145 Beam::get_beam_translation and the quanter.</remarks>
    public double BeamThickness { get; init; } = 0.48;

    /// <summary><c>DotColumn.padding</c>: the gap between a note (or rest) and its first dot, in
    /// staff spaces — null for LilyPond's default, one dot's own width
    /// (<see cref="EngravingDefaults.DotPadding"/>).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm DotColumn (padding .
    /// dot-column-interface::pad-by-one-dot-width).</remarks>
    public double? DotPadding { get; init; }

    /// <summary><c>AccidentalPlacement.right-padding</c>: the gap an accidental keeps from its
    /// note head beyond <c>padding</c>'s 0.2, in staff spaces — the gap is the two summed.</summary>
    /// <remarks>LILYPOND-REF: lily/accidental-placement.cc:398-400 position_apes raises the heads'
    /// skyline by it; scm/define-grobs.scm AccidentalPlacement (right-padding . 0.15).</remarks>
    public double AccidentalRightPadding { get; init; } = 0.15;

    /// <summary><c>Beam.damping</c>: how much a beam's slope is flattened — 0 none, 10000 or
    /// more a horizontal beam.</summary>
    /// <remarks>LILYPOND-REF: lily/beam-quanting.cc:745-775 Beam_scoring_problem::slope_damping —
    /// <c>slope = 0.6 * tanh (slope) / (damping + concaveness)</c>, so the damped slope stays under
    /// 0.6 / damping; scm/define-grobs.scm Beam (damping . 1). Held as the beam quanting's
    /// parameters (<see cref="CurrentBeamParameters"/>), which the beam memo keys by reference.</remarks>
    public double BeamDamping { get; init; } = 1.0;

    /// <summary><c>BarLine.hair-thickness</c>, in staff line thicknesses.</summary>
    /// <remarks>LILYPOND-REF: scm/bar-line.scm:227-238 make-simple-bar-line reads it times the
    /// paper's line-thickness; scm/define-grobs.scm BarLine (hair-thickness . 1.9).</remarks>
    public double BarLineHairThickness { get; init; } = 1.9;

    /// <summary><c>BarLine.thick-thickness</c>, in staff line thicknesses.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm BarLine (thick-thickness . 6.0).</remarks>
    public double BarLineThickThickness { get; init; } = 6.0;

    /// <summary>The staff's line thickness in staff spaces — what LilyPond's
    /// <c>Staff_symbol_referencer::line_thickness</c> answers.</summary>
    public double StaffLineThickness => StaffSymbolThickness * LineThickness;

    [System.ThreadStatic] private static EngravingStyle? t_current;
    [System.ThreadStatic] private static StemDetails? t_stem;
    [System.ThreadStatic] private static BeamQuantParameters? t_beam;

    /// <summary>The style of the layout or render running on this thread (the default
    /// outside one).</summary>
    internal static EngravingStyle Current => t_current ?? Default;

    /// <summary>The stem table at <see cref="Current"/>'s length-fraction — the one
    /// <see cref="StemDetails.Default"/> answers.</summary>
    internal static StemDetails CurrentStem => t_stem ?? DefaultStemDetails;

    /// <summary>The stem table at LilyPond's defaults — the one instance
    /// <see cref="CurrentStem"/> answers at the default length-fraction.</summary>
    internal static readonly StemDetails DefaultStemDetails = new();

    /// <summary>The beam quanting's parameters at <see cref="Current"/>'s damping — one instance
    /// per scope, <see cref="BeamQuantParameters.Default"/> at the default, so a beam solved
    /// under another style is another question to the memo (it keys the parameters by reference).</summary>
    internal static BeamQuantParameters CurrentBeamParameters => t_beam ?? BeamQuantParameters.Default;

    /// <summary>Holds <paramref name="style"/> until the scope is disposed; scopes nest.</summary>
    internal static Scope Use(EngravingStyle style) => new(style);

    /// <summary>The scope <see cref="Use"/> opens.</summary>
    internal readonly struct Scope : System.IDisposable
    {
        private readonly EngravingStyle? _previous;
        private readonly StemDetails? _previousStem;
        private readonly BeamQuantParameters? _previousBeam;

        internal Scope(EngravingStyle style)
        {
            _previous = t_current;
            _previousStem = t_stem;
            _previousBeam = t_beam;
            bool isDefault = style.Equals(Default);
            t_current = isDefault ? null : style;
            t_stem = isDefault || style.StemLengthFraction == 1.0
                ? null
                : DefaultStemDetails with { LengthFraction = style.StemLengthFraction };
            t_beam = isDefault || style.BeamDamping == BeamQuantParameters.Default.Damping
                ? null
                : BeamParametersAt(style.BeamDamping);
        }

        // The last damping's parameters, kept so the layout's scope and the render's ask the
        // memo with the same reference (a new instance per scope would miss it every time).
        private static BeamQuantParameters? s_lastBeam;

        private static BeamQuantParameters BeamParametersAt(double damping)
        {
            var last = s_lastBeam;
            if (last != null && last.Damping == damping)
                return last;
            return s_lastBeam = BeamQuantParameters.Default with { Damping = damping };
        }

        /// <summary>Restores the style that was current before.</summary>
        public void Dispose()
        {
            t_current = _previous;
            t_stem = _previousStem;
            t_beam = _previousBeam;
        }
    }
}

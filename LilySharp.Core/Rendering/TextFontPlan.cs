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

using System.Collections.Immutable;

namespace LilySharp.Core.Rendering;

/// <summary>
/// What a score's <c>font</c> directive asked for, resolved: the one place that turns a
/// <see cref="TextRole"/> into the faces a backend draws with and the bundled family the
/// layout reserved against.
/// </summary>
/// <remarks>
/// THE RESOLUTION ORDER IS ONE RULE, applied to two questions:
/// <list type="number">
/// <item>the leaf's own binding (<c>lyricText "Charis SIL"</c>),</item>
/// <item>its group's binding (<c>lyrics "Charis SIL"</c>),</item>
/// <item>the generic family it belongs to (<c>serif "Georgia"</c>) — binding BOTH
/// families is how a score says "the whole document's text" — UNLESS the role is
/// notation (<see cref="TextRoles.IsNotation"/>),</item>
/// <item>the bundled face.</item>
/// </list>
/// The narrower spelling wins wherever both are written, in either source order, which is
/// what lets a score say <c>marks "Georgia"</c> and then <c>tempo "Playfair Display"</c>
/// without the second being a special case.
/// <para>
/// ⚠️ RESOLVING IS NOT MEASURING, and this type still does only the first half.
/// <see cref="ResolvedFace.Family"/> is the family a role FALLS BACK to — the bundled file
/// used when nothing is bound, and the target of a <c>chords serif</c> style redirect. It
/// deliberately says nothing about which file a NAMED face resolves to, because that
/// answer depends on the machine: <see cref="ScoreTextMetrics.Face"/> walks the chain and
/// asks <see cref="TextFontMetrics.CanMeasure"/>, and the reservation follows the face it
/// gets. The gap this note used to record — a 16-character tempo mark at 2.2 ss running
/// −2.05 to +3.61 staff spaces off depending on the face, measured 2026-08-18 — was closed
/// the same day by giving the layout the roles.
/// </para>
/// </remarks>
public sealed class TextFontPlan
{
    /// <summary>
    /// The faces a role is drawn with and the bundled family it is measured against.
    /// </summary>
    /// <param name="Names">The face chain, most-preferred first; EMPTY means the bundled
    /// face of <paramref name="Family"/>, which is also the only case the reservation and
    /// the drawing are known to agree in.</param>
    /// <param name="Family">The bundled family the layout reserved against.</param>
    public readonly record struct ResolvedFace(ImmutableArray<string> Names, TextFontFamily Family)
    {
        /// <summary>True when nothing was bound and the bundled face is what gets drawn.</summary>
        public bool IsBundled => Names.IsDefaultOrEmpty;

        /// <summary>
        /// The family name a backend puts on the page: the bound chain, or the bundled
        /// face's own name.
        /// </summary>
        /// <remarks>
        /// Comma-separated because that is what SVG and CSS read as a fallback chain, and
        /// a chain is the whole point of allowing more than one name — a Latin face for
        /// the words and a CJK face for the syllables the first one has no glyph for.
        /// Backends that can only hold ONE face (PNG, PDF) walk
        /// <see cref="Names"/> themselves and take the first that resolves.
        /// </remarks>
        public string FamilyAttribute => IsBundled
            ? BundledName(Family)
            : string.Join(", ", Names);
    }

    private readonly ImmutableDictionary<TextFontFamily, ImmutableArray<string>> _families;
    private readonly ImmutableDictionary<TextRoleGroup, Binding> _groups;
    private readonly ImmutableDictionary<TextRole, Binding> _leaves;

    /// <summary>Whether the named faces should be subset-embedded in a PDF.</summary>
    public bool Embed { get; }

    /// <summary>True when the score bound nothing — every role takes the bundled face.</summary>
    /// <remarks>
    /// Used by the SVG fragment memo, which may only reuse a recorded system when the
    /// families in the recorded bytes are still the ones this render would emit.
    /// </remarks>
    public bool IsDefault =>
        _families.IsEmpty && _groups.IsEmpty && _leaves.IsEmpty;

    /// <summary>The plan of a score with no <c>font</c> directive.</summary>
    public static readonly TextFontPlan Default = new(
        ImmutableDictionary<TextFontFamily, ImmutableArray<string>>.Empty,
        ImmutableDictionary<TextRoleGroup, Binding>.Empty,
        ImmutableDictionary<TextRole, Binding>.Empty,
        embed: false);

    private TextFontPlan(
        ImmutableDictionary<TextFontFamily, ImmutableArray<string>> families,
        ImmutableDictionary<TextRoleGroup, Binding> groups,
        ImmutableDictionary<TextRole, Binding> leaves,
        bool embed)
    {
        _families = families;
        _groups = groups;
        _leaves = leaves;
        Embed = embed;
        Signature = BuildSignature(families, groups, leaves, embed);
    }

    /// <summary>
    /// A canonical, order-independent spelling of every binding — this plan's identity.
    /// </summary>
    /// <remarks>
    /// Two plans built from differently-ORDERED but equally-BINDING directives have the
    /// same signature, which is what equality has to mean here: the incremental
    /// collector compares a re-read header against its recorded one to decide whether a
    /// resume is legal, and the SVG fragment memo keys recorded bytes by it. A
    /// reference comparison would call every keystroke a change; a field-by-field one
    /// would be a second spelling of the same question.
    /// </remarks>
    public string Signature { get; }

    private static string BuildSignature(
        ImmutableDictionary<TextFontFamily, ImmutableArray<string>> families,
        ImmutableDictionary<TextRoleGroup, Binding> groups,
        ImmutableDictionary<TextRole, Binding> leaves,
        bool embed)
    {
        var parts = new List<string>();
        foreach (var (family, names) in families)
            parts.Add($"@{TextRoles.Spelling(family)}={string.Join("|", names)}");
        foreach (var (group, b) in groups)
            parts.Add($"#{TextRoles.Spelling(group)}={Spell(b)}");
        foreach (var (role, b) in leaves)
            parts.Add($".{TextRoles.Spelling(role)}={Spell(b)}");
        parts.Sort(StringComparer.Ordinal);
        return (embed ? "embed;" : "") + string.Join(";", parts);

        static string Spell(Binding b) => b.Redirect is { } r
            ? "->" + TextRoles.Spelling(r)
            : string.Join("|", b.Names.IsDefault ? [] : b.Names);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TextFontPlan other && other.Signature == Signature;

    /// <inheritdoc/>
    public override int GetHashCode() => Signature.GetHashCode(StringComparison.Ordinal);

    /// <summary>
    /// One binding's right-hand side: face names, or a redirect to a generic family.
    /// </summary>
    /// <param name="Names">Face names, most-preferred first; empty when this is a redirect.</param>
    /// <param name="Redirect">The generic family this key points at, when it points at one.</param>
    internal readonly record struct Binding(ImmutableArray<string> Names, TextFontFamily? Redirect)
    {
        /// <summary>A binding to an explicit face chain.</summary>
        public static Binding ToFaces(IEnumerable<string> names) =>
            new([.. names], null);

        /// <summary>A binding that points at one of the generic families.</summary>
        public static Binding ToFamily(TextFontFamily family) =>
            new([], family);
    }

    /// <summary>
    /// The faces <paramref name="role"/> is drawn with, and the family it is measured
    /// against.
    /// </summary>
    public ResolvedFace Resolve(TextRole role)
    {
        // The brace is a music glyph addressed by face name; no score binds it.
        if (role == TextRole.SystemBrace)
            return new ResolvedFace([BraceFaceName], TextFontFamily.Serif);

        var group = TextRoles.GroupOf(role);
        Binding? leaf = _leaves.TryGetValue(role, out var lb) ? lb : null;
        Binding? grp = group is { } g && _groups.TryGetValue(g, out var gb) ? gb : null;

        // The family the reservation uses: a redirect on the narrower key wins, then the
        // wider one, then what the role is by nature.
        TextFontFamily family =
            leaf?.Redirect ?? grp?.Redirect ?? TextRoles.DefaultFamily(role);

        // The faces drawn: the narrower binding's names, then the wider one's, then the
        // generic family's — except for notation, which the generic binding does not reach.
        if (leaf is { Names.IsDefaultOrEmpty: false } lf)
            return new ResolvedFace(lf.Names, family);
        if (grp is { Names.IsDefaultOrEmpty: false } gf)
            return new ResolvedFace(gf.Names, family);
        if (!TextRoles.IsNotation(role) &&
            _families.TryGetValue(family, out var fam) && !fam.IsDefaultOrEmpty)
            return new ResolvedFace(fam, family);
        return new ResolvedFace([], family);
    }

    /// <summary>The bundled face name for <paramref name="family"/>.</summary>
    public static string BundledName(TextFontFamily family) =>
        family == TextFontFamily.Sans ? TextFontMetrics.SansFamily : TextFontMetrics.SerifFamily;

    /// <summary>
    /// The Emmentaler brace face, addressed by name because the brace ladder lives in its
    /// own file rather than in the score's Emmentaler design.
    /// </summary>
    public const string BraceFaceName = "Emmentaler-Brace";

    /// <summary>Every face name this plan names, deduplicated — what a PDF must embed.</summary>
    public IEnumerable<string> NamedFaces()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var names in _families.Values)
            foreach (var n in names)
                if (seen.Add(n))
                    yield return n;
        foreach (var b in _groups.Values)
            foreach (var n in b.Names.IsDefault ? [] : b.Names)
                if (seen.Add(n))
                    yield return n;
        foreach (var b in _leaves.Values)
            foreach (var n in b.Names.IsDefault ? [] : b.Names)
                if (seen.Add(n))
                    yield return n;
    }

    /// <summary>Builds a plan from the bindings a <c>font</c> directive wrote.</summary>
    /// <remarks>
    /// Later bindings of the SAME key overwrite earlier ones — the reader's last word
    /// wins, matching how the language already treats a repeated global setting (and the
    /// duplicate is reported separately, so nothing is silently dropped).
    /// </remarks>
    public sealed class Builder
    {
        private readonly Dictionary<TextFontFamily, ImmutableArray<string>> _families = [];
        private readonly Dictionary<TextRoleGroup, Binding> _groups = [];
        private readonly Dictionary<TextRole, Binding> _leaves = [];
        private bool _embed;

        /// <summary>Binds a generic family to a face chain.</summary>
        public Builder Family(TextFontFamily family, IEnumerable<string> names)
        {
            _families[family] = [.. names];
            return this;
        }

        /// <summary>Binds a group to a face chain.</summary>
        public Builder Group(TextRoleGroup group, IEnumerable<string> names)
        {
            _groups[group] = Binding.ToFaces(names);
            return this;
        }

        /// <summary>Points a group at one of the generic families.</summary>
        public Builder Group(TextRoleGroup group, TextFontFamily family)
        {
            _groups[group] = Binding.ToFamily(family);
            return this;
        }

        /// <summary>Binds one leaf role to a face chain.</summary>
        public Builder Role(TextRole role, IEnumerable<string> names)
        {
            _leaves[role] = Binding.ToFaces(names);
            return this;
        }

        /// <summary>Points one leaf role at a generic family.</summary>
        public Builder Role(TextRole role, TextFontFamily family)
        {
            _leaves[role] = Binding.ToFamily(family);
            return this;
        }

        /// <summary>Asks for the named faces to be subset-embedded in a PDF.</summary>
        public Builder Embed(bool embed = true)
        {
            _embed = embed;
            return this;
        }

        /// <summary>
        /// Binds BOTH generic families to one chain — "the whole document's text", which a
        /// score writes as <c>fonts { serif "NAME"  sans "NAME" }</c>.
        /// </summary>
        /// <remarks>
        /// ⚠️ It does not reach notation — see <see cref="TextRoles.IsNotation"/>.
        /// <para>
        /// ⚠️ NO SOURCE SPELLING REACHES THIS ANY MORE. It served the one-line
        /// <c>font "NAME"</c>, removed 2026-08-18; a block spells the same thing with two
        /// entries and takes the ordinary <c>Family</c> path. It survives as the Builder's
        /// own vocabulary — several tests build plans with it — so it is not dead code, but
        /// it is no longer evidence that the language has a shorthand.
        /// </para>
        /// </remarks>
        public Builder Everything(IEnumerable<string> names)
        {
            var chain = names.ToArray();
            Family(TextFontFamily.Serif, chain);
            Family(TextFontFamily.Sans, chain);
            return this;
        }

        /// <summary>Freezes the bindings into a plan.</summary>
        public TextFontPlan Build() => new(
            _families.ToImmutableDictionary(),
            _groups.ToImmutableDictionary(),
            _leaves.ToImmutableDictionary(),
            _embed);
    }
}

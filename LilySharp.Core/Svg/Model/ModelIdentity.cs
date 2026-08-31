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

using System.Runtime.CompilerServices;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// The one home of the model's equality rule: which model types answer by IDENTITY and
/// which answer by VALUE. Every entity type's <c>GetHashCode</c> routes through
/// <see cref="HashOf"/>, so the rule has a single searchable anchor.
/// </summary>
/// <remarks>
/// <para>
/// The model is written as C# records, and a record's synthesized equality compares every
/// field. For a type that denotes ONE OCCURRENCE in one score that is the wrong answer:
/// two occurrences carrying the same content are still two different things, so
/// <c>IndexOf</c> / <c>Contains</c> / a dictionary key silently resolve to the FIRST twin.
/// Fixed #18 was exactly that — a unison tie pair both resolved to slot 0 through
/// <c>ordered.IndexOf</c>, and one bow was drawn twice. The discipline that followed
/// ("search model collections with reference equality") lives at the call sites, so it
/// only protects the call sites that remember it; this moves the answer into the type.
/// </para>
/// <para>
/// ⚠️ Twins are not hypothetical. Measured over 574 corpus books (2026-08-31, session 307,
/// <c>scratch/p307/twinscan.txt</c>): <c>repeat tremolo</c> expands one written group into
/// many content-identical <see cref="NoteItem"/>s inside one measure's item list — 396 twin
/// pairs across the four <c>wntacc*.lys</c> books — and twins also occur for
/// <see cref="TieItem"/> (the fixed #18 shape, still in the corpus),
/// <see cref="VoltaBracketItem"/> and <see cref="Measure"/>.
/// </para>
/// <para>
/// The types NOT on the identity side are the ones whose instances DESCRIBE something
/// rather than occur in a score — a standing override, a beam's derived geometry, a
/// column's grouping. Their value equality is load-bearing: the incremental compiler asks
/// "is the thing I just recomputed the same VALUE as the one I stored?" to decide whether
/// a cached layout may be reused. Poisoning the whole model with identity (session 307)
/// turned exactly three of them red — <c>GrobOverride</c> and <c>GrobRevert</c>
/// (IncrementalCompiler's <c>overridesUnchanged</c>) and <c>BeamRestStem</c>
/// (BeamDetector's memo-soundness <c>SequenceEqual</c>) — and nothing else in 6806 tests.
/// </para>
/// <para>
/// ⚠️ Keeping the types as <c>record</c> and overriding <c>Equals</c> is deliberate: the
/// alternative (turning them into <c>class</c>) would delete <c>with</c>, which the tree
/// uses in over a hundred places on <c>Staff</c>, <c>Measure</c> and <c>Voice</c> alone.
/// A record with a user-declared <c>Equals(T?)</c> keeps <c>with</c>, deconstruction and
/// <c>ToString</c>, and the compiler routes <c>==</c>, <c>Equals(object)</c> and every
/// collection search through the declared one. For the <see cref="MusicItem"/> hierarchy
/// ONE declaration on the abstract base is enough: each derived record's synthesized
/// equality begins with <c>base.Equals(...)</c>, so identity propagates.
/// </para>
/// <para>
/// The buckets are pinned by <c>ModelEqualityKindTests</c> — a new model type cannot be
/// added without landing in one of them on purpose. HANDOFF §2 C⑶ carries the triage.
/// </para>
/// </remarks>
internal static class ModelIdentity
{
    /// <summary>
    /// The hash that goes with reference equality: stable for one object, independent of
    /// its fields. Every entity model type's <c>GetHashCode</c> is this call.
    /// </summary>
    public static int HashOf(object instance) => RuntimeHelpers.GetHashCode(instance);
}

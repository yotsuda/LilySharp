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

using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The engine must never build a HarfBuzz blob with <c>Blob.FromStream</c>.
/// </summary>
/// <remarks>
/// That factory hands HarfBuzz a pointer into a managed <c>byte[]</c> that nothing pins and
/// nothing references. HarfBuzzSharp 8.3.1.3 ends it (binding/HarfBuzzSharp.Shared/Blob.cs):
/// <code>
/// var data = ms.ToArray ();
/// fixed (byte* dataPtr = data)
///     return new Blob ((IntPtr)dataPtr, data.Length, MemoryMode.ReadOnly, () =&gt; ms.Dispose ());
/// </code>
/// <c>fixed</c> pins for the BLOCK, and the release delegate captures <c>ms</c>, not
/// <c>data</c>. HarfBuzz then reads the face's tables out of that pointer LAZILY, on the
/// first shape, so any gen-2 collection in between — reclaiming the buffer, or moving it if
/// the large-object heap is compacted — leaves the shaper reading freed memory.
/// <para>
/// ⚠️ THE POISON FOR THIS TEST CANNOT BE THE BEHAVIOUR, WHICH IS WHY THE GUARD IS STRUCTURAL.
/// The failure is an access violation inside <c>hb_shape_full</c>: it tears the test host
/// down rather than failing a case, so a behavioural net would abort the run instead of
/// going red — the suite would report a Passed! total short of its real one, which HANDOFF
/// §0 already records as the shape of a silent lie. Reverting
/// <c>TextFontMetrics.BlobOver</c> to <c>Blob.FromStream</c> makes THIS test red, on every
/// platform, in milliseconds.
/// </para>
/// <para>
/// MEASURED, 2026-09-01 (session 317, <c>scratch/p317/hbstress</c>, four arms over
/// <c>arial.ttf</c>, single-threaded, a blocking gen-2 collection between shapes):
/// <c>fromstream</c> and <c>fromstream-lohc</c> both die <c>0xC0000005</c> at
/// <c>hb_shape_full</c> before the first round finishes; <c>pinned</c> and
/// <c>pinned-lohc</c> — what the engine does now — survive 300 rounds with the shaped
/// checksum unmoved at 40816.
/// </para>
/// <para>
/// ⚠️ ONLY A NAMED SYSTEM FACE EVER REACHED IT, which is why the corpus was blind: a bundled
/// face takes <c>Blob.FromFile</c>, whose memory HarfBuzz owns. Found by the suite aborting
/// after 1625 of 6819 cases in <c>FontEditIncrementalTests.FontsFaceEdit_MatchesFullRecompile</c>,
/// the one book that says <c>fonts { serif "Arial" }</c>. It reads as a flake of session
/// 315's parallelisation and is not one — parallelism only raised the gen-2 rate.
/// </para>
/// </remarks>
public class ShapingBlobOwnershipTests
{
    [Fact]
    public void NoCoreSourceBuildsAHarfBuzzBlobFromAStream()
    {
        var root = CollectResumeTests.FindRepoRoot();
        var core = Path.Combine(root, "LilySharp.Core");

        var files = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                                    StringComparison.Ordinal)
                     && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                                    StringComparison.Ordinal))
            .ToArray();

        // A vacuous census reads exactly like a clean one (HANDOFF §0).
        Assert.True(files.Length > 100,
            $"only {files.Length} .cs files under LilySharp.Core — this scan would be vacuous.");

        var hits = new List<string>();
        foreach (var path in files)
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                // The ban is on the CALL. This test's own citation of the name, and
                // TextFontMetrics.BlobOver's, are in comments and doc, so skip those.
                if (!lines[i].Contains("Blob.FromStream(", StringComparison.Ordinal))
                    continue;
                var code = lines[i].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal)
                    || code.StartsWith("///", StringComparison.Ordinal)
                    || code.StartsWith("*", StringComparison.Ordinal))
                    continue;
                hits.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{i + 1}: {code}");
            }
        }

        Assert.True(hits.Count == 0,
            "HarfBuzzSharp's Blob.FromStream leaves HarfBuzz holding a pointer into an "
            + "unpinned, unreferenced managed byte[]; the next gen-2 collection turns the "
            + "first shape into an access violation that tears the process down. Build the "
            + "blob over memory this side keeps alive — TextFontMetrics.BlobOver.\n"
            + string.Join("\n", hits));
    }

    /// <summary>
    /// A cache whose factory takes ownership of a HarfBuzz font must build it exactly once.
    /// </summary>
    /// <remarks>
    /// <c>ConcurrentDictionary.GetOrAdd(key, factory)</c> may run the factory on SEVERAL
    /// threads for one cold key and keep only the winner. Where the factory builds a
    /// Blob+Face+Font triple, every loser is a native triple nobody disposes — dropped for a
    /// finaliser to destroy while other threads are shaping — and, since
    /// <c>TextFontMetrics.BlobOver</c>, one that also PINS a font-sized <c>byte[]</c> until
    /// that finaliser runs. Storing a <see cref="Lazy{T}"/> fixes it: a losing thread builds
    /// a Lazy, which costs an allocation and no native handle, and only the STORED one is
    /// ever evaluated.
    /// <para>
    /// MEASURED 2026-09-01 (session 317, <c>scratch/p317/hbstress</c> arm <c>getoradd</c>,
    /// the real triple as its factory): 4 threads on a cold key ran the factory more than
    /// once in 38 of 40 trials, orphaning 64 triples across the run; 8 and 16 threads,
    /// 16/40 and 19/40.
    /// </para>
    /// <para>
    /// ⚠️ STRUCTURAL, BECAUSE THE BEHAVIOUR IS UNOBSERVABLE FROM INSIDE THE SUITE: these
    /// caches are process-static and any earlier test has already warmed them, so no case
    /// can reach a cold key to count the factory. The shape is the only thing a test here
    /// can hold.
    /// </para>
    /// <para>
    /// ⚠️ THE RULE IS THE INVARIANT, NOT TWO FIELD NAMES — it is written over every static
    /// cache in Core so a third one added later is covered without anybody remembering this.
    /// It deliberately does NOT cover caches that merely duplicate WORK (Paths, RunCache,
    /// Faces): a race there costs an ordinary managed orphan, which is what a cache race
    /// costs everywhere else in this tree.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryStaticCacheThatOwnsAHarfBuzzFontBuildsItExactlyOnce()
    {
        var assembly = typeof(LilySharp.Core.Rendering.TextFontMetrics).Assembly;
        var owning = new List<(string Field, Type Value)>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type.GetFields(
                         System.Reflection.BindingFlags.Static
                         | System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.NonPublic
                         | System.Reflection.BindingFlags.DeclaredOnly))
            {
                var ft = field.FieldType;
                if (!ft.IsGenericType
                    || ft.GetGenericTypeDefinition()
                       != typeof(System.Collections.Concurrent.ConcurrentDictionary<,>))
                    continue;
                var value = ft.GetGenericArguments()[1];
                if (Mentions(value, typeof(HarfBuzzSharp.Font)))
                    owning.Add(($"{type.FullName}.{field.Name}", value));
            }
        }

        // A vacuous census reads exactly like a clean one (HANDOFF §0).
        Assert.True(owning.Count >= 2,
            $"found {owning.Count} static ConcurrentDictionary caches holding a "
            + "HarfBuzzSharp.Font; TextFontMetrics has at least two (ShapingFonts, "
            + "MusicFaces), so this scan has stopped seeing them and proves nothing.");

        var bare = owning
            .Where(o => !(o.Value.IsGenericType
                          && o.Value.GetGenericTypeDefinition() == typeof(Lazy<>)))
            .Select(o => $"{o.Field} : {o.Value}")
            .ToArray();

        Assert.True(bare.Length == 0,
            "a cache that owns a HarfBuzz font must store a Lazy<>, or ConcurrentDictionary "
            + "will build the Blob+Face+Font triple on several threads for one cold key and "
            + "orphan every loser — each pinning a font-sized buffer until its finaliser "
            + "runs, while other threads are shaping. Measured: 38 of 40 cold trials at four "
            + "threads. Offending fields:\n" + string.Join("\n", bare));
    }

    /// <summary>True when <paramref name="type"/> is, or generically contains, <paramref name="sought"/>.</summary>
    private static bool Mentions(Type type, Type sought)
        => type == sought
           || (type.IsGenericType
               && type.GetGenericArguments().Any(a => Mentions(a, sought)));
}

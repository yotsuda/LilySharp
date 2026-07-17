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
/// Reports a phrase / variable reference CYCLE — a phrase that references itself
/// directly (<c>phrase x { x }</c>), through a pair (<c>x -> y -> x</c>), or around a
/// longer ring (<c>x -> y -> z -> x</c>). Such a phrase can never expand to a finite
/// piece; the consumers (collector, MIDI, MusicXML) each break the recursion at
/// runtime so nothing crashes, but the mistake would otherwise be silent, so it is
/// surfaced once here — over the whole declaration graph, INDEPENDENT of whether a form
/// references any of the phrases.
/// </summary>
internal sealed class PhraseCycleValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();

        // Each phrase/variable name -> the body node that defines it.
        var bodies = new Dictionary<string, SyntaxNode>();
        foreach (var n in root.DescendantNodes())
        {
            if (n is PhraseDeclarationSyntax ph)
                bodies[ph.Name.Text] = ph.Body;
            else if (n is VariableDeclarationSyntax vd)
                bodies[vd.Name.Text] = vd.Expression;
        }
        if (bodies.Count == 0)
            return;

        // Edges name -> referenced names (only edges to a declared phrase/variable; an
        // undefined reference is SymbolReferenceValidator's business, not a cycle).
        var edges = new Dictionary<string, List<string>>();
        foreach (var (name, body) in bodies)
        {
            var outs = new List<string>();
            foreach (var r in body.DescendantNodes().OfType<VariableReferenceSyntax>())
                if (bodies.ContainsKey(r.Name.Text))
                    outs.Add(r.Name.Text);
            edges[name] = outs;
        }

        // Colour-DFS: report the first back-edge on each cycle exactly once. `path`
        // is the active chain, so the reported ring runs from the revisited node to
        // the current one — naming the whole cycle (x -> y -> z -> x), not just a node.
        var colour = new Dictionary<string, int>(); // 0 = unvisited, 1 = on stack, 2 = done
        var path = new List<string>();
        var reported = new HashSet<string>();

        void Visit(string name)
        {
            colour[name] = 1;
            path.Add(name);
            foreach (var next in edges[name])
            {
                if (colour.GetValueOrDefault(next) == 1)
                    Report(next);
                else if (colour.GetValueOrDefault(next) == 0)
                    Visit(next);
            }
            path.RemoveAt(path.Count - 1);
            colour[name] = 2;
        }

        void Report(string closes)
        {
            int at = path.IndexOf(closes);
            var ring = path.Skip(at).ToList();
            // One diagnostic per distinct ring (a cycle is reachable from several starts).
            var key = string.Join(" ", ring.OrderBy(x => x));
            if (!reported.Add(key))
                return;
            // Point at the reference that closes the ring — the edge from the ring's last
            // member back to its first — so the squiggle lands on the offending `name`.
            var last = ring[^1];
            var backRef = bodies[last].DescendantNodes().OfType<VariableReferenceSyntax>()
                .FirstOrDefault(r => r.Name.Text == closes);
            var span = backRef?.Name.Span ?? bodies[closes].Span;
            string chain = string.Join(" -> ", ring) + " -> " + closes;
            _diagnostics.Error(span, DiagnosticCodes.PhraseReferenceCycle,
                $"Phrase reference cycle: {chain}. A phrase cannot reference itself "
                + "(directly or through a chain) — it would never expand to a finite piece.");
        }

        foreach (var name in bodies.Keys.OrderBy(x => x))
            if (colour.GetValueOrDefault(name) == 0)
                Visit(name);
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lab1;

class DefUseCollector
{
    public void Collect(AbstractSemanticGraph graph)
    {
        var defsBySymbol = new Dictionary<ISymbol, List<AsgNode>>(
            SymbolEqualityComparer.Default);

        foreach (var node in graph.Nodes)
        {
            if (node.Symbol == null) continue;
            if (!IsDefinition(node)) continue;

            if (!defsBySymbol.TryGetValue(node.Symbol, out var list))
            {
                list = new List<AsgNode>();
                defsBySymbol[node.Symbol] = list;
            }

            list.Add(node);
        }

        foreach (var node in graph.Nodes)
        {
            if (!IsUse(node)) continue;
            if (node.Symbol == null) continue;

            if (!defsBySymbol.TryGetValue(node.Symbol, out var defs)) continue;

            foreach (var def in defs)
                graph.AddEdge(node, def, AsgEdgeKind.DataFlow);
        }
    }

    private static bool IsDefinition(AsgNode node) =>
        node.Kind is AsgNodeKind.VariableDeclaration
            or AsgNodeKind.Parameter
            or AsgNodeKind.ForEachVariable;

    private static bool IsUse(AsgNode node)
    {
        if (node.Kind != AsgNodeKind.Identifier) return false;

        if (node.Source is not IdentifierNameSyntax) return false;

        if (node.Symbol is not (ILocalSymbol or IParameterSymbol)) return false;

        return true;
    }
}
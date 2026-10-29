using Microsoft.CodeAnalysis;

namespace Lab1;

class ControlFlowMapper
{
    public void AddEdges(AbstractSemanticGraph asg, ControlFlowGraph cfg)
    {
        var blockNodes = new Dictionary<BasicBlock, List<AsgNode>>();

        foreach (var block in cfg.Blocks)
        {
            var ordered = new List<AsgNode>();
            foreach (var instr in block.Instructions)
            {
                if (instr.SourceCodeNode == null) continue;
                var nodes = FindExecutableNodes(asg, instr.SourceCodeNode);
                ordered.AddRange(nodes.OrderBy(n => n.Source!.SpanStart));
            }
            blockNodes[block] = ordered;
        }

        
        foreach (var nodes in blockNodes.Values)
        {
            for (int i = 0; i + 1 < nodes.Count; i++)
                asg.AddEdge(nodes[i], nodes[i + 1], AsgEdgeKind.ControlFlow);
        }

        
        foreach (var fromBlock in cfg.Blocks)
        {
            if (!blockNodes.TryGetValue(fromBlock, out var fromNodes) || fromNodes.Count == 0)
                continue;

            var lastNode = fromNodes[^1];

            foreach (var edge in fromBlock.Successors)
            {
                if (!blockNodes.TryGetValue(edge.To, out var toNodes) || toNodes.Count == 0)
                    continue;

                var firstNode = toNodes[0];
                if (lastNode == firstNode) continue;

                string? label = !string.IsNullOrEmpty(edge.Label)
                    ? edge.Label
                    : edge.Kind switch
                    {
                        EdgeKind.True     => "true",
                        EdgeKind.False    => "false",
                        EdgeKind.Break    => "break",
                        EdgeKind.Continue => "continue",
                        _ => null
                    };

                asg.AddEdge(lastNode, firstNode, AsgEdgeKind.ControlFlow, label);
            }
        }
    }

    private static List<AsgNode> FindExecutableNodes(AbstractSemanticGraph asg, SyntaxNode source)
    {
        var result = new List<AsgNode>();

        foreach (var node in asg.Nodes)
        {
            if (node.Source == null) continue;
            if (!IsExecutable(node.Kind)) continue;
            if (!source.Span.Contains(node.Source.Span)) continue;

            
            bool hasExecutableAncestor = false;
            var current = node.In.FirstOrDefault(e => e.Kind == AsgEdgeKind.AstChild)?.From;
            while (current != null)
            {
                if (IsExecutable(current.Kind)
                    && current.Source != null
                    && source.Span.Contains(current.Source.Span))
                {
                    hasExecutableAncestor = true;
                    break;
                }
                current = current.In.FirstOrDefault(e => e.Kind == AsgEdgeKind.AstChild)?.From;
            }

            if (!hasExecutableAncestor)
                result.Add(node);
        }

        return result;
    }

    private static bool IsExecutable(AsgNodeKind kind) => kind switch
    {
        AsgNodeKind.VariableDeclaration => true,
        AsgNodeKind.Statement           => true,
        AsgNodeKind.Expression          => true,
        AsgNodeKind.Invocation          => true,
        AsgNodeKind.ObjectCreation      => true,
        AsgNodeKind.ArrayCreation       => true,
        AsgNodeKind.FieldAccess         => true,
        AsgNodeKind.ElementAccess       => true,
        _ => false
    };
}
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lab1;

class AsgCompactor
{
    private AbstractSemanticGraph _source = null!;
    private AbstractSemanticGraph _target = null!;
    private int _nextId;
    private readonly Dictionary<AsgNode, AsgNode> _nearestKept = new();

    public AbstractSemanticGraph Compact(AbstractSemanticGraph source)
    {
        _source = source;
        _target = new AbstractSemanticGraph();
        _nextId = 0;
        _nearestKept.Clear();

        _target.Root = Visit(source.Root, null)!;

        
        foreach (var node in source.Nodes)
        {
            foreach (var edge in node.Out)
            {
                if (edge.Kind == AsgEdgeKind.AstChild) continue;

                var fromKept = _nearestKept.GetValueOrDefault(edge.From);
                var toKept = _nearestKept.GetValueOrDefault(edge.To);

                if (fromKept != null && toKept != null && fromKept != toKept)
                {
                    if (!fromKept.Out.Any(e => e.To == toKept && e.Kind == edge.Kind))
                        _target.AddEdge(fromKept, toKept, edge.Kind, edge.Label);
                }
            }
        }

        return _target;
    }

    private AsgNode? Visit(AsgNode node, AsgNode? parentKept)
    {
        bool keep = ShouldKeep(node);
        AsgNode? currentKept = parentKept;

        if (keep)
        {
            var newNode = new AsgNode(_nextId++)
            {
                Kind = node.Kind,
                Label = GetCompactLabel(node),
                Source = node.Source,
                Symbol = node.Symbol,
                InferredType = node.InferredType,
                Nullable = node.Nullable,
                IsStatic = node.IsStatic
            };
            _target.Nodes.Add(newNode);

            if (parentKept != null)
            {
                
                string? edgeLabel = null;
                if (parentKept.Source is IfStatementSyntax ifStmt && node.Source != null)
                {
                    if (ifStmt.Condition.Span.Contains(node.Source.Span))
                        edgeLabel = "cond";
                    else if (ifStmt.Statement.Span.Contains(node.Source.Span))
                        edgeLabel = "true";
                    else if (ifStmt.Else != null && ifStmt.Else.Span.Contains(node.Source.Span))
                        edgeLabel = "false";
                }

                _target.AddEdge(parentKept, newNode, AsgEdgeKind.AstChild, edgeLabel);
            }

            currentKept = newNode;
        }

        _nearestKept[node] = currentKept!;



        if (node.Kind == AsgNodeKind.Expression && keep)
        {
            MapDescendantsTo(node, currentKept!);
            return currentKept;
        }

        foreach (var childEdge in node.Out.Where(e => e.Kind == AsgEdgeKind.AstChild))
            Visit(childEdge.To, currentKept);

        return currentKept;
    }

    private void MapDescendantsTo(AsgNode node, AsgNode target)
    {
        foreach (var e in node.Out.Where(x => x.Kind == AsgEdgeKind.AstChild))
        {
            _nearestKept[e.To] = target;
            MapDescendantsTo(e.To, target);
        }
    }

    private static bool ShouldKeep(AsgNode node)
    {
        
        if (node.Source is ParenthesizedExpressionSyntax or ElseClauseSyntax)
            return false;

        return node.Kind switch
        {
            AsgNodeKind.Block => false, 
            AsgNodeKind.Type => false, 
            AsgNodeKind.Target => false, 
            AsgNodeKind.MethodName => false,
            AsgNodeKind.FieldName => false,
            AsgNodeKind.ArgumentList => false,
            AsgNodeKind.Argument => false,
            AsgNodeKind.Identifier => false, 
            AsgNodeKind.Literal => false, 
            _ => true
        };
    }

    private static string GetCompactLabel(AsgNode node)
    {
        switch (node.Source)
        {
            case MethodDeclarationSyntax m:
                var ps = string.Join(", ", m.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier}"));
                return $"{m.Identifier.Text}({ps}): {m.ReturnType}";

            case IfStatementSyntax i:
                return "if";

            case ForStatementSyntax f:
                return "for";

            case WhileStatementSyntax w:
                return "while";

            case ForEachStatementSyntax fe when node.Kind == AsgNodeKind.ForEachVariable:
                return $"{fe.Type} {fe.Identifier}";

            case ForEachStatementSyntax fe:
                return $"foreach ({fe.Type} {fe.Identifier} in {fe.Expression})";

            case VariableDeclaratorSyntax v:
                var type = (v.Parent as VariableDeclarationSyntax)?.Type.ToString() ?? "var";

                if (v.Initializer == null)
                    return $"{type} {v.Identifier}";

                var init = v.Initializer.Value;
                bool initKept = init is InvocationExpressionSyntax
                    or ObjectCreationExpressionSyntax
                    or ImplicitObjectCreationExpressionSyntax
                    or ArrayCreationExpressionSyntax
                    or ImplicitArrayCreationExpressionSyntax
                    or MemberAccessExpressionSyntax
                    or ElementAccessExpressionSyntax;

                return initKept
                    ? $"{type} {v.Identifier}" 
                    : $"{type} {v.Identifier} = {init}"; 

            case ExpressionSyntax e:
                
                
                return e.ToString();

            default:
                return node.Source?.ToString() ?? node.Label ?? node.Kind.ToString();
        }
    }
}
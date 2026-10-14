using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lab1;

class SemanticEnricher
{
    private readonly SemanticModel _model;

    public SemanticEnricher(SemanticModel model) => _model = model;

    public void Enrich(AbstractSemanticGraph graph)
    {
        foreach (var node in graph.Nodes)
            EnrichNode(node);
    }

    private void EnrichNode(AsgNode node)
    {
        if (node.Source == null) return;

        switch (node.Kind)
        {
            case AsgNodeKind.Identifier:
            case AsgNodeKind.Target:
            case AsgNodeKind.MethodName:
                FillSymbol(node);
                break;

            case AsgNodeKind.Invocation:
                FillInvocation(node);
                break;

            case AsgNodeKind.Argument:
                FillArgument(node);
                break;

            case AsgNodeKind.Type:
                FillType(node);
                break;

            case AsgNodeKind.Method:
                FillMethod(node);
                break;

            case AsgNodeKind.VariableDeclaration:
            case AsgNodeKind.ForEachVariable:
                FillDeclaredVariable(node);
                break;
        }
    }
    
    private void FillSymbol(AsgNode node)
    {
        var symbol = _model.GetSymbolInfo(node.Source!).Symbol;
        node.Symbol = symbol;

        switch (symbol)
        {
            case IMethodSymbol m:
                node.InferredType = m.ReturnType;
                node.IsStatic = m.IsStatic;
                // Для Target/MethodName можно уточнить Label
                break;

            case INamedTypeSymbol t:
                // Console, String — это тип
                node.IsStatic = true;
                node.InferredType = t;
                break;

            case ILocalSymbol l:
                node.InferredType = l.Type;
                node.Nullable = l.NullableAnnotation;
                break;

            case IParameterSymbol p:
                node.InferredType = p.Type;
                node.Nullable = p.NullableAnnotation;
                break;

            case IFieldSymbol f:
                node.InferredType = f.Type;
                node.Nullable = f.NullableAnnotation;
                node.IsStatic = f.IsStatic;
                break;

            case IPropertySymbol prop:
                node.InferredType = prop.Type;
                node.Nullable = prop.NullableAnnotation;
                node.IsStatic = prop.IsStatic;
                break;
        }
    }
    
    private void FillInvocation(AsgNode node)
    {
        var inv = (InvocationExpressionSyntax)node.Source!;
        var symbol = _model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
        if (symbol == null) return;

        node.Symbol = symbol;
        node.IsStatic = symbol.IsStatic;
        node.InferredType = symbol.ReturnType;

        // Найти дочерний Type-узел (ReturnType) и заполнить его
        var returnTypeNode = node.Out
            .Select(e => e.To)
            .FirstOrDefault(n => n.Kind == AsgNodeKind.Type);

        if (returnTypeNode != null)
        {
            returnTypeNode.InferredType = symbol.ReturnType;
            returnTypeNode.Nullable = symbol.ReturnType.NullableAnnotation;
            returnTypeNode.Label = FormatType(symbol.ReturnType);
        }

        // Уточнить MethodName (если оно есть) — сделать Label полным именем
        var methodNameNode = node.Out
            .Select(e => e.To)
            .FirstOrDefault(n => n.Kind == AsgNodeKind.MethodName);
        if (methodNameNode != null)
        {
            methodNameNode.Symbol = symbol;
            // опционально: methodNameNode.Label = $"{symbol.ContainingType.Name}.{symbol.Name}";
        }
    }
    
    private void FillArgument(AsgNode node)
    {
        var arg = (ArgumentSyntax)node.Source!;
        var typeInfo = _model.GetTypeInfo(arg.Expression);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        if (type == null) return;

        node.InferredType = type;
        node.Nullable = type.NullableAnnotation;

        // Найти дочерний Type-узел (placeholder)
        var typeNode = node.Out
            .Select(e => e.To)
            .FirstOrDefault(n => n.Kind == AsgNodeKind.Type);

        if (typeNode != null)
        {
            typeNode.InferredType = type;
            typeNode.Nullable = type.NullableAnnotation;
            typeNode.Label = FormatType(type);
        }
    }
    
    private void FillType(AsgNode node)
    {
        // Если узел уже заполнен — не трогаем
        if (node.Label != "?" && node.Label != "var") return;

        // Смотрим родителя: Type может относиться к VariableDeclaration, Argument, Invocation...
        var parent = node.In.ParentNode;
        if (parent == null) return;

        ITypeSymbol? type = parent.Kind switch
        {
            AsgNodeKind.VariableDeclaration => GetTypeForDeclarator(parent),
            AsgNodeKind.ForEachVariable     => GetTypeForForEach(parent),
            AsgNodeKind.Argument            => GetTypeForArgument(parent),
            AsgNodeKind.Invocation          => GetReturnTypeForInvocation(parent),
            AsgNodeKind.Parameter           => GetTypeForParameter(parent),
            _ => null
        };

        if (type != null)
        {
            node.InferredType = type;
            node.Nullable = type.NullableAnnotation;
            node.Label = FormatType(type);
        }
    }
    
    private static string FormatType(ITypeSymbol type)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (type.NullableAnnotation == NullableAnnotation.Annotated && !name.EndsWith("?"))
            name += "?";
        return name;
    }
}
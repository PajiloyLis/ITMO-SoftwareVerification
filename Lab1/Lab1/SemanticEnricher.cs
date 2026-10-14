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
        {
            if (node.Kind == AsgNodeKind.Type) continue; // отложим Type на второй проход
            EnrichNode(node);
        }

        // Проход 2: Type-узлы — им нужен уже готовый Symbol у родителя
        foreach (var node in graph.Nodes.Where(n => n.Kind == AsgNodeKind.Type))
            FillType(node);
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
            case AsgNodeKind.ObjectCreation:
                FillObjectCreation(node);
                break;
            
            case AsgNodeKind.ArrayCreation:
                FillArrayCreation(node);
                break;
            
            case AsgNodeKind.FieldAccess:
                FillFieldAccess(node);
                break;

            case AsgNodeKind.FieldName:
                FillSymbol(node);   // переиспользуем существующий метод
                break;
            
            case AsgNodeKind.ElementAccess:
                FillElementAccess(node);
                break;
            
            case AsgNodeKind.Parameter:
                FillParameter(node);
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
        if (node.Label is not null and not "?" and not "var") return;

        // Смотрим родителя: Type может относиться к VariableDeclaration, Argument, Invocation...
        var parent = node.In.FirstOrDefault()?.From;
        if (parent == null) return;

        ITypeSymbol? type = parent.Kind switch
        {
            AsgNodeKind.VariableDeclaration => GetTypeForDeclarator(parent),
            AsgNodeKind.ForEachVariable => GetTypeForForEach(parent),
            AsgNodeKind.Argument => GetTypeForArgument(parent),
            AsgNodeKind.Invocation => GetReturnTypeForInvocation(parent),
            AsgNodeKind.Parameter => GetTypeForParameter(parent),
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

    private void FillMethod(AsgNode node)
    {
        if (node.Source is not MethodDeclarationSyntax m) return;
        var symbol = _model.GetDeclaredSymbol(m) as IMethodSymbol;
        if (symbol == null) return;

        node.Symbol = symbol;
        node.InferredType = symbol.ReturnType;
        node.Nullable = symbol.ReturnType.NullableAnnotation;
        node.IsStatic = symbol.IsStatic;
    }

    private void FillDeclaredVariable(AsgNode node)
    {
        ISymbol? symbol = node.Source switch
        {
            VariableDeclaratorSyntax decl => _model.GetDeclaredSymbol(decl),
            ForEachStatementSyntax fe => _model.GetDeclaredSymbol(fe),
            _ => null
        };

        if (symbol == null) return;
        node.Symbol = symbol;

        ITypeSymbol? type = symbol switch
        {
            ILocalSymbol l => l.Type,
            _ => null
        };
        if (type != null)
        {
            node.InferredType = type;
            node.Nullable = type.NullableAnnotation;
        }
    }
    
    private void FillObjectCreation(AsgNode node)
    {
        ITypeSymbol? type;
        IMethodSymbol? ctor;

        switch (node.Source)
        {
            case ObjectCreationExpressionSyntax oce:
                type = _model.GetTypeInfo(oce).Type;
                ctor = _model.GetSymbolInfo(oce).Symbol as IMethodSymbol;
                break;
            case ImplicitObjectCreationExpressionSyntax ioce:
                type = _model.GetTypeInfo(ioce).Type;
                ctor = _model.GetSymbolInfo(ioce).Symbol as IMethodSymbol;
                break;
            default:
                return;
        }

        node.InferredType = type;
        node.Symbol = ctor;
        node.IsStatic = false;

        foreach (var edge in node.Out)
        {
            var child = edge.To;
            if (child.Kind == AsgNodeKind.Target && type != null)
            {
                child.InferredType = type;
                child.Symbol = type;
                child.Label = type.Name;
            }
            if (child.Kind == AsgNodeKind.Type && edge.Label == "return" && type != null)
            {
                child.InferredType = type;
                child.Nullable = type.NullableAnnotation;
                child.Label = FormatType(type);
            }
            if (child.Kind == AsgNodeKind.MethodName && ctor != null)
            {
                child.Symbol = ctor;
            }
        }
    }
    
    private void FillArrayCreation(AsgNode node)
    {
        ITypeSymbol? type = node.Source switch
        {
            ArrayCreationExpressionSyntax ace       => _model.GetTypeInfo(ace).Type,
            ImplicitArrayCreationExpressionSyntax i => _model.GetTypeInfo(i).Type,
            _ => null
        };

        if (type == null) return;
        node.InferredType = type;

        // Для массива IArrayTypeSymbol — путь к ElementType
        if (type is not IArrayTypeSymbol arrayType) return;

        // Обогатить два дочерних Type-узла, различая по label ребра
        foreach (var edge in node.Out)
        {
            var child = edge.To;
            if (child.Kind != AsgNodeKind.Type) continue;

            if (edge.Label == "element")
            {
                child.InferredType = arrayType.ElementType;
                child.Nullable = arrayType.ElementType.NullableAnnotation;
                child.Label = FormatType(arrayType.ElementType);
            }
            else if (edge.Label == "return")
            {
                child.InferredType = type;
                child.Nullable = type.NullableAnnotation;
                child.Label = FormatType(type);
            }
        }
    }
    
    private void FillFieldAccess(AsgNode node)
    {
        var type = node.Source switch
        {
            MemberAccessExpressionSyntax ma  => _model.GetTypeInfo(ma).Type,
            MemberBindingExpressionSyntax mb => _model.GetTypeInfo(mb).Type,
            _ => null
        };
        node.InferredType = type;

        // Symbol самого узла — это тот же член, что и у FieldName.
        // Можно не дублировать: у FieldName Symbol уже есть.
    }
    
    private void FillElementAccess(AsgNode node)
    {
        var ea = (ElementAccessExpressionSyntax)node.Source!;

        // Тип результата — тип элемента или тип значения индексатора
        var type = _model.GetTypeInfo(ea).Type;
        node.InferredType = type;

        // Символ: для индексатора — IPropertySymbol, для массива — null
        node.Symbol = _model.GetSymbolInfo(ea).Symbol;

        // Заполнить дочерний Type
        foreach (var edge in node.Out)
        {
            if (edge.To.Kind == AsgNodeKind.Type && edge.Label == "return" && type != null)
            {
                edge.To.InferredType = type;
                edge.To.Nullable = type.NullableAnnotation;
                edge.To.Label = FormatType(type);
            }
        }
    }
    
    private void FillParameter(AsgNode node)
    {
        if (node.Source is not ParameterSyntax p) return;
        var symbol = _model.GetDeclaredSymbol(p) as IParameterSymbol;
        if (symbol == null) return;
        node.Symbol = symbol;
        node.InferredType = symbol.Type;
        node.Nullable = symbol.NullableAnnotation;
    }

    private ITypeSymbol? GetTypeForDeclarator(AsgNode parent)
    {
        if (parent.Source is not VariableDeclaratorSyntax decl) return null;
        return (_model.GetDeclaredSymbol(decl) as ILocalSymbol)?.Type;
    }

    private ITypeSymbol? GetTypeForForEach(AsgNode parent)
    {
        if (parent.Source is not ForEachStatementSyntax fe) return null;
        return (_model.GetDeclaredSymbol(fe) as ILocalSymbol)?.Type;
    }

    private ITypeSymbol? GetTypeForArgument(AsgNode parent)
    {
        if (parent.Source is not ArgumentSyntax arg) return null;
        var info = _model.GetTypeInfo(arg.Expression);
        return info.Type ?? info.ConvertedType;
    }

    private ITypeSymbol? GetReturnTypeForInvocation(AsgNode parent)
    {
        if (parent.Symbol is IMethodSymbol m) return m.ReturnType;
        if (parent.Source is InvocationExpressionSyntax inv)
            return (_model.GetSymbolInfo(inv).Symbol as IMethodSymbol)?.ReturnType;
        return null;
    }

    private ITypeSymbol? GetTypeForParameter(AsgNode parent)
    {
        if (parent.Source is not ParameterSyntax p) return null;
        return (_model.GetDeclaredSymbol(p) as IParameterSymbol)?.Type;
    }
}
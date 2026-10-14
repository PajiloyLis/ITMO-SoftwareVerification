using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.Runtime.Loader;


var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
    .Split(Path.PathSeparator);

var references = tpa
    .Select(path => MetadataReference.CreateFromFile(path))
    .ToList();

namespace Lab1;



class AsgBuilder
{
    private AbstractSemanticGraph _graph = null!;
    private int _nextId;

    public AbstractSemanticGraph Build(MethodDeclarationSyntax method)
    {
        _graph = new AbstractSemanticGraph();
        _nextId = 0;

        _graph.Root = WalkMethod(method);

        return _graph;
    }

    private AsgNode WalkMethod(MethodDeclarationSyntax m, AsgNode? parent = null)
    {
        var methodNode = NewNode(AsgNodeKind.Method, m.Identifier.Text, m);

        if (parent != null)
            _graph.AddEdge(parent, methodNode, AsgEdgeKind.AstChild);
        else
            _graph.Root = methodNode;

        // 1. Тип возвращаемого значения
        var retType = m.ReturnType;
        var retTypeNode = NewNode(AsgNodeKind.Type, retType.ToString(), retType);
        _graph.AddEdge(methodNode, retTypeNode, AsgEdgeKind.AstChild);

        // 2. Имя функции — синтетический Identifier-узел из токена
        var nameNode = NewNode(AsgNodeKind.Identifier, m.Identifier.Text, m);
        _graph.AddEdge(methodNode, nameNode, AsgEdgeKind.AstChild);

        // 3. Параметры
        foreach (var p in m.ParameterList.Parameters)
            WalkParameter(p, methodNode);

        // 4. Тело
        if (m.Body != null)
            Walk(m.Body, methodNode);

        return methodNode;
    }

    private AsgNode WalkParameter(ParameterSyntax p, AsgNode parent)
    {
        var paramNode = NewNode(AsgNodeKind.Parameter, p.Identifier.Text, p);
        _graph.AddEdge(parent, paramNode, AsgEdgeKind.AstChild);

        // тип (если есть)
        if (p.Type != null)
        {
            var typeNode = NewNode(AsgNodeKind.Type, p.Type.ToString(), p.Type);
            _graph.AddEdge(paramNode, typeNode, AsgEdgeKind.AstChild);
        }

        // имя параметра
        var nameNode = NewNode(AsgNodeKind.Identifier, p.Identifier.Text, p);
        _graph.AddEdge(paramNode, nameNode, AsgEdgeKind.AstChild);

        // значение по умолчанию
        if (p.Default != null)
            Walk(p.Default.Value, paramNode);

        return paramNode;
    }

    private AsgNode NewNode(AsgNodeKind kind, string? label, SyntaxNode source)
    {
        var n = new AsgNode(_nextId++) { Kind = kind, Label = label, Source = source };
        _graph.Nodes.Add(n);
        return n;
    }

    private AsgNode Walk(SyntaxNode node, AsgNode parent)
    {
        // LocalDeclarationStatement — пропускаем сам узел, тело разворачиваем
        if (node is LocalDeclarationStatementSyntax localDecl)
        {
            if (localDecl.Declaration != null)
                foreach (var v in localDecl.Declaration.Variables)
                    WalkDeclarator(v, parent, localDecl.Declaration);
            return parent;
        }

        // VariableDeclaration вне LocalDeclaration (например, в for) — тоже пропускаем
        if (node is VariableDeclarationSyntax standaloneDecl)
        {
            foreach (var v in standaloneDecl.Variables)
                WalkDeclarator(v, parent, standaloneDecl);
            return parent;
        }

        // VariableDeclarator сам по себе — на всякий случай
        if (node is VariableDeclaratorSyntax decl)
            return WalkDeclarator(decl, parent, decl.Parent as VariableDeclarationSyntax);

        if (node is ExpressionStatementSyntax exprStmt)
            return Walk(exprStmt.Expression, parent);

        if (node is InvocationExpressionSyntax inv)
            return WalkInvocation(inv, parent);

        if (node is ForEachStatementSyntax fe)
            return WalkForEach(fe, parent);

        var asg = NewNode(Classify(node), LabelFor(node), node);
        _graph.AddEdge(parent, asg, AsgEdgeKind.AstChild);

        foreach (var child in node.ChildNodes())
            Walk(child, asg);

        return asg;
    }

    private AsgNode WalkForEach(ForEachStatementSyntax fe, AsgNode parent)
    {
        var n = NewNode(AsgNodeKind.ForEach, "", fe);
        _graph.AddEdge(parent, n, AsgEdgeKind.AstChild);

        var varNode = NewNode(AsgNodeKind.ForEachVariable, "", fe);
        _graph.AddEdge(n, varNode, AsgEdgeKind.AstChild);

        var nameNode = NewNode(AsgNodeKind.Identifier, fe.Identifier.Text, fe);
        _graph.AddEdge(varNode, nameNode, AsgEdgeKind.AstChild);

        // Один Type. Что написано в исходнике — то и пишем.
        var typeNode = NewNode(AsgNodeKind.Type, fe.Type.ToString(), fe.Type);
        _graph.AddEdge(varNode, typeNode, AsgEdgeKind.AstChild, "type");

        Walk(fe.Expression, n);

        if (fe.Statement != null)
            Walk(fe.Statement, n);

        return n;
    }

    private AsgNode WalkInvocation(InvocationExpressionSyntax inv, AsgNode parent)
    {
        var invNode = NewNode(AsgNodeKind.Invocation, "", inv);
        _graph.AddEdge(parent, invNode, AsgEdgeKind.AstChild);

        // 1. Target / MethodName
        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax ma:
                WalkTarget(ma.Expression, invNode);
                AddMethodName(ma.Name, invNode);
                break;

            case IdentifierNameSyntax id:
                AddMethodName(id, invNode);
                break;

            case GenericNameSyntax gen:
                AddMethodName(gen, invNode);
                break;

            default:
                Walk(inv.Expression, invNode);
                break;
        }

        // 2. ReturnType — placeholder, заполнит семантический проход
        var returnTypeNode = NewNode(AsgNodeKind.Type, null, inv);
        _graph.AddEdge(invNode, returnTypeNode, AsgEdgeKind.AstChild);

        // 3. ArgumentList
        var argListNode = NewNode(AsgNodeKind.ArgumentList, "", inv.ArgumentList);
        _graph.AddEdge(invNode, argListNode, AsgEdgeKind.AstChild);
        foreach (var arg in inv.ArgumentList.Arguments)
            WalkArgument(arg, argListNode);

        return invNode;
    }

    private void WalkTarget(ExpressionSyntax target, AsgNode invNode)
    {
        // Цель может быть простым идентификатором (Console, obj, this)
        // или сложным выражением (obj.Field, GetObj()).
        // Простой идентификатор — выделяем в отдельный Target-узел,
        // сложное — обходим как обычно (там свои узлы).
        if (target is IdentifierNameSyntax id)
        {
            var t = NewNode(AsgNodeKind.Target, id.Identifier.Text, id);
            _graph.AddEdge(invNode, t, AsgEdgeKind.AstChild);
        }
        else
        {
            Walk(target, invNode);
        }
    }

    private void AddMethodName(SimpleNameSyntax name, AsgNode invNode)
    {
        string text = name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax gen => gen.Identifier.Text,
            _ => name.ToString()
        };
        var n = NewNode(AsgNodeKind.MethodName, text, name);
        _graph.AddEdge(invNode, n, AsgEdgeKind.AstChild);
    }

    private AsgNode WalkArgument(ArgumentSyntax arg, AsgNode parent)
    {
        var argNode = NewNode(AsgNodeKind.Argument, "", arg);
        _graph.AddEdge(parent, argNode, AsgEdgeKind.AstChild);

        // Тип пока неизвестен — заглушка, заполнит семантический проход.
        var typeNode = NewNode(AsgNodeKind.Type, "?", arg);
        _graph.AddEdge(argNode, typeNode, AsgEdgeKind.AstChild);

        // Само выражение
        Walk(arg.Expression, argNode);

        return argNode;
    }

    private AsgNode WalkDeclarator(VariableDeclaratorSyntax decl, AsgNode parent,
        VariableDeclarationSyntax? varDecl)
    {
        var declNode = NewNode(AsgNodeKind.VariableDeclaration, decl.Identifier.Text, decl);
        _graph.AddEdge(parent, declNode, AsgEdgeKind.AstChild);

        // тип — берём у родительского VariableDeclaration
        if (varDecl != null)
        {
            var typeNode = NewNode(AsgNodeKind.Type, varDecl.Type.ToString(), varDecl.Type);
            _graph.AddEdge(declNode, typeNode, AsgEdgeKind.AstChild);
        }

        // имя
        var nameNode = NewNode(AsgNodeKind.Identifier, decl.Identifier.Text, decl);
        _graph.AddEdge(declNode, nameNode, AsgEdgeKind.AstChild);

        // значение — только если есть инициализатор
        if (decl.Initializer != null)
            Walk(decl.Initializer.Value, declNode);

        return declNode;
    }

    private static AsgNodeKind Classify(SyntaxNode node) => node switch
    {
        BlockSyntax => AsgNodeKind.Block,
        IdentifierNameSyntax => AsgNodeKind.Identifier,
        LiteralExpressionSyntax => AsgNodeKind.Literal,
        StatementSyntax => AsgNodeKind.Statement,
        TypeSyntax => AsgNodeKind.Type,
        ExpressionSyntax => AsgNodeKind.Expression,
        _ => AsgNodeKind.Expression
    };

    private static string LabelFor(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ParameterSyntax p => p.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        LiteralExpressionSyntax l => l.Token.Text,
        BlockSyntax => "{ }",
        BinaryExpressionSyntax b => BinaryOperatorSymbol(b.Kind()),
        AssignmentExpressionSyntax a => AssignmentOperatorSymbol(a.Kind()),
        PrefixUnaryExpressionSyntax p => UnaryOperatorSymbol(p.Kind()),
        PostfixUnaryExpressionSyntax p => UnaryOperatorSymbol(p.Kind()),
        _ => node.Kind().ToString()
    };

    private static string BinaryOperatorSymbol(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddExpression => "+",
        SyntaxKind.SubtractExpression => "-",
        SyntaxKind.MultiplyExpression => "*",
        SyntaxKind.DivideExpression => "/",
        SyntaxKind.ModuloExpression => "%",

        SyntaxKind.EqualsExpression => "==",
        SyntaxKind.NotEqualsExpression => "!=",
        SyntaxKind.LessThanExpression => "<",
        SyntaxKind.LessThanOrEqualExpression => "<=",
        SyntaxKind.GreaterThanExpression => ">",
        SyntaxKind.GreaterThanOrEqualExpression => ">=",

        SyntaxKind.LogicalAndExpression => "&&",
        SyntaxKind.LogicalOrExpression => "||",
        SyntaxKind.BitwiseAndExpression => "&",
        SyntaxKind.BitwiseOrExpression => "|",
        SyntaxKind.ExclusiveOrExpression => "^",

        SyntaxKind.LeftShiftExpression => "<<",
        SyntaxKind.RightShiftExpression => ">>",
        SyntaxKind.UnsignedRightShiftExpression => ">>>",

        SyntaxKind.CoalesceExpression => "??",

        SyntaxKind.IsExpression => "is",
        SyntaxKind.AsExpression => "as",

        _ => kind.ToString()
    };

    private static string AssignmentOperatorSymbol(SyntaxKind kind) => kind switch
    {
        SyntaxKind.SimpleAssignmentExpression => "=",
        SyntaxKind.AddAssignmentExpression => "+=",
        SyntaxKind.SubtractAssignmentExpression => "-=",
        SyntaxKind.MultiplyAssignmentExpression => "*=",
        SyntaxKind.DivideAssignmentExpression => "/=",
        SyntaxKind.ModuloAssignmentExpression => "%=",
        SyntaxKind.AndAssignmentExpression => "&=",
        SyntaxKind.OrAssignmentExpression => "|=",
        SyntaxKind.ExclusiveOrAssignmentExpression => "^=",
        SyntaxKind.LeftShiftAssignmentExpression => "<<=",
        SyntaxKind.RightShiftAssignmentExpression => ">>=",
        SyntaxKind.UnsignedRightShiftAssignmentExpression => ">>>=",
        SyntaxKind.CoalesceAssignmentExpression => "??=",
        _ => kind.ToString()
    };

    private static string UnaryOperatorSymbol(SyntaxKind kind) => kind switch
    {
        // prefix
        SyntaxKind.UnaryPlusExpression => "+",
        SyntaxKind.UnaryMinusExpression => "-",
        SyntaxKind.LogicalNotExpression => "!",
        SyntaxKind.BitwiseNotExpression => "~",
        SyntaxKind.PreIncrementExpression => "++",
        SyntaxKind.PreDecrementExpression => "--",
        SyntaxKind.AddressOfExpression => "&",
        SyntaxKind.PointerIndirectionExpression => "*",
        SyntaxKind.IndexExpression => "^", // ^x (index from end)

        // postfix
        SyntaxKind.PostIncrementExpression => "++",
        SyntaxKind.PostDecrementExpression => "--",
        SyntaxKind.SuppressNullableWarningExpression => "!", // x! (null-forgiving)

        _ => kind.ToString()
    };
}
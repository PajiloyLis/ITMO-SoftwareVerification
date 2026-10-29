using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.Runtime.Loader;

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

        var retType = m.ReturnType;
        var retTypeNode = NewNode(AsgNodeKind.Type, retType.ToString(), retType);
        _graph.AddEdge(methodNode, retTypeNode, AsgEdgeKind.AstChild);

        var nameNode = NewNode(AsgNodeKind.Identifier, m.Identifier.Text, m);
        _graph.AddEdge(methodNode, nameNode, AsgEdgeKind.AstChild);

        foreach (var p in m.ParameterList.Parameters)
            WalkParameter(p, methodNode);

        if (m.Body != null)
            Walk(m.Body, methodNode);

        return methodNode;
    }

    private AsgNode WalkParameter(ParameterSyntax p, AsgNode parent)
    {
        var paramNode = NewNode(AsgNodeKind.Parameter, p.Identifier.Text, p);
        _graph.AddEdge(parent, paramNode, AsgEdgeKind.AstChild);

        if (p.Type != null)
        {
            var typeNode = NewNode(AsgNodeKind.Type, p.Type.ToString(), p.Type);
            _graph.AddEdge(paramNode, typeNode, AsgEdgeKind.AstChild);
        }

        var nameNode = NewNode(AsgNodeKind.Identifier, p.Identifier.Text, p);
        _graph.AddEdge(paramNode, nameNode, AsgEdgeKind.AstChild);

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
        if (node is BreakStatementSyntax or ContinueStatementSyntax)
            return parent;
        if (node is LocalDeclarationStatementSyntax localDecl)
        {
            if (localDecl.Declaration != null)
                foreach (var v in localDecl.Declaration.Variables)
                    WalkDeclarator(v, parent, localDecl.Declaration);
            return parent;
        }

        if (node is VariableDeclarationSyntax standaloneDecl)
        {
            foreach (var v in standaloneDecl.Variables)
                WalkDeclarator(v, parent, standaloneDecl);
            return parent;
        }

        if (node is VariableDeclaratorSyntax decl)
            return WalkDeclarator(decl, parent, decl.Parent as VariableDeclarationSyntax);

        if (node is ExpressionStatementSyntax exprStmt)
            return Walk(exprStmt.Expression, parent);

        if (node is InvocationExpressionSyntax inv)
            return WalkInvocation(inv, parent);

        if (node is ForEachStatementSyntax fe)
            return WalkForEach(fe, parent);


        if (node is ObjectCreationExpressionSyntax oce)
            return WalkObjectCreation(oce, parent);

        if (node is ImplicitObjectCreationExpressionSyntax ioce)
            return WalkImplicitObjectCreation(ioce, parent);

        if (node is ArrayCreationExpressionSyntax ace)
            return WalkArrayCreation(ace, parent);

        if (node is ImplicitArrayCreationExpressionSyntax iace)
            return WalkArrayCreation(iace, parent);

        if (node is MemberAccessExpressionSyntax ma)
            return WalkFieldAccess(ma, parent);

        if (node is MemberBindingExpressionSyntax mb)
            return WalkMemberBinding(mb, parent);

        if (node is ElementAccessExpressionSyntax ea)
            return WalkElementAccess(ea, parent);

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

        
        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax ma:
                WalkTarget(ma.Expression, invNode);
                AddMethodName(ma.Name, invNode);
                break;

            case MemberBindingExpressionSyntax mb:
                AddMethodName(mb.Name, invNode);
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

        
        var returnTypeNode = NewNode(AsgNodeKind.Type, null, inv);
        _graph.AddEdge(invNode, returnTypeNode, AsgEdgeKind.AstChild);

        
        var argListNode = NewNode(AsgNodeKind.ArgumentList, "", inv.ArgumentList);
        _graph.AddEdge(invNode, argListNode, AsgEdgeKind.AstChild);
        foreach (var arg in inv.ArgumentList.Arguments)
            WalkArgument(arg, argListNode);

        return invNode;
    }

    private void WalkTarget(ExpressionSyntax target, AsgNode invNode)
    {
        
        
        
        
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

        
        var typeNode = NewNode(AsgNodeKind.Type, "?", arg);
        _graph.AddEdge(argNode, typeNode, AsgEdgeKind.AstChild);

        
        Walk(arg.Expression, argNode);

        return argNode;
    }

    private AsgNode WalkDeclarator(VariableDeclaratorSyntax decl, AsgNode parent,
        VariableDeclarationSyntax? varDecl)
    {
        var declNode = NewNode(AsgNodeKind.VariableDeclaration, decl.Identifier.Text, decl);
        _graph.AddEdge(parent, declNode, AsgEdgeKind.AstChild);

        
        if (varDecl != null)
        {
            var typeNode = NewNode(AsgNodeKind.Type, varDecl.Type.ToString(), varDecl.Type);
            _graph.AddEdge(declNode, typeNode, AsgEdgeKind.AstChild);
        }

        
        var nameNode = NewNode(AsgNodeKind.Identifier, decl.Identifier.Text, decl);
        _graph.AddEdge(declNode, nameNode, AsgEdgeKind.AstChild);

        
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
        PredefinedTypeSyntax p => p.Keyword.Text, 
        TypeSyntax t => t.ToString(), 
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
        
        SyntaxKind.UnaryPlusExpression => "+",
        SyntaxKind.UnaryMinusExpression => "-",
        SyntaxKind.LogicalNotExpression => "!",
        SyntaxKind.BitwiseNotExpression => "~",
        SyntaxKind.PreIncrementExpression => "++",
        SyntaxKind.PreDecrementExpression => "--",
        SyntaxKind.AddressOfExpression => "&",
        SyntaxKind.PointerIndirectionExpression => "*",
        SyntaxKind.IndexExpression => "^", 

        
        SyntaxKind.PostIncrementExpression => "++",
        SyntaxKind.PostDecrementExpression => "--",
        SyntaxKind.SuppressNullableWarningExpression => "!", 

        _ => kind.ToString()
    };

    private AsgNode WalkObjectCreation(ObjectCreationExpressionSyntax oce, AsgNode parent)
    {
        var n = NewNode(AsgNodeKind.ObjectCreation, "", oce);
        _graph.AddEdge(parent, n, AsgEdgeKind.AstChild);

        
        var typeNode = NewNode(AsgNodeKind.Target, oce.Type.ToString(), oce.Type);
        _graph.AddEdge(n, typeNode, AsgEdgeKind.AstChild, "target");

        
        var ctorName = NewNode(AsgNodeKind.MethodName, "#ctor", oce);
        _graph.AddEdge(n, ctorName, AsgEdgeKind.AstChild);

        
        var retType = NewNode(AsgNodeKind.Type, null, oce);
        _graph.AddEdge(n, retType, AsgEdgeKind.AstChild, "return");

        
        if (oce.ArgumentList != null)
        {
            var argListNode = NewNode(AsgNodeKind.ArgumentList, "", oce.ArgumentList);
            _graph.AddEdge(n, argListNode, AsgEdgeKind.AstChild);
            foreach (var arg in oce.ArgumentList.Arguments)
                WalkArgument(arg, argListNode);
        }

        
        if (oce.Initializer != null)
            Walk(oce.Initializer, n);

        return n;
    }

    private AsgNode WalkImplicitObjectCreation(
        ImplicitObjectCreationExpressionSyntax ioce, AsgNode parent)
    {
        var n = NewNode(AsgNodeKind.ObjectCreation, "", ioce);
        _graph.AddEdge(parent, n, AsgEdgeKind.AstChild);

        
        var typeNode = NewNode(AsgNodeKind.Target, null, ioce);
        _graph.AddEdge(n, typeNode, AsgEdgeKind.AstChild, "target");

        
        var ctorName = NewNode(AsgNodeKind.MethodName, "#ctor", ioce);
        _graph.AddEdge(n, ctorName, AsgEdgeKind.AstChild);

        
        var retType = NewNode(AsgNodeKind.Type, null, ioce);
        _graph.AddEdge(n, retType, AsgEdgeKind.AstChild, "return");

        
        if (ioce.ArgumentList != null)
        {
            var argListNode = NewNode(AsgNodeKind.ArgumentList, "", ioce.ArgumentList);
            _graph.AddEdge(n, argListNode, AsgEdgeKind.AstChild);
            foreach (var arg in ioce.ArgumentList.Arguments)
                WalkArgument(arg, argListNode);
        }

        
        if (ioce.Initializer != null)
            Walk(ioce.Initializer, n);

        return n;
    }

    private AsgNode WalkArrayCreation(SyntaxNode node, AsgNode parent)
    {
        var n = NewNode(AsgNodeKind.ArrayCreation, "", node);
        _graph.AddEdge(parent, n, AsgEdgeKind.AstChild);

        switch (node)
        {
            case ArrayCreationExpressionSyntax ace:
                
                var elemType = NewNode(AsgNodeKind.Type,
                    ace.Type.ElementType.ToString(), ace.Type.ElementType);
                _graph.AddEdge(n, elemType, AsgEdgeKind.AstChild, "element");

                
                var retType = NewNode(AsgNodeKind.Type, null, ace);
                _graph.AddEdge(n, retType, AsgEdgeKind.AstChild, "return");

                
                
                
                
                foreach (var rank in ace.Type.RankSpecifiers)
                foreach (var size in rank.Sizes)
                {
                    var sizeWrapper = NewNode(AsgNodeKind.ArraySize, "", size);
                    _graph.AddEdge(n, sizeWrapper, AsgEdgeKind.AstChild);
                    Walk(size, sizeWrapper);
                }

                
                if (ace.Initializer != null)
                    Walk(ace.Initializer, n);
                break;

            case ImplicitArrayCreationExpressionSyntax iace:
                
                var elemTypeI = NewNode(AsgNodeKind.Type, null, iace);
                _graph.AddEdge(n, elemTypeI, AsgEdgeKind.AstChild, "element");

                
                var retTypeI = NewNode(AsgNodeKind.Type, null, iace);
                _graph.AddEdge(n, retTypeI, AsgEdgeKind.AstChild, "return");

                
                if (iace.Initializer != null)
                    Walk(iace.Initializer, n);
                break;
        }

        return n;
    }

    private AsgNode WalkFieldAccess(MemberAccessExpressionSyntax ma, AsgNode parent)
    {
        var n = NewNode(AsgNodeKind.FieldAccess, "", ma);
        _graph.AddEdge(parent, n, AsgEdgeKind.AstChild);

        
        WalkTarget(ma.Expression, n);

        
        var nameNode = NewNode(AsgNodeKind.FieldName, ma.Name.Identifier.Text, ma.Name);
        _graph.AddEdge(n, nameNode, AsgEdgeKind.AstChild);

        return n;
    }

    private AsgNode WalkMemberBinding(MemberBindingExpressionSyntax mb, AsgNode parent)
    {
        var n = NewNode(AsgNodeKind.FieldAccess, "", mb);
        _graph.AddEdge(parent, n, AsgEdgeKind.AstChild);

        
        

        var nameNode = NewNode(AsgNodeKind.FieldName, mb.Name.Identifier.Text, mb.Name);
        _graph.AddEdge(n, nameNode, AsgEdgeKind.AstChild);

        return n;
    }

    private AsgNode WalkElementAccess(ElementAccessExpressionSyntax ea, AsgNode parent)
    {
        var n = NewNode(AsgNodeKind.ElementAccess, "", ea);
        _graph.AddEdge(parent, n, AsgEdgeKind.AstChild);

        
        WalkTarget(ea.Expression, n);

        
        var retType = NewNode(AsgNodeKind.Type, null, ea);
        _graph.AddEdge(n, retType, AsgEdgeKind.AstChild, "return");

        
        var argList = NewNode(AsgNodeKind.ArgumentList, "", ea.ArgumentList);
        _graph.AddEdge(n, argList, AsgEdgeKind.AstChild);
        foreach (var arg in ea.ArgumentList.Arguments)
            WalkArgument(arg, argList);

        return n;
    }
}
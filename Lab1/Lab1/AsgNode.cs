using Microsoft.CodeAnalysis;

namespace Lab1;

enum AsgNodeKind
{
    Method,
    Parameter,
    Block,
    Statement,
    VariableDeclaration,
    Expression,
    Identifier,
    Literal,
    Type,
    Invocation,
    ArgumentList,
    Argument,
    Target,
    MethodName,
    ForEach,
    ForEachVariable,
    ObjectCreation,
    ArrayCreation,
    ArraySize,
    FieldAccess,
    FieldName,
    ElementAccess,
}

class AsgNode
{
    public int Id;
    public AsgNodeKind Kind;
    public string? Label;
    public SyntaxNode? Source;

    public ISymbol? Symbol;
    public ITypeSymbol? InferredType;
    public NullableAnnotation Nullable = NullableAnnotation.None;
    public bool IsStatic;

    public List<AsgEdge> Out = new();
    public List<AsgEdge> In = new();

    public AsgNode(int id) => Id = id;
}
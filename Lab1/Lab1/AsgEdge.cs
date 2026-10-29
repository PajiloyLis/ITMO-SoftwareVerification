namespace Lab1;

enum AsgEdgeKind
{
    AstChild,
    DataFlow,
    ControlDep,
    ControlFlow
}

class AsgEdge
{
    public AsgNode From, To;
    public AsgEdgeKind Kind;
    public string? Label;

    public AsgEdge(AsgNode from, AsgNode to, AsgEdgeKind kind, string? label = null)
    {
        From = from;
        To = to;
        Kind = kind;
        Label = label;
    }
}
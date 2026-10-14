namespace Lab1;

class AbstractSemanticGraph
{
    public AsgNode Root = null!;
    public List<AsgNode> Nodes = new();

    public void AddEdge(AsgNode from, AsgNode to, AsgEdgeKind kind, string? label = null)
    {
        var e = new AsgEdge(from, to, kind, label);
        from.Out.Add(e);
        to.In.Add(e);
    }
}
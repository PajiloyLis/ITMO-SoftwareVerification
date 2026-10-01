namespace Lab1;

class ControlFlowGraph
{
    public BasicBlock Start = null!;
    public BasicBlock End   = null!;
    public BasicBlock Entry = null!; // первый исполняемый блок (после Start)

    public List<BasicBlock> Blocks = new();

    public void AddEdge(BasicBlock from, BasicBlock to, EdgeKind kind, string? label = null)
    {
        var edge = new Edge(from, to, kind, label);
        from.Successors.Add(edge);
        to.Predecessors.Add(edge);
    }
}
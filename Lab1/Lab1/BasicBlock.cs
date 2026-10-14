namespace Lab1;

enum TerminatorKind
{
    Fallthrough,
    Return,
    Branch,
    Goto,
    Throw
}

enum BlockKind
{
    Normal,
    Start,
    End
}

class BasicBlock
{
    public int Id;
    public BasicBlock(int id) => Id = id;
    public BlockKind Kind = BlockKind.Normal;
    public List<Instruction> Instructions = new List<Instruction>();
    public TerminatorKind Terminator = TerminatorKind.Fallthrough;

    public List<CfgEdge> Successors = new List<CfgEdge>();
    public List<CfgEdge> Predecessors = new List<CfgEdge>();
}
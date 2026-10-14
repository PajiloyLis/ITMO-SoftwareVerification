namespace Lab1;

enum EdgeKind
{
    Fallthrough, 
    True,        
    False,       
    BackEdge,    
    Break,       
    Continue,    
    Call,        
    Return,      
    Exception    
}

class CfgEdge
{
    public BasicBlock From, To;
    public EdgeKind Kind;
    public string? Label;
    
    public CfgEdge(BasicBlock from, BasicBlock to, EdgeKind kind, string? label = null)
    {
        From = from;
        To = to;
        Kind = kind;
        Label = label;
    }
}
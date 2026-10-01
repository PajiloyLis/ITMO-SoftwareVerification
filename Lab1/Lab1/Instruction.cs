using Microsoft.CodeAnalysis;

namespace Lab1;

public class Instruction
{
    public string? Text;
    public SyntaxNode? SourceCodeNode;
    public Instruction(string text, SyntaxNode? source = null)
    {
        Text = text;
        SourceCodeNode = source;
    }
}
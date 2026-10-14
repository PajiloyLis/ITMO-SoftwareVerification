using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lab1;

class SemanticContext
{
    public CSharpCompilation Compilation { get; }
    public SemanticModel Model { get; }

    public SemanticContext(SyntaxTree tree)
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));

        Compilation = CSharpCompilation.Create(
            "asg",
            new[] { tree },
            tpa,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Model = Compilation.GetSemanticModel(tree);
    }
}
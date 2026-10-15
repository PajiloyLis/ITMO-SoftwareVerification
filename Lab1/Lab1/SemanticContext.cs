using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lab1;

class SemanticContext
{
    public CSharpCompilation Compilation { get; }
    public SemanticModel Model { get; }

    public SemanticContext(SyntaxTree tree)
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SyntaxNode).Assembly.Location), 
            MetadataReference.CreateFromFile(typeof(CSharpSyntaxTree).Assembly.Location),
        };

        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Distinct();

        foreach (var path in tpa)
        {
            try
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
            catch
            {
            }
        }

        var implicitUsings = """
                             global using global::System;
                             global using global::System.Collections.Generic;
                             global using global::System.IO;
                             global using global::System.Linq;
                             global using global::System.Net.Http;
                             global using global::System.Threading;
                             global using global::System.Threading.Tasks;
                             """;
        var usingsTree = CSharpSyntaxTree.ParseText(implicitUsings);
        
        Compilation = CSharpCompilation.Create(
            "asg",
            new[] { tree, usingsTree},
            refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        Model = Compilation.GetSemanticModel(tree);
        foreach (var d in Compilation.GetDiagnostics()
                     .Where(d => d.Severity == DiagnosticSeverity.Error)
                     .Take(20))
        {
            Console.WriteLine($"[{d.Id}] {d.GetMessage()} @ {d.Location}");
        }
    }
}
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Lab1;

static class DotExporter
{
    public static string ToDot(ControlFlowGraph cfg, string title = "CFG")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"digraph \"{Esc(title)}\" {{");
        sb.AppendLine("  rankdir=TB;");
        sb.AppendLine("  node [fontname=\"Consolas\", fontsize=10];");
        sb.AppendLine("  edge [fontname=\"Consolas\", fontsize=9];");
        sb.AppendLine();


        foreach (var b in cfg.Blocks)
        {
            string label = BuildLabel(b);
            string shape = ShapeFor(b);
            string style = StyleFor(b);
            string styleAttr = string.IsNullOrEmpty(style) ? "" : $", style={style}";
            sb.AppendLine($"  B{b.Id} [label=\"{label}\", shape={shape}{styleAttr}];");
        }

        sb.AppendLine();

        foreach (var b in cfg.Blocks)
        foreach (var e in b.Successors)
            sb.AppendLine($"  B{b.Id} -> B{e.To.Id}{EdgeAttributes(e)};");

        sb.AppendLine("}");
        return sb.ToString();
    }

    public static void SaveDot(ControlFlowGraph cfg, string path, string title = "CFG")
        => File.WriteAllText(path, ToDot(cfg, title));

    public static bool Render(string dotPath, string outputPath, string format = "svg")
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dot",
                Arguments = $"-T{format} \"{dotPath}\" -o \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Export(ControlFlowGraph cfg, string basePath, string title, string format = "svg")
    {
        string dotPath = basePath + ".dot";
        string outPath = basePath + "." + format;
        SaveDot(cfg, dotPath, title);
        if (Render(dotPath, outPath, format))
            Console.WriteLine($"  -> {outPath}");
        else
            Console.WriteLine($"  -> {dotPath} (Graphviz не найден, рендер пропущен)");
    }

    private static string BuildLabel(BasicBlock b) => b.Kind switch
    {
        BlockKind.Start => "Начало",
        BlockKind.End => "Конец",
        _ => BuildInstructionLabel(b)
    };

    private static string BuildInstructionLabel(BasicBlock b)
    {
        if (b.Instructions.Count == 0)
            return b.Terminator != TerminatorKind.Fallthrough
                ? Esc($"[{b.Terminator}]")
                : " ";

        bool centered = b.Terminator == TerminatorKind.Branch;
        string br = centered ? "\\n" : "\\l";

        var parts = b.Instructions
            .Select(i => Esc(FormatInstruction(i.Text, centered)))
            .ToList();

        return centered
            ? string.Join(br, parts)
            : string.Join("", parts.Select(p => p + br));
    }

    private static string FormatInstruction(string? text, bool isBranch)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!isBranch) return text;

        if ((text.StartsWith("if (") || text.StartsWith("while (")) && text.EndsWith(")"))
            return text.Substring(text.IndexOf('(') + 1, text.Length - text.IndexOf('(') - 2);

        return text;
    }

    private static string ShapeFor(BasicBlock b) => b.Kind switch
    {
        BlockKind.Start or BlockKind.End => "box",
        _ => b.Terminator switch
        {
            TerminatorKind.Branch => "diamond",
            _ => "box"
        }
    };

    private static string StyleFor(BasicBlock b) =>
        b.Kind is BlockKind.Start or BlockKind.End ? "rounded" : "";

    private static string EdgeAttributes(CfgEdge e)
    {
        var attrs = new List<string>();

        string? label = e.Label ?? e.Kind switch
        {
            EdgeKind.True => "true",
            EdgeKind.False => "false",
            EdgeKind.BackEdge => null,
            EdgeKind.Break => "break",
            EdgeKind.Continue => "continue",
            _ => null
        };

        if (!string.IsNullOrEmpty(label))
            attrs.Add($"label=\"{Esc(label)}\"");

        return attrs.Count > 0 ? " [" + string.Join(", ", attrs) + "]" : "";
    }

    private static string Esc(string s) =>
        s.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "")
            .Replace("\n", "\\n");

    public static string AsgToDot(AbstractSemanticGraph graph, string title = "ASG")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"digraph \"{Esc(title)}\" {{");
        sb.AppendLine("  rankdir=TB;");
        sb.AppendLine("  node [fontname=\"Consolas\", fontsize=10];");
        sb.AppendLine("  edge [fontname=\"Consolas\", fontsize=9];");
        sb.AppendLine();

        foreach (var n in graph.Nodes)
        {
            string shape = n.Kind switch
            {
                AsgNodeKind.Method => "folder",
                AsgNodeKind.Identifier => "ellipse",
                AsgNodeKind.Literal => "plaintext",
                AsgNodeKind.Type => "component",
                AsgNodeKind.VariableDeclaration => "box",
                _ => "box"
            };

            string html = BuildHtmlLabel(n);
            sb.AppendLine($"  N{n.Id} [label={html}, shape={shape}];");
        }

        sb.AppendLine();

        foreach (var n in graph.Nodes)
        foreach (var e in n.Out)
        {
            string lbl = string.IsNullOrEmpty(e.Label)
                ? ""
                : $" [label=\"{Esc(e.Label)}\"]";
            sb.AppendLine($"  N{n.Id} -> N{e.To.Id}{lbl};");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// HTML-метка вида:
    ///   <B>Kind</B><BR/>value
    /// Обратите внимание: label без кавычек, потому что это HTML-label.
    /// </summary>
    private static string BuildHtmlLabel(AsgNode n)
    {
        string kind = EscHtml(n.Kind.ToString());
        string value = n.Label is null or "" ? "" : $"<BR/>{EscHtml(n.Label)}";

        var badges = new List<string>();
        if (n.IsStatic) badges.Add("static");

        // Показываем nullable только у Type-узлов — там это информация,
        // а у Identifier/VariableDeclaration это шум: сам тип уже помечен.
        if (n.Kind == AsgNodeKind.Type
            && n.Nullable == NullableAnnotation.Annotated)
            badges.Add("nullable");

        string extra = badges.Count > 0
            ? $"<BR/><FONT POINT-SIZE=\"8\">{string.Join(", ", badges)}</FONT>"
            : "";

        return value.Length > 0
            ? $"<<B>{kind}</B><BR/>{value}{extra}>"
            : $"<<B>{kind}</B>{extra}>";
    }

    private static string EscHtml(string s) =>
        s.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

    public static void SaveAsgDot(AbstractSemanticGraph graph, string path, string title = "ASG")
        => File.WriteAllText(path, AsgToDot(graph, title));
}
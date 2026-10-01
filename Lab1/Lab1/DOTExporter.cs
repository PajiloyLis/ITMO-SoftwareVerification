using System.Diagnostics;
using System.Text;

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
        BlockKind.Start => "Start",
        BlockKind.End   => "End",
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

        var parts = b.Instructions.Select(i => Esc(i.Text ?? "")).ToList();

        return centered
            ? string.Join(br, parts)
            : string.Join("", parts.Select(p => p + br));
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

    // Все рёбра — сплошные. Подписи у рёбер тоже убраны.
    private static string EdgeAttributes(Edge e)
    {
        var attrs = new List<string>();

        // подписи, которые несут смысл
        string? label = e.Label ?? e.Kind switch
        {
            EdgeKind.True     => "true",
            EdgeKind.False    => "false",
            EdgeKind.BackEdge => null,   // back обычно и так понятен по форме
            EdgeKind.Break    => "break",
            EdgeKind.Continue => "continue",
            _ => null
        };

        if (!string.IsNullOrEmpty(label))
            attrs.Add($"label=\"{Esc(label)}\"");

        // никаких style=dashed/dotted — всё сплошное
        return attrs.Count > 0 ? " [" + string.Join(", ", attrs) + "]" : "";
    }

    private static string Esc(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\r", "")
         .Replace("\n", "\\n");
}
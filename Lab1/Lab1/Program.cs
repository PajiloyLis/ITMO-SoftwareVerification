using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lab1;

class Program
{
    static void Main()
    {
        string saveImageDir = "./images";
        string saveDotDir = "./dots";
        string imageFormat = "pdf";
        bool compactAsg = true;
        while (true)
        {
            Console.Clear();
            Console.WriteLine("Текущие настройки:");
            Console.WriteLine($"  1. Директория .dot:     {Path.GetFullPath(saveDotDir)}");
            Console.WriteLine($"  2. Директория картинок: {Path.GetFullPath(saveImageDir)}");
            Console.WriteLine($"  3. Формат картинок:     {imageFormat}");
            Console.WriteLine($"  4. Компактный ASG:      {(compactAsg ? "Да" : "Нет")}");
            Console.WriteLine();
            Console.WriteLine("Выберите опцию:");
            Console.WriteLine("0. Выход");
            Console.WriteLine("1. Установить директорию сохранения .dot файла");
            Console.WriteLine("2. Установить директорию сохранения изображений");
            Console.WriteLine("3. Установить формат изображения");
            Console.WriteLine("4. Переключить режим ASG (компактный/полный)");
            Console.WriteLine("5. Выбрать способ ввода и построить граф");
            Console.Write("> ");

            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "0":
                    return;

                case "1":
                    Console.Write("Путь к директории для .dot: ");
                    var dotDir = Console.ReadLine()?.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(dotDir))
                        saveDotDir = dotDir;
                    break;

                case "2":
                    Console.Write("Путь к директории для изображений: ");
                    var imgDir = Console.ReadLine()?.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(imgDir))
                        saveImageDir = imgDir;
                    break;

                case "3":
                    Console.Write("Формат (png/svg/pdf): ");
                    var fmt = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (fmt is "png" or "svg" or "pdf")
                        imageFormat = fmt;
                    else
                        Console.WriteLine("Некорректный формат");
                    Pause();
                    break;

                case "4":
                    compactAsg = !compactAsg;
                    break;

                case "5":
                    RunBuild(saveDotDir, saveImageDir, imageFormat, compactAsg);
                    break;
                default:
                    Console.WriteLine("Неизвестная опция");
                    Pause();
                    break;
            }
        }
    }

    static void RunBuild(string saveDotDir, string saveImageDir, string imageFormat, bool compactAsg)
    {
        Console.Clear();
        Console.WriteLine("Способ ввода:");
        Console.WriteLine("  1. Из файла");
        Console.WriteLine("  2. С клавиатуры (завершить Ctrl+D на Linux, Ctrl+Z на Windows)");
        Console.Write("> ");

        var mode = Console.ReadLine()?.Trim();

        string code;
        if (mode == "1")
        {
            Console.Write("Путь к файлу с кодом: ");
            var path = Console.ReadLine()?.Trim().Trim('"');
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Console.WriteLine("Файл не найден.");
                Pause();
                return;
            }

            code = File.ReadAllText(path);
        }
        else if (mode == "2")
        {
            Console.WriteLine();
            code = ReadMultiLine();
        }
        else
        {
            Console.WriteLine("Отмена.");
            Pause();
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            Console.WriteLine("Пустой код, отмена.");
            Pause();
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Что строить?");
        Console.WriteLine("  1. Только CFG");
        Console.WriteLine("  2. Только ASG");
        Console.WriteLine("  3. И CFG, и ASG");
        Console.Write("> ");
        var what = Console.ReadLine()?.Trim();

        bool buildCfg = what is "1" or "3";
        bool buildAsg = what is "2" or "3";
        if (!buildCfg && !buildAsg)
        {
            Console.WriteLine("Отмена.");
            Pause();
            return;
        }

        Directory.CreateDirectory(saveDotDir);
        Directory.CreateDirectory(saveImageDir);

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var semantic = new SemanticContext(tree);

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        if (methods.Count == 0)
        {
            Console.WriteLine("Методы не найдены.");
            Pause();
            return;
        }

        foreach (var method in methods)
        {
            string name = method.Identifier.Text;
            Console.WriteLine($"=== {name} ===");

            if (buildCfg)
            {
                var cfg = new CfgBuilder().Build(method);
                string dotPath = Path.Combine(saveDotDir, name + ".cfg.dot");
                string imgPath = Path.Combine(saveImageDir, name + ".cfg." + imageFormat);

                DotExporter.SaveDot(cfg, dotPath, name);
                Console.WriteLine($"  cfg dot: {dotPath}");
                Console.WriteLine(DotExporter.Render(dotPath, imgPath, imageFormat)
                    ? $"  cfg img: {imgPath}"
                    : "  cfg img: не удалось (проверьте, что dot в PATH)");
            }

            if (buildAsg)
            {
                var cfg = new CfgBuilder().Build(method);
                var asg = new AsgBuilder().Build(method);
                new SemanticEnricher(semantic.Model).Enrich(asg);
                new DefUseCollector().Collect(asg);

                new ControlFlowMapper().AddEdges(asg, cfg); 
                
                string dotPath = Path.Combine(saveDotDir, name + ".asg.dot");
                string imgPath = Path.Combine(saveImageDir, name + ".asg." + imageFormat);

                DotExporter.SaveAsgDot(asg, dotPath, name + " (ASG)", compactAsg);
                Console.WriteLine($"  asg dot: {dotPath}");
                Console.WriteLine(DotExporter.Render(dotPath, imgPath, imageFormat)
                    ? $"  asg img: {imgPath}"
                    : "  asg img: не удалось (проверьте, что dot в PATH)");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Готово.");
        Pause();
    }

    static string ReadMultiLine()
    {
        var sb = new StringBuilder();

        Console.WriteLine("Введите код.");
        Console.WriteLine("  Завершение ввода: Ctrl+D");
        Console.WriteLine(new string('-', 60));

        while (true)
        {
            string? line = Console.ReadLine();

            if (line == null)
                break;

            sb.AppendLine(line);
        }

        Console.WriteLine(new string('-', 60));
        return sb.ToString();
    }

    static void Pause()
    {
        Console.WriteLine("Нажмите любую клавишу...");
        Console.ReadKey(true);
    }
}
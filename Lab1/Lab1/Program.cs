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
        string imageFormat = "png";

        while (true)
        {
            Console.Clear();
            Console.WriteLine("Текущие настройки:");
            Console.WriteLine($"  1. Директория .dot:     {Path.GetFullPath(saveDotDir)}");
            Console.WriteLine($"  2. Директория картинок: {Path.GetFullPath(saveImageDir)}");
            Console.WriteLine($"  3. Формат картинок:     {imageFormat}");
            Console.WriteLine();
            Console.WriteLine("Выберите опцию:");
            Console.WriteLine("0. Выход");
            Console.WriteLine("1. Установить директорию сохранения .dot файла");
            Console.WriteLine("2. Установить директорию сохранения изображений");
            Console.WriteLine("3. Установить формат изображения");
            Console.WriteLine("4. Выбрать способ ввода и построить CFG");
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
                        Console.WriteLine($"Некорректный формат, оставляю {imageFormat}");
                    Pause();
                    break;

                case "4":
                    RunBuild(saveDotDir, saveImageDir, imageFormat);
                    break;

                default:
                    Console.WriteLine("Неизвестная опция");
                    Pause();
                    break;
            }
        }
    }

    static void RunBuild(string saveDotDir, string saveImageDir, string imageFormat)
    {
        Console.Clear();
        Console.WriteLine("Способ ввода:");
        Console.WriteLine("  1. Из файла");
        Console.WriteLine("  2. С клавиатуры (завершить Ctrl+Enter)");
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

        Directory.CreateDirectory(saveDotDir);
        Directory.CreateDirectory(saveImageDir);

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        if (methods.Count == 0)
        {
            Console.WriteLine("Методы не найдены.");
            Pause();
            return;
        }

        foreach (var method in methods)
        {
            var builder = new CFGBuilder();
            var cfg = builder.Build(method);

            string name = method.Identifier.Text;
            Console.WriteLine($"=== {name} ===");

            string dotPath = Path.Combine(saveDotDir, name + ".dot");
            string imgPath = Path.Combine(saveImageDir, name + "." + imageFormat);

            DotExporter.SaveDot(cfg, dotPath, name);
            Console.WriteLine($"  dot: {dotPath}");

            if (DotExporter.Render(dotPath, imgPath, imageFormat))
                Console.WriteLine($"  img: {imgPath}");
            else
                Console.WriteLine("  img: не удалось (проверьте, что dot в PATH)");
        }

        Console.WriteLine();
        Console.WriteLine("Готово.");
        Pause();
    }
    
    static string ReadMultiLine()
    {
        var sb = new StringBuilder();

        Console.WriteLine("Введите код.");
        Console.WriteLine("  Завершение ввода: Ctrl+D (Linux/macOS) или Ctrl+Z + Enter (Windows)");
        Console.WriteLine(new string('-', 60));

        while (true)
        {
            string? line = Console.ReadLine();

            // null = EOF → Ctrl+D на Linux/macOS, Ctrl+Z на Windows
            if (line == null)
                break;

            sb.AppendLine(line);
        }

        Console.WriteLine(new string('-', 60));
        return sb.ToString();
    }

    // static string ReadUntilCtrlEnter()
    // {
    //     var sb = new StringBuilder();
    //
    //     while (true)
    //     {
    //         var key = Console.ReadKey(intercept: true);
    //         
    //         if (key.Key == ConsoleKey.Enter && (key.Modifiers & ConsoleModifiers.Control) != 0)
    //         {
    //             Console.WriteLine();
    //             break;
    //         }
    //         
    //         if (key.Key == ConsoleKey.Enter)
    //         {
    //             sb.Append('\n');
    //             Console.WriteLine();
    //             continue;
    //         }
    //
    //         if (key.Key == ConsoleKey.Backspace)
    //         {
    //             if (sb.Length > 0 && sb[^1] != '\n')
    //             {
    //                 sb.Length--;
    //                 Console.Write("\b \b");
    //             }
    //             continue;
    //         }
    //
    //         
    //         if (char.IsControl(key.KeyChar) && key.KeyChar != '\t')
    //             continue;
    //
    //         sb.Append(key.KeyChar);
    //         Console.Write(key.KeyChar);
    //     }
    //
    //     return sb.ToString();
    // }

    static void Pause()
    {
        Console.WriteLine("Нажмите любую клавишу...");
        Console.ReadKey(true);
    }
}
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lab1;

class Program
{
    static void Main()
    {
        string code = """
                      ﻿using System;
                      using System.Runtime.InteropServices;
                      
                      namespace task
                      {
                          class Programm
                          {
                              static void Main(string[] args)
                              {
                                  int n = Convert.ToInt32(Console.ReadLine());
                                  int[] fact = new int[n];
                                  for(int i=0; i<n; ++i)
                                  {
                                      if (i == 0)
                                      {
                                          fact[i] = 1;
                                      }
                                      else
                                      {
                                          fact[i] = fact[i - 1] * (i);
                                      }
                                  }
                                  for(int i = 0; i<n; ++i)
                                  {
                                      for(int j=0; j<=i; ++j)
                                      {
                                          Console.Write(fact[i] / (fact[j] * fact[i - j]) + " ");
                                      }
                                      Console.Write("\n");
                                  }
                              }
                          }
                      }
                      """;

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var builder = new CFGBuilder();
            var cfg = builder.Build(method);

            string name = method.Identifier.Text;
            Console.WriteLine($"=== {name} ===");
            Console.WriteLine(DotExporter.ToDot(cfg, name));

            DotExporter.Export(cfg, name, name, "png");
            Console.WriteLine();
        }
    }
}
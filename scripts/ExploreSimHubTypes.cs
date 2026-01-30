using System;
using System.Linq;
using System.Reflection;

namespace ExploreSimHub
{
    class Program
    {
        static void Main()
        {
            // Caminho das DLLs
            string simhubPath = @"C:\Program Files (x86)\SimHub\";
            
            // DLLs para explorar
            string[] dlls = {
                simhubPath + "SimHub.Plugins.dll",
                simhubPath + "SimHubWPF.exe"
            };
            
            Console.WriteLine("=== EXPLORANDO CONTROLOS DO SIMHUB ===\n");
            
            foreach (var dllPath in dlls)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dllPath);
                    Console.WriteLine($"\n📦 {System.IO.Path.GetFileName(dllPath)}");
                    Console.WriteLine("=" + new string('=', 50));
                    
                    // Procurar classes com "Control", "Input", "Button" no nome
                    var types = assembly.GetTypes()
                        .Where(t => t.IsPublic && 
                               (t.Name.Contains("Control") || 
                                t.Name.Contains("Input") || 
                                t.Name.Contains("Button") ||
                                t.Name.Contains("Action")))
                        .OrderBy(t => t.Name);
                    
                    foreach (var type in types)
                    {
                        Console.WriteLine($"  ✓ {type.FullName}");
                        
                        // Se for UserControl, mostrar propriedades
                        if (type.BaseType?.Name == "UserControl")
                        {
                            Console.WriteLine($"    → É um WPF UserControl!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Erro: {ex.Message}");
                }
            }
            
            Console.WriteLine("\n\nPressione qualquer tecla...");
            Console.ReadKey();
        }
    }
}

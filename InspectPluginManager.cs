using System;
using System.Linq;
using System.Reflection;
using SimHub.Plugins;

namespace InspectAPI
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== PluginManager PUBLIC METHODS ===\n");

            var type = typeof(PluginManager);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods.OrderBy(m => m.Name))
            {
                var parameters = method.GetParameters();
                var paramString = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Console.WriteLine($"{method.ReturnType.Name} {method.Name}({paramString})");
            }

            Console.WriteLine("\n\n=== Methods with 'Device' or 'Display' in name ===\n");

            var allMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var method in allMethods.Where(m => m.Name.Contains("Device") || m.Name.Contains("Display")).OrderBy(m => m.Name))
            {
                var parameters = method.GetParameters();
                var paramString = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));

                var visibility = method.IsPublic ? "PUBLIC" : (method.IsAssembly ? "INTERNAL" : "PRIVATE");
                Console.WriteLine($"[{visibility}] {method.ReturnType.Name} {method.Name}({paramString})");
            }

            Console.WriteLine("\n\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Text;

// Run only the installed game's pure string-processing helpers. Do not call
// InjectTryBlocks itself: Options' static initializer requires a live Unity game.
// This reproduces its enabled-injection branch without initializing Options,
// scenes, game settings, mod behaviours, or any native Unity function.
public static class NativeSourcePreprocessor
{
    private static string managed;
    private static Type controller;
    private static MethodInfo Helper(string name)
    {
        return controller.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
    }
    private static string Transform(string source)
    {
        // The mod does not use SWINC conditionals. Three header lines preserve
        // the line count of the game's version defines for error reproduction.
        string input = (string)Helper("StripComments").Invoke(null, new object[] { "\n\n\n" + source });
        StringBuilder output = new StringBuilder();
        int cursor = 0, previous = 0;
        while (cursor < input.Length)
        {
            object[] find = { input, cursor };
            bool found = (bool)Helper("FindNextClassOrStruct").Invoke(null, find);
            cursor = (int)find[1];
            if (!found) continue;
            object[] skip = { input, cursor, '{' };
            bool brace = (bool)Helper("SkipTo").Invoke(null, skip);
            cursor = (int)skip[1];
            if (!brace) continue;
            object[] parse = { input, cursor, previous, output, "AI Compute" };
            Helper("HandleObjectParsing").Invoke(null, parse);
            cursor = (int)parse[1]; previous = (int)parse[2];
        }
        output.Append(input.Substring(previous, cursor - previous));
        return output.ToString();
    }
    public static int Main(string[] args)
    {
        managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Assembly game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
        controller = game.GetType("ModController", true);
        Directory.CreateDirectory(args[2]);
        foreach (string path in Directory.GetFiles(args[1], "*.cs"))
            File.WriteAllText(Path.Combine(args[2], Path.GetFileName(path)), Transform(File.ReadAllText(path)));
        Console.WriteLine("Native source injection reproduced using installed string helpers; no Unity runtime executed.");
        return 0;
    }
}

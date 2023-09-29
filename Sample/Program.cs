using Lib;

Console.WriteLine("Hello World!");
Console.WriteLine(string.Join(Environment.NewLine, SampleInitializers.Strings));

// Guaranteed to only be called once
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(SampleInitializers).Module.ModuleHandle);

Console.WriteLine();

Console.WriteLine("After other module .ctor:");
Console.WriteLine(string.Join(Environment.NewLine, SampleInitializers.Strings));

class SampleInitializers
{
    public static List<string> Strings = new();

    private static int i = 0;

    [InitializeAtStartup]
    public static void CallMe() => Strings.Add($"Called! ({i++})");

    [InitializeAtStartup(-5)]
    public static void CallMeBefore() => Strings.Add($"Called before Called()! ({i++})");

    [InitializeAtStartup(2)]
    public static void CallMeAfter() => Strings.Add($"Called after Called()! ({i++})");

    [InitializeAtStartup(10)]
    public static void CallMeMuchAfter() => Strings.Add($"Called much later than Called()! ({i++})");

    [InitializeAtStartup(-10)]
    public static void CallMeMuchBefore() => Strings.Add($"Called way before Called()! ({i++})");
}

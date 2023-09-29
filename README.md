# InitializeAtStartup

Small sample implementing a **C# Source Generator** that allows the user to mark methods with the
`[InitializeAtStartup]` attribute, which is similar to the .NET `[ModuleInit]` attribute, but accepts
an `order` parameter.

The Source Generator will then generate an initialization method that is invoked on module load
(like the `[ModuleInit]`-annotated methods), and invokes each one of the methods in the order specified.
For example:

```cs
class SampleInitializers
{
    [InitializeAtStartup]
    public static void CallMe() => Console.WriteLine($"Called!");  /* Order 0 (default) */

    [InitializeAtStartup(-5)]
    public static void CallMeBefore() => Console.WriteLine($"Called before Called()!");

    [InitializeAtStartup(2)]
    public static void CallMeAfter() => Console.WriteLine($"Called after Called()!");

    [InitializeAtStartup(10)]
    public static void CallMeMuchAfter() => Console.WriteLine($"Called much later than Called()!");

    [InitializeAtStartup(-10)]
    public static void CallMeMuchBefore() => Console.WriteLine($"Called way before Called()!");
}
```

The result of this will be:

```
Called way before Called()!
Called before Called()!
Called!
Called after Called()!
Called much later than Called()!
```

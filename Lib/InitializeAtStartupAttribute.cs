namespace Lib;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InitializeAtStartupAttribute : Attribute
{
    public int Order { get; }

    public InitializeAtStartupAttribute(int order = 0) => Order = order;
}

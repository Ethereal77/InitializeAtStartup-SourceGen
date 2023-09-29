using System.Reflection;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Analyzer;

[Generator]
public class ModuleInitGenerator : ISourceGenerator
{
    // Names of the attribute to look for
    private const string InitializeAtStartup = nameof(InitializeAtStartup);
    private const string InitializeAtStartupAttribute = nameof(InitializeAtStartupAttribute);

    private const string InitializeAtStartupAttributeName = $"Lib.{InitializeAtStartupAttribute}";


    // Entry assembly is null in VS IDE.
    // At build time, it is `csc` or `VBCSCompiler`.
    private static readonly bool IsBuildTime = Assembly.GetEntryAssembly() is not null;


    public void Initialize(GeneratorInitializationContext context)
    {
        // Register only when running from VBCSCompiler / csc
        if (IsBuildTime)
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // Don't run if not in build-time
        if (!IsBuildTime)
            return;

        if (context.SyntaxReceiver is not SyntaxReceiver syntaxReceiver ||
            !syntaxReceiver.CandidateMethods.Any())
            return;

        // Get the symbol for InitializeAtStartupAttribute
        INamedTypeSymbol? moduleInitAttributeType = context.Compilation.GetTypeByMetadataName(InitializeAtStartupAttributeName);
        if (moduleInitAttributeType is null)
            // If it doesn't exist in the compilation, the generator has nothing to do
            return;

        // Associate each ModuleInit method with its order so we can order them
        var moduleInitMethods = new List<(int Order, MethodDeclarationSyntax Syntax)>();

        // Store a mapping between SyntaxTree and SemanticModel.
        //   SemanticModels cache results and since we could be looking at method declarations in the same SyntaxTree we
        //   want to benefit from this caching.
        var syntaxToModel = new Dictionary<SyntaxTree, SemanticModel>();

        foreach (SyntaxReference syntaxRef in syntaxReceiver.CandidateMethods)
        {
            var methodSyntax = (MethodDeclarationSyntax) syntaxRef.GetSyntax(context.CancellationToken);

            // Get the model for the method
            if (!syntaxToModel.TryGetValue(methodSyntax.SyntaxTree, out SemanticModel semanticModel))
            {
                semanticModel = context.Compilation.GetSemanticModel(methodSyntax.SyntaxTree, ignoreAccessibility: true);
                syntaxToModel.Add(methodSyntax.SyntaxTree, semanticModel);
            }

            // Process the method syntax and get its SymbolInfo
            var methodSymbolInfo = semanticModel.GetDeclaredSymbol(methodSyntax, context.CancellationToken)!;

            // Get the ModuleInit attribute on the method
            AttributeData? moduleInitAttribute = null;
            foreach (var attribute in methodSymbolInfo.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, moduleInitAttributeType))
                    moduleInitAttribute = attribute;
            }
            if (moduleInitAttribute is null)
                continue;

            // Process the ModuleInit attribute
            int moduleInitOrder = ProcessModuleInitAttribute(moduleInitAttribute);
            moduleInitMethods.Add((moduleInitOrder, methodSyntax));
        }

        // Order the methods by the initialization order
        moduleInitMethods.Sort(static (x, y) => x.Order.CompareTo(y.Order));

        var generatedModuleInits = new StringBuilder();
        PrintGeneratedSource(generatedModuleInits, context, moduleInitMethods);

        //PrintGeneratedSource(generatedModuleInits, methodSyntax, moduleInitOrder);
        context.AddSource("ModuleInitGenerator.g.cs", SourceText.From(generatedModuleInits.ToString(), Encoding.UTF8));

        //
        // Processes a candidate attribute and extracts the order value from its constructor.
        //
        static int ProcessModuleInitAttribute(AttributeData attributeData)
        {
            // Found the ModuleInit, but it has an error so report the error.
            //   This is most likely an issue with targeting an incorrect TFM.
            if (attributeData.AttributeClass?.TypeKind is null or TypeKind.Error)
            {
                // TODO: Report ModuleInit has an error - corrupt metadata?
                throw new InvalidProgramException();
            }

            // Get the initialization order from the ModuleInitAttribute attribute
            int order = (int) attributeData.ConstructorArguments[0].Value!;

            return order;
        }

        //
        // Composes the source text of the generated module initializer.
        //
        static void PrintGeneratedSource(StringBuilder builder, GeneratorExecutionContext context, IList<(int order, MethodDeclarationSyntax syntax)> moduleInitMethods)
        {
            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine();
            builder.AppendLine("internal static class Module");
            builder.AppendLine("{");
            builder.AppendLine("    [System.Runtime.CompilerServices.ModuleInitializerAttribute]");
            builder.AppendLine("    internal static void __Initialize()");
            builder.AppendLine("    {");
            for (int i = 0; i < moduleInitMethods.Count; i++)
            {
                (int order, MethodDeclarationSyntax methodSyntax) = moduleInitMethods[i];

                var semanticModel = context.Compilation.GetSemanticModel(methodSyntax.SyntaxTree);
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax, context.CancellationToken);

                if (methodSymbol is null)
                    continue;

                if (i != 0)
                    builder.AppendLine();

                builder.AppendLine($"        // Order: {order}");
                builder.AppendLine($"        {methodSymbol.ToDisplayString()};");
            }
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }
    }

    #region Syntax Receiver

    /// <summary>
    ///   Filters the syntax nodes in the compilation and composes a list of candidate ModuleInit methods.
    /// </summary>
    private class SyntaxReceiver : ISyntaxReceiver
    {
        public ICollection<SyntaxReference> CandidateMethods { get; } = new List<SyntaxReference>();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            // We only support C# method declarations
            if (syntaxNode.Language != LanguageNames.CSharp ||
                syntaxNode is not MethodDeclarationSyntax methodSyntax)
                return;

            // Verify the method is static and has no generic types
            if (methodSyntax.TypeParameterList is not null ||
                !methodSyntax.Modifiers.Any(SyntaxKind.StaticKeyword))
                return;

            // Check if the method is marked with the ModuleInit attribute
            foreach (AttributeListSyntax attributeListSyntax in methodSyntax.AttributeLists)
                foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
                {
                    if (IsModuleInitAttribute(attributeSyntax))
                    {
                        CandidateMethods.Add(syntaxNode.GetReference());
                        return;
                    }
                }

            //
            // Determines if an AttributeSyntax corresponds to a InitializeAtStartup attribute.
            //
            static bool IsModuleInitAttribute(AttributeSyntax attributeSyntax)
            {
                var attributeName = attributeSyntax.Name.ToString();

                if (attributeName.Length == InitializeAtStartup.Length)
                    return attributeName.Equals(InitializeAtStartup);
                else if (attributeName.Length == InitializeAtStartupAttribute.Length)
                    return attributeName.Equals(InitializeAtStartupAttribute);

                // Handle the case where the user defines an attribute with
                // the same name but adds a prefix
                const string PrefixedModuleInit = "." + InitializeAtStartup;
                const string PrefixedModuleInitAttribute = "." + InitializeAtStartupAttribute;
                return attributeName.EndsWith(PrefixedModuleInit) ||
                       attributeName.EndsWith(PrefixedModuleInitAttribute);
            }
        }
    }

    #endregion
}

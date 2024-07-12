using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MyGenerator2
{
    [Generator]
    public class IncGen : IIncrementalGenerator
    {
        public const string Attribute = @"
namespace Helper
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class ToGenerateAttribute : System.Attribute
    {
    }
}";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
            "Helper.g.cs",
            SourceText.From(Attribute, Encoding.UTF8)));

            var s = context.SyntaxProvider;
            var pipeline = s.ForAttributeWithMetadataName("Helper.ToGenerateAttribute", 
                static (node, conc) => true, 
                static (transform, conc) => 
                {
                    var semanticModel = transform.SemanticModel;
                    var parentNode = transform.TargetNode;

                    var parent = semanticModel.GetDeclaredSymbol(parentNode);
                    var parentSymbol = semanticModel.GetDeclaredSymbol(parentNode) as INamedTypeSymbol;
                    var members = parentSymbol.GetMembers().Where(m => m.Kind is SymbolKind.Field).Select(m => m as IFieldSymbol);
                    var fields = members.Select(m => new FieldModel(m.Name, "P" + m.Name, m.Type.Name)).ToList();

                    return new Model(parent.ContainingNamespace.Name, parent.Name, fields);
                });

            context.RegisterSourceOutput(pipeline, static (context, model) =>
            {

                string props = "";
                foreach(var field in model.fields)
                {
                    props += $"public {field.Type} {field.nameUp} {{ get; set; }}\n";
                }

                string source = $$"""
                namespace {{model.namespaceName}}
                {
                    public partial class {{model.className}}
                    {
                        {{props}}
                    }
                }
                """;
                context.AddSource($"{model.className}.g.cs", source);
            });
        }

        private record struct FieldModel(string nameLow, string nameUp, string Type);
        private record struct Model(string namespaceName, string className, List<FieldModel> fields);
    }
}

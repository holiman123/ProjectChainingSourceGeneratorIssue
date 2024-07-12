This is minimal reproduction for bug report #74368 to dotnet/roslyn:

- dotnet/roslyn: https://github.com/dotnet/roslyn, 
- Issue: https://github.com/dotnet/roslyn/issues/74368

**Version Used**: 

- Visual Studio 2022: 17.10.4
- Microsoft.CodeAnalysis.CSharp: 4.10.0
- Microsoft.CodeAnalysis.Analyzers: 3.3.4 

**Steps to Reproduce**:

1. Create solution with 3 projects: source generator, class library, console app.
![image](https://github.com/user-attachments/assets/e4aca388-1130-48eb-9bc0-70096584e91f)

- GeneratorUser - Class library that utilize MyGenerator2.
- MyGenerator2 - Incremental source generator. (For this example that finds all classes with specific attribute and adds properties to all fields in it. Specific attribute generates in the same generator)
- ProjChainingSourceGeneratorTest - Simple console app that utilize GeneratorUser.

2. Define MyGenerator2 as source generator:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>11.0</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.10.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>

</Project>

```

3. Define references: 

**Reference chain:** MyGenerator2 > GeneratorUser > ProjChainingSourceGeneratorTest 

- GeneratorUser:
  ```xml
  <ItemGroup>
    <ProjectReference Include="..\MyGenerator2\MyGenerator2.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false"/>
  </ItemGroup>
   ```
- ProjChainingSourceGeneratorTest:
  ```xml
  <ItemGroup>
    <ProjectReference Include="..\GeneratorUser\GeneratorUser.csproj"/>
  </ItemGroup>
  ```

4. Define generator:  
(For this example that finds all classes with specific attribute and adds properties to all fields in it. Specific attribute generates in the same generator)
 ```c#
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
 ```

5. Utilize MyGenerator2 in GeneratorUser:
```c#
using Helper;

namespace GeneratorUser;

[ToGenerate]
public partial class Class1
{
    private int foo;

    public Class1(int pfoo)
    {
        Pfoo = pfoo;
    }

    public Class1()
    {}
}
```

6. Create instance of Class1 in console app. And find out that generated property is not showing in Intellisense autocomplete:
![image](https://github.com/user-attachments/assets/a3f5b7a7-a003-4a70-a119-7c4e2e03138b)

**A minimal repro:** https://github.com/holiman123/ProjectChainingSourceGeneratorIssue

**Expected Behavior**:
Show code autocomplete correctly.

**Actual Behavior**:
Doesn't show autocomplete. 

**Annotation**:
Build works as expected and generated files are used, if write generated property manualy. 
There are no errors in error list and all genereted properties do not underscored with red curly line.
Go to definition (F12) works fine. It shows right generated file.

It is possible to call right autocomplete:

1. Write name of instance
2. Write dot (at this point wrong autocomplete window appears)
3. Press "Esc" to close autocomplete window
4. Press "Ctrl" + "Space" to call autocomplete window back (that appears to be right)

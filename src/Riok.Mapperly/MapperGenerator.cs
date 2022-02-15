﻿using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Riok.Mapperly.Abstractions;
using Riok.Mapperly.Descriptors;
using Riok.Mapperly.Emit;
using Riok.Mapperly.Helpers;

namespace Riok.Mapperly;

[Generator]
public class MapperGenerator : IIncrementalGenerator
{
    private static readonly string _mapperAttributeName = typeof(MapperAttribute).FullName;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var mapperClassDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (s, _) => IsSyntaxTargetForGeneration(s),
                static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .WhereNotNull();

        var compilationAndMappers = context.CompilationProvider.Combine(mapperClassDeclarations.Collect());
        context.RegisterSourceOutput(compilationAndMappers, static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
        => node
            is InterfaceDeclarationSyntax { AttributeLists.Count: > 0 }
            or ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    private static SyntaxNode? GetSemanticTargetForGeneration(GeneratorSyntaxContext ctx)
    {
        var attributeList = ctx.Node is InterfaceDeclarationSyntax intfDecl
            ? intfDecl.AttributeLists
            : ((ClassDeclarationSyntax)ctx.Node).AttributeLists;

        foreach (var attributeListSyntax in attributeList)
        {
            foreach (var attributeSyntax in attributeListSyntax.Attributes)
            {
                if (ctx.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                    continue;

                var attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                var fullName = attributeContainingTypeSymbol.ToDisplayString();
                if (fullName == _mapperAttributeName)
                    return ctx.Node;
            }
        }

        return null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<SyntaxNode> mappers, SourceProductionContext ctx)
    {
        if (mappers.IsDefaultOrEmpty)
            return;

        DebuggerUtil.AttachDebugger();

        var mapperAttributeSymbol = compilation.GetTypeByMetadataName(_mapperAttributeName);
        if (mapperAttributeSymbol == null)
            return;

        foreach (var mapperSyntax in mappers.Distinct())
        {
            var mapperModel = compilation.GetSemanticModel(mapperSyntax.SyntaxTree);
            if (mapperModel.GetDeclaredSymbol(mapperSyntax) is not ITypeSymbol mapperSymbol)
                continue;

            if (!mapperSymbol.HasAttribute(mapperAttributeSymbol))
                continue;

            var builder = new DescriptorBuilder(ctx, compilation, mapperSyntax, mapperSymbol);
            var descriptor = builder.Build();

            ctx.AddSource(
                descriptor.FileName,
                SourceText.From(SourceEmitter.Build(descriptor), Encoding.UTF8));
        }
    }
}
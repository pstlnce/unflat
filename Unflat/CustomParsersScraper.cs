using Unflat.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Unflat;

internal static class CustomParsersScraper
{
    public static IncrementalValuesProvider<CustomParserMethod?> ForCustomParsers(this SyntaxValueProvider syntaxProvider)
    {
        return syntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: CustomParserAttribute.FullName,
            predicate: (SyntaxNode _, CancellationToken token) => !token.IsCancellationRequested,
            transform: TransformCustomParser
        );
    }

    private static CustomParserMethod? TransformCustomParser(GeneratorAttributeSyntaxContext context, CancellationToken token)
    {
        var semantic = context.SemanticModel.GetDeclaredSymbol(context.TargetNode);
        
        if(semantic is not IMethodSymbol methodSemantic)
        {
            return default;
        }

        var namedArgs    = context.Attributes[0].NamedArguments;
        var isDefaultArg = namedArgs.Find(CustomParserAttribute.IsDefault);

        var location     = context.TargetNode.GetLocation();
        var callFormat   = namedArgs.Find(CustomParserAttribute.CallFormat) is { Value.Value: string format } ? format : null;
        var isDefault    = isDefaultArg is { Value.Value: true };
        var namespaceArg = namedArgs.Find(CustomParserAttribute.Namespace) is { Value.Value: string namespaceInArg } ? namespaceInArg : null;
        var @namespace   = namespaceArg ?? methodSemantic.ContainingNamespace.ToDisplayString();
        var isGlobal     = methodSemantic.ContainingNamespace.IsGlobalNamespace;
        var path         = methodSemantic.ContainingSymbol.ToDisplayString();
        var returnType   = methodSemantic.ReturnType.ToDisplayString();
        var methodName   = methodSemantic.Name;
        var namespaces   = namespaceArg?.Split('.')?.AsMemory();

        namespaces ??= methodSemantic.ContainingNamespace.ConstituentNamespaces.ExtractNames();

        if(namespaceArg?.AsSpan().Trim().IsEmpty == true)
        {
            isGlobal = true;
        }

        if(isGlobal && isDefaultArg is null)
        {
            isDefault = true;
        }

        var transformed = new CustomParserMethod()
        {
            Namespaces = namespaces.Value,
            Namespace = @namespace,
            Location = location,
            CallFormat = callFormat,
            IsDefault = isDefault,
            IsInGlobalNamespace = isGlobal,
            Path = path,
            Name = methodName,
            ReturningType = returnType,
        };

        return transformed;
    }

    public static CustomParsersMap ToParsers(this ImmutableArray<CustomParserMethod?> parserMethods, SourceProductionContext productionContext)
    {
        var parsersCollisions = new Dictionary<(string @namespace, string type), (int index, CustomParserMethod instance)>(parserMethods.Length);

        var parsers = new CustomParserMethod[parserMethods.Length];
        var parsersCount = 0;

        foreach (var parserMethodNullable in parserMethods)
        {
            if (parserMethodNullable is null) continue;

            var parserMethod = parserMethodNullable.Value;

            if(parsersCollisions.TryGetValue((parserMethod.Namespace, parserMethod.ReturningType), out var instance))
            {
                var (index, collision) = instance;

                productionContext.ReportDiagnostic(
                    Diagnostic.Create(
                        location: parserMethod.Location,
                        messageArgs: default,
                        descriptor: new DiagnosticDescriptor(
                            id: "lowiq001",
                            title: "Parsers collision",
                            messageFormat: string.Empty,
                            category: "custom parsing",
                            defaultSeverity: DiagnosticSeverity.Warning,
                            description: $"The same returning type and the same namespace as {collision.Path}",
                            isEnabledByDefault: true
                        )
                    )
                );

                if (!collision.IsDefault && parserMethod.IsDefault)
                {
                    parsersCollisions[(parserMethod.Namespace, parserMethod.ReturningType)] = (index, parserMethod);
                    parsers[index] = parserMethod;
                }

                continue;
            }
            
            parsersCollisions[(parserMethod.Namespace, parserMethod.ReturningType)] = (parsersCount, parserMethod);
            parsers[parsersCount] = parserMethod;
            parsersCount += 1;
        }

        var parserSlice = parsers.AsMemory(0, parsersCount);

        var defaults = new Dictionary<string, CustomParserMethod>(parsersCount);

        foreach (var parser in parserSlice.Span)
        {
            if (!parser.IsDefault) continue;

            if(defaults.TryGetValue(parser.ReturningType, out var parserMethod))
            {
                productionContext.ReportDiagnostic(
                    Diagnostic.Create(
                        location: parserMethod.Location,
                        messageArgs: default,
                        descriptor: new DiagnosticDescriptor(
                            id: "lowiq002",
                            title: "Default Parsers Collision",
                            messageFormat: string.Empty,
                            category: "custom parsing",
                            defaultSeverity: DiagnosticSeverity.Warning,
                            description: $"The same returning type as {parserMethod.Path}",
                            isEnabledByDefault: true
                        )
                    )
                );

                continue;
            }

            defaults[parser.ReturningType] = parser;
        }

        var notDefaults = new Dictionary<string, List<CustomParserMethod>>(parsersCount);

        foreach (var parser in parserSlice.Span)
        {
            if (parser.IsDefault) continue;
            
            if(!notDefaults.TryGetValue(parser.ReturningType, out var group))
            {
                notDefaults[parser.ReturningType] = group = [];
            }

            group.Add(parser);
        }

        var parsersMap = new Dictionary<string, Memory<CustomParserMethod>>(parsersCount);

        foreach (var item in notDefaults)
        {
            var (returnType, group) = (item.Key, item.Value);
            parsersMap[returnType] = group.AsMemory();
        }

        var result = new CustomParsersMap()
        {
            Parsers = parsers,
            DefaultParsers = defaults,
            ParsersMap = parsersMap,
        };

        return result;
    }
}

public struct CustomParsersMap
{
    public Memory<CustomParserMethod> Parsers;
    public Dictionary<string, CustomParserMethod> DefaultParsers;
    public Dictionary<string, Memory<CustomParserMethod>> ParsersMap;
}

public struct CustomParserMethod
{
    public Memory<string> Namespaces;
    public Location Location;
    public string Namespace;
    public string? CallFormat;
    public string Path;
    public string Name;
    public string ReturningType;
    public bool IsDefault;
    public bool IsInGlobalNamespace;
}
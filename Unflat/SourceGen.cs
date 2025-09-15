using Unflat.Options;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

namespace Unflat;

[Generator(LanguageNames.CSharp)]
internal sealed class SourceGen : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if false && DEBUG
        Debugger.Launch();
#endif

        context.RegisterPostInitializationOutput(postInitContext =>
            postInitContext.AddSource($"{UnflatMarkerAttributeGenerator.AttributeFullName}.g.cs", UnflatMarkerAttributeGenerator.MarkerAttributeSourceCode)
        );

        var customParsers  = context.SyntaxProvider.ForCustomParsers();
        var dbParseTargets = context.SyntaxProvider.ForParseTargets();

        var customParsersCollected = customParsers.Collect();
        var dbParseCollected       = dbParseTargets.Collect();

        var collectionEpilogue = customParsersCollected.Combine(dbParseCollected);

        context.RegisterSourceOutput(collectionEpilogue, (context, items) =>
        {
            var parsers = items.Left.ToParsers(context);

            DifferentWay.GenerateDataReaderParsers(context, items.Right, parsers);
        });
    }
}

internal readonly struct UnflatMarkerAttributeParse(AttributeData source)
{
    private readonly AttributeData _source = source;

    public int? MatchCasePropertyValue => FindNamedArg(UnflatMarkerAttributeGenerator.MatchCaseProperty) switch
    {
        TypedConstant caseValue
            when caseValue.Kind != TypedConstantKind.Error => caseValue.Value as int?,

        TypedConstant => null,
        _ => UnflatMarkerAttributeGenerator.DefaultCase.value
    };

    public string? ClassName => FindNamedArg(UnflatMarkerAttributeGenerator.ClassNameProperty) switch
    {
        TypedConstant className
            when className.Kind != TypedConstantKind.Error => className.Value as string,

        _ => default
    };

    private TypedConstant? FindNamedArg(string parameter)
    {
        for (int i = 0; i != _source.NamedArguments.Length; i++)
        {
            var argument = _source.NamedArguments[i];

            if(argument.Key == parameter)
            {
                return argument.Value;
            }
        }

        return default;
    }
}

internal static class FieldSourceAttributeParse
{
    public static FieldsOrOrder? ParseToFieldSource(this AttributeData? source)
    {
        if(source is null)
        {
            return default;
        }

        var arguments = source.ConstructorArguments;

        if (arguments.Length != 1)
            return default;

        var argument = source.ConstructorArguments[0];

        var type = argument.Type?.ToDisplayString();

        return type switch
        {
            "int" when argument.Value is int fieldOrder => new(fieldOrder),
            
            "string[]" when argument.Values
                .Where(x => !x.IsNull)
                .Select(x => (string)x.Value!)
                .ToList() is { Count: > 0 } fields => new(fields),

            _ => default
        };
    }
}

internal readonly struct MatchingModel
{
    public readonly ImmutableArray<Settable> RequiredSettables;
    public readonly ImmutableArray<Settable> UsualSettables;
    public readonly ImmutableArray<Settable> Settables;

    public readonly TypeSnapshot Type;
    public readonly MatchingSettings MatchingSettings;

    public readonly Dictionary<string, MatchingModel>? Inner = default;

    public MatchingModel(TypeSnapshot type, Settable[] settables, MatchingSettings matchingSettings, Dictionary<string, MatchingModel>? inner = null)
    {
        Type = type;
        MatchingSettings = matchingSettings;

        Array.Sort(settables, _comparer);

        var requiredCount = settables.TakeWhile(x => x.Required).Count();

        var requiredSettables = ImmutableArray.Create(settables.AsSpan(0, requiredCount));
        var otherSettables = ImmutableArray.Create(settables.AsSpan(requiredCount));

        Settables = ImmutableArray.Create(settables);
        RequiredSettables = requiredSettables;
        UsualSettables = otherSettables;
        Inner = inner;
    }

    private static readonly Comparer<Settable> _comparer = Comparer<Settable>.Create(static (x, y) => (x, y) switch
    {
        ({ Required: true }, { Required: false }) => -1,
        ({ Required: false }, { Required: true }) => 1,
        _ => y.DeclarationOrder - x.DeclarationOrder
    });
}
internal readonly struct ParserStaticMethod
{
    public readonly string CallId;
    public readonly string TargetType;

    public ParserStaticMethod(string callId, string targetType)
        => (CallId, TargetType) = (callId, targetType);
}


internal readonly struct Settable
{
    public readonly TypeSnapshot Type;
    public readonly FieldsOrOrder FieldSource;
    public readonly string Name;
    public readonly string? CustomParseFormat;
    public readonly string ColumnPrefix;
    public readonly int DeclarationOrder;
    public readonly bool Required;
    public readonly bool SetToDefault;
    public readonly bool SettedPerSettableParser;

    public bool IsPrimitive => Type.IsPrimitive;

    public Settable(TypeSnapshot type, string name, FieldsOrOrder fieldSource, bool required, bool setToDefault, int declarationOrder, string? customParseFormat, bool settedPerSettableParser, string columnPrefix)
        => (Name, Type, FieldSource, DeclarationOrder, Required, SetToDefault, CustomParseFormat, SettedPerSettableParser, ColumnPrefix)
        = (name, type, fieldSource, declarationOrder, required, setToDefault, customParseFormat, settedPerSettableParser, columnPrefix);
}

internal readonly record struct TypeSnapshot(string Name, string DisplayString, bool IsReference, bool IsPrimitive, NamespaceSnapshot Namespace);

internal readonly struct NamespaceSnapshot
{
    public readonly Memory<string> Namespaces;
    public readonly string Name;
    public readonly string DisplayString;
    public readonly bool IsGlobal;

    public NamespaceSnapshot(string name, string display, bool isGlobal, Memory<string> namespaces)
        => (Name, DisplayString, IsGlobal, Namespaces) = (name, display, isGlobal, namespaces);
}

internal readonly struct GenerationSettings
{
    public readonly string ClassName;
}

internal readonly struct MatchingSettings
{
    public readonly MatchCase MatchCase;

    public MatchingSettings(MatchCase matchCase)
        => (MatchCase) = (matchCase);
}

internal readonly struct FieldsOrOrder
{
    private const int FIELDS = 1;
    private const int ORDER = 2;

    private readonly IEnumerable<string> _fields = default!;
    private readonly int _order;
    private readonly int _state;

    public FieldsOrOrder(IEnumerable<string> fields)
    {
        _fields = fields;
        _state = FIELDS;
    }

    public FieldsOrOrder(int order)
    {
        _order = order;
        _state = ORDER;
    }

    public bool IsFields => _state == FIELDS;
    public bool IsOrder => _state == ORDER;

    public IEnumerable<string> Fields => _state switch
    {
        FIELDS => _fields,
        _ => throw new StateMismatchinException(FIELDS, _state)
    };

    public int Order => _state switch
    {
        ORDER => _order,
        _ => throw new StateMismatchinException(ORDER, _state)
    };

    public bool TryGetFields([NotNullWhen(true)] out IEnumerable<string> fiels)
    {
        var (match, result) = _state switch
        {
            FIELDS => (true, _fields),
            ORDER => (false, null!),
            _ => throw new Exception($"Invalid state: {_state}, allowed only: {Order} or {Fields}"),
        };

        fiels = result;

        return match;
    }

    public bool TryGetOrder([NotNullWhen(true)] out int order)
    {
        var (match, result) = _state switch
        {
            FIELDS => (false, default),
            ORDER => (true, _order),
            _ => throw new Exception($"Invalid state: {_state}, allowed only: {Order} or {Fields}"),
        };

        order = result;

        return match;
    }

    public static implicit operator FieldsOrOrder(string[] fields) => new(fields);
    public static implicit operator FieldsOrOrder(List<string> fields) => new(fields);
    public static implicit operator FieldsOrOrder(ImmutableArray<string> fields) => new(fields);

    public static implicit operator FieldsOrOrder(int order) => new(order);
}

[Serializable]
public class StateMismatchinException(int expected, int actual)
    : Exception($"The union state expected to be {expected} but in fact {actual}")
{
}

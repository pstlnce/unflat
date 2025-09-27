using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Unflat;

internal static class TypeSymbolExtensions
{
    public static bool IsComplex(this ITypeSymbol typeSymbol) => !IsPrimitive(typeSymbol);

    public static ITypeSymbol EraseNullable(this ITypeSymbol typeSymbol)
    {
        var specialType = typeSymbol.OriginalDefinition.SpecialType;
        if (specialType == SpecialType.System_Nullable_T)
        {
            return ((INamedTypeSymbol)typeSymbol).TypeArguments[0];
        }

        return typeSymbol;
    }

    public static bool IsPrimitive(this ITypeSymbol typeSymbol)
    {
        typeSymbol = EraseNullable(typeSymbol);

        var specialSimple = typeSymbol.SpecialType
            is SpecialType.System_Object
            or SpecialType.System_Boolean
            or SpecialType.System_Char
            or SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Decimal
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_DateTime
            or SpecialType.System_String;

        if (specialSimple) return true;

        return typeSymbol.ContainingNamespace.Name == nameof(System) &&
            typeSymbol.Name is nameof(DateTimeOffset)
            or nameof(TimeSpan)
            or "DateOnly"
            or "TimeOnly";
    }

    public static Memory<string> ExtractNames(this ImmutableArray<INamespaceSymbol> namespaces)
    {
        var namespacesArray = namespaces.Length > 0
            ? new string[namespaces.Length]
            : [];

        for (var i = 0; i < namespaces.Length; i++)
            namespacesArray[i] = namespaces[i].Name;

        return namespacesArray;
    }

    public static KeyValuePair<string, TypedConstant>? Find(this ImmutableArray<KeyValuePair<string, TypedConstant>> arguments, string target)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].Key == target)
                return arguments[i];
        }

        return null;
    }
}

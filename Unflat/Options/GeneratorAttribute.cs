﻿using System;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

namespace Unflat.Options;

internal static class UnflatMarkerAttributeGenerator
{
    public const string AttributeName = "UnflatMarkerAttribute";
    public const string Namespace = "Unflat";
    public const string AttributeFullName = $"{Namespace}.{AttributeName}";

    public const string ClassNameProperty = "ClassName";
    public const string MatchCaseProperty = "Case";

    public static readonly (string name, int value) DefaultCase = (MatchCase.All.ToString(), (int)MatchCase.All);

    public static readonly string MarkerAttributeSourceCode =
@$"using System;

namespace {Namespace}
{{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class {AttributeName} : Attribute
    {{
        public string? {ClassNameProperty} {{ get; set; }}
        public {MatchCaseGenerator.EnumName} {MatchCaseProperty} {{ get; set; }} = {MatchCaseGenerator.EnumName}.{DefaultCase.name};
    }}

    {FieldSourceAttrubteGenerator.SourceCode}

    {MatchCaseGenerator.MatchCaseEnum}

    {NotEnoughReaderFieldsException.Source}

    {MissingRequiredFieldOrPropertyException.Source}

    {CustomParserAttribute.Source}
}}";
}

internal sealed class FieldSourceAttrubteGenerator
{
    public const string AttributeName = "FieldSourceAttribute";

    public static readonly string SourceCode =
$@"[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class {AttributeName} : Attribute
    {{
        public {AttributeName}(params string[] fields) {{}}
        public {AttributeName}(int fieldOrder) {{}}
    }}";
}

[Flags]
internal enum MatchCase : int
{
    None = 0,
    IgnoreCase = 1,
    MatchOriginal = 1 << 1,
    SnakeCase = 1 << 2,
    CamalCase = 1 << 3,
    PascalCase = 1 << 4,
    ApplyOnOverritenName = 1 << 5,
    All = IgnoreCase | MatchOriginal | SnakeCase | CamalCase | PascalCase | ApplyOnOverritenName,
}

internal static class MatchCaseGenerator
{
    public const string EnumName = nameof(MatchCase);
    public const string Namespace = UnflatMarkerAttributeGenerator.Namespace;

    public static readonly Regex _snake = new("([a-z])([A-Z])", RegexOptions.Compiled);

    public static MatchCase[] CaseSettings = Enum.GetValues(typeof(MatchCase))
        .Cast<MatchCase>()
        .Where(x => x > MatchCase.None && x < MatchCase.All)
        .OrderBy(x => x)
        .ToArray();

    public static readonly string MatchCaseEnum =
@$"[Flags]
    public enum {nameof(MatchCase)} : int
    {{
        None = 0,
{string.Join(
    "\n",
    CaseSettings.Select(x => (int)x == 1
        ? $"        {x} = 1,"
        : $"        {x} = 1 << {(int)Math.Log((int)x, 2)},"
    ).Concat([
        $"        {MatchCase.All} = {string.Join(" | ", CaseSettings.Select(x => x.ToString()))}"
    ])
)}
    }}";

    public static IEnumerable<string> ToAllCasesForCompare(this MatchCase cases, string value)
    {
        if (cases.Has(MatchCase.IgnoreCase))
        {
            if (cases.Has(MatchCase.PascalCase, MatchCase.CamalCase, MatchCase.MatchOriginal))
            {
                yield return value.ToLower();
            }

            yield break;
        }

        if (cases.Has(MatchCase.MatchOriginal))
            yield return value;

        if (cases.Has(MatchCase.SnakeCase))
            yield return ToSnakeCase(value);

        if (cases.Has(MatchCase.PascalCase))
            yield return ToPascalCase(value);

        if (cases.Has(MatchCase.CamalCase))
            yield return ToCamelCase(value);
    }

    public static bool Has(this MatchCase cases, MatchCase flag)
        => (cases & flag) != 0;

    public static bool Has(this MatchCase cases, MatchCase flag1, MatchCase flag2)
        => cases.Has(flag1 | flag2);

    public static bool Has(this MatchCase cases, MatchCase flag1, MatchCase flag2, MatchCase flag3)
        => cases.Has(flag1 | flag2 | flag3);


    public static string ToSnakeCase(string value)
    {
        var snakeCase = _snake.Replace(value, "$1_$2").ToLower();
        return snakeCase;
    }

    public static string ToPascalCase(string value)
    {
        var length = 0;

        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                length += 1;
            }
        }

        Span<char> span = length <= 1024 ? stackalloc char[length] : new char[length];

        int writeIndex = 0;
        bool newWord = true;

        for (int i = 0; i < value.Length; i++)
        {
            var symbol = value[i];

            if (!char.IsLetterOrDigit(symbol))
            {
                newWord = true;
                continue;
            }

            if (newWord)
            {
                symbol = char.ToUpper(symbol);
                newWord = false;
            }
            else
            {
                symbol = char.ToLower(symbol);
            }

            span[writeIndex++] = symbol;
        }

        var word = span.ToString();

        return word;
    }

    public static string ToCamelCase(string value)
    {
        var length = 0;

        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                length += 1;
            }
        }

        Span<char> span = length <= 1024 ? stackalloc char[length] : new char[length];

        int writeIndex = 0;
        bool newWord = false;

        for (int i = 0; i < value.Length; i++)
        {
            var symbol = value[i];

            if (!char.IsLetterOrDigit(symbol))
            {
                newWord = true;
                continue;
            }

            if (newWord)
            {
                symbol = char.ToUpper(symbol);
                newWord = false;
            }
            else
            {
                symbol = char.ToLower(symbol);
            }

            span[writeIndex++] = symbol;
        }

        var word = span.ToString();

        return word;
    }
}

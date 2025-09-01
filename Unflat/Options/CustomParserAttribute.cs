﻿namespace Unflat.Options;

internal static class CustomParserAttribute
{
    public const string FullName = Namespace + "." + Name;

    public const string Namespace = UnflatMarkerAttributeGenerator.Namespace;
    public const string Name = "ParserAttribute";

    public const string Source =
    @$"[AttributeUsage(AttributeTargets.Method)]
    internal sealed class {Name} : Attribute
    {{ }}";
}

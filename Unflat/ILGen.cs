#if false

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unflat.Options;

namespace Unflat;

internal static class ILGen
{
    enum IlOperation
    {
        None = 0,

        Pop,
        Duplicate, // Dup

        Call,
        //CallForHasValue,
        Callvirt,
        Calli,

        CallVarArgs,

        Newobj,
        Initobj,
        NewArray,

        SetLoc,
        SetArrayElement,

        LoadLoc,
        LoadArg,
        LoadStr,
        LoadInt32,
        LoadInt64,
        LoadArrayElement,
        LoadNull,
        LoadField, // Ldfld
        LoadFieldStatic,
        LoadChar,

        Goto,         // br
        GotoIf_True,  // brtrue
        GotoIf_False, // brfalse
        GotoIf_TheFirstValue_IsLess_Than_OrEqualTo_TheSecondOne,          // Ble
        GotoIf_TheFirstValue_IsLess_Than_OrEqualTo_TheSecondOne_Unsigned, // Ble_Un
        GotoIf_TheFirstValue_IsLess_Than_TheSecond,          // Blt
        GotoIf_TheFirstValue_IsLess_Than_TheSecond_Unsigned, // Blt_Un
        GotoIf_Equals, // Beq
        GotoIf_TheFirstValue_IsGreater_Than_OrEqualTo_TheSecondOne,          // Bge
        GotoIf_TheFirstValue_IsGreater_Than_OrEqualTo_TheSecondOne_Unsigned, // Bge_Un
        GotoIf_TheFirstValue_IsGreater_Than_TheSecondOne,          // Bgt
        GotoIf_TheFirstValue_IsGreater_Than_TheSecondOne_Unsigned, // Bgt_Un

        Label_Define,
        Label_Mark,

        Box,
        UnboxAny,

        Add,      // Add
        Add_Int,  // Add_Ovf
        Add_UInt, // Add_Ovf_Un

        Sub,

        Mul,      // Mul
        Mul_Int,  // Mul_Ovf
        Mul_UInt, // Mul_Ovf_Un

        Div,         // Div
        DivUnsigned, // Div_Un

        Mod,

        Bit_AND, // And
        Bit_OR,  // Or
        Bit_XOR, // Xor

        Equals, // Ceq
        NotEquals,

        GreaterThan,          // Cgt
        GreaterThan_Unsigned, // Cgt_Un
        LessThan,             // Clt
        LessThan_Unsigned,    // Clt_Un

        Shift_Left,
        Shift_Right,

        Return, // Ret

        Unsupported = 999, // Fire warning
    }

    class LocalVariable
    {
        public Type Type;
        public LocalBuilder Builder;
    }

    struct LocalVarId
    {
        public string Name;
        public int Depth;
        public int Order;
    }

    sealed class LabelLink
    {
        public Label? Label;
    }

    struct IlInstruction
    {
        public VariableLink? VariableLink;

        public Type? Type;

        public ConstructorInfo? CtorInfo;

        public MethodInfo? CallingMethod;
        public FieldInfo? FieldInfo;

        public string? LoadString;
        public long? LoadInt;
        public char? LoadChar;

        public LabelLink? LabelLink;


        public IlOperation Code;
    }

    public const string SYSTEM = nameof(System);
    public const string SYSTEM_TEXT = nameof(System) + "." + nameof(System.Text);

    public struct SharedTypes
    {
        public Dictionary<string, Type> TypeByName;
        public Dictionary<Type, (ConstructorInfo ctor, ParameterInfo[] parameters)[]> Ctors;
        public Dictionary<Type, (MethodInfo method, ParameterInfo[] parameters)[]> Methods;
        public Dictionary<Type, FieldInfo[]> Fields;
        public Dictionary<Type, PropertyInfo[]> Properties;
    }

    public static Dictionary<string, TypeSlice> _sharedTypes = Init();

    public static bool IsAllowedNamespace(string? value)
    {
        return value is SYSTEM or SYSTEM_TEXT;
    }

    public struct TypeSlice
    {
        public Type Type;

        public Dictionary<(string name, int parametersCount), List<(MethodInfo method, ParameterInfo[] parameters)>> MethodsMap;

        public Dictionary<int, List<(ConstructorInfo method, ParameterInfo[] parameters)>> CtorsMap;

        public Dictionary<string, FieldInfo> FieldsMap;

        public Dictionary<string, PropertyInfo> PropsMap;
    }

    public static Dictionary<string, TypeSlice> Init()
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies();

        var types = new List<Type>(1024);

        for (int i = 0; i != asms.Length; i++)
        {
            var asm = asms[i];

            var asmTypes = Type.EmptyTypes;

            try
            {
                asmTypes = asm.GetTypes();
            }
            catch
            { }

            for (var typeI = 0; typeI != asmTypes.Length; typeI++)
            {
                var asmType = asmTypes[typeI];

                if (asmType.IsPublic
                    && asmType.Namespace is SYSTEM or SYSTEM_TEXT)
                {
                    types.Add(asmType);
                }
            }
        }

        types.Add(typeof(object?[]));

        var a = typeof(object?[]).Name;

        var typesSpan = types.AsMemory().Span;
        var typeSlices = new Dictionary<string, TypeSlice>(typesSpan.Length * 3);

        for (var i = 0; i != typesSpan.Length; i++)
        {
            var type = typesSpan[i];

            var typeFields = type.GetFields();
            var typeMethods = type.GetMethods();
            var typeProperties = type.GetProperties();
            var typeCtors = type.GetConstructors();

            var methodsMap = new Dictionary<(string name, int parametersCount), List<(MethodInfo method, ParameterInfo[] parameters)>>(typeMethods.Length * 2);

            for (var methodI = 0; methodI != typeMethods.Length; methodI++)
            {
                var method = typeMethods[methodI];
                var parameters = method.GetParameters();

                (!methodsMap.TryGetValue((method.Name, parameters.Length), out var otherGroup)
                    ? methodsMap[(method.Name, parameters.Length)] = []
                    : otherGroup
                ).Add((method, parameters));
            }

            var ctorsMap = new Dictionary<int, List<(ConstructorInfo ctor, ParameterInfo[] parameters)>>(typeMethods.Length * 2);

            for (var ctorI = 0; ctorI != typeCtors.Length; ctorI++)
            {
                var ctor = typeCtors[ctorI];
                var parameters = ctor.GetParameters();

                (!ctorsMap.TryGetValue(parameters.Length, out var otherGroup)
                    ? ctorsMap[parameters.Length] = []
                    : otherGroup
                ).Add((ctor, parameters));
            }

            var fieldsMap = new Dictionary<string, FieldInfo>(typeFields.Length * 2);
            for (var fieldI = 0; fieldI != typeFields.Length; fieldI++)
            {
                var field = typeFields[fieldI];
                fieldsMap[field.Name] = field;
            }

            var propsMap = new Dictionary<string, PropertyInfo>(typeProperties.Length * 2);
            for (var propI = 0; propI != typeProperties.Length; propI++)
            {
                var prop = typeProperties[propI];
                propsMap[prop.Name] = prop;
            }

            typeSlices.Add(type.Name, new()
            {
                Type = type,
                MethodsMap = methodsMap,
                CtorsMap = ctorsMap,
                FieldsMap = fieldsMap,
                PropsMap = propsMap,
            });

            if (type == typeof(object[]))
            {
                typeSlices.Add("object[]", new()
                {
                    Type = type,
                    MethodsMap = methodsMap,
                    CtorsMap = ctorsMap,
                    FieldsMap = fieldsMap,
                    PropsMap = propsMap,
                });

                typeSlices.Add("object?[]", new()
                {
                    Type = type,
                    MethodsMap = methodsMap,
                    CtorsMap = ctorsMap,
                    FieldsMap = fieldsMap,
                    PropsMap = propsMap,
                });
            }
        }

        return typeSlices;
    }

    static void Traverse(
        SemanticModel semantic,
        SyntaxNode node,
        List<IlInstruction> instructions,
        VariablesScoper locals,
        bool outputWillBeUsed = false
    )
    {
        switch (node)
        {
            case EmptyStatementSyntax: break;
            case BlockSyntax block:
                foreach (var childNode in block.ChildNodes())
                {
                    Traverse(semantic, childNode, instructions, locals.NewScope());
                }

                break;

            case ParenthesizedExpressionSyntax parenthesizedExpression:
                Traverse(semantic, parenthesizedExpression.Expression, instructions, locals, outputWillBeUsed);
                break;

            case PostfixUnaryExpressionSyntax postUnary:
                {
                    var operand = semantic.GetSymbolInfo(postUnary.Operand);

                    // @Incomplete: not support fields/properties/indexes for now
                    if (operand.Symbol is not ISymbol variableSymbol || variableSymbol.Kind != SymbolKind.Local)
                    {
                        throw new NotImplementedException();
                    }

                    // @Incomplete: not found variable diagnostic
                    if (!locals.TryFind(variableSymbol.Name, out var variableLink))
                    {
                        throw new NotImplementedException();
                    }

                    var unaryOperation = postUnary.Kind() switch
                    {
                        SyntaxKind.PostIncrementExpression => IlOperation.Add,
                        SyntaxKind.PostDecrementExpression => IlOperation.Sub,
                        _ => default(IlOperation?)
                    };

                    // @Incomplete: not supported smth
                    if (unaryOperation is null)
                    {
                        throw new NotImplementedException();
                    }

                    instructions.Add(new() { Code = IlOperation.LoadLoc, VariableLink = variableLink }); // val

                    if (outputWillBeUsed)
                    {
                        instructions.Add(new() { Code = IlOperation.Duplicate }); // val, val
                    }

                    instructions.Add(new() { Code = IlOperation.LoadInt32, LoadInt = 1 }); // val, 1
                    instructions.Add(new() { Code = unaryOperation.Value }); // val + 1
                    instructions.Add(new() { Code = IlOperation.SetLoc, VariableLink = variableLink }); //
                }
                break;

            case PrefixUnaryExpressionSyntax prefUnary:
                {
                    var operand = semantic.GetSymbolInfo(prefUnary.Operand);

                    // @Incomplete: not support fields/properties/indexes for now
                    if (operand.Symbol is not ISymbol variableSymbol || variableSymbol.Kind != SymbolKind.Local)
                    {
                        throw new NotImplementedException();
                    }

                    // @Incomplete: not found variable diagnostic
                    if (!locals.TryFind(variableSymbol.Name, out var variableLink))
                    {
                        throw new NotImplementedException();
                    }

                    var unaryOperation = prefUnary.Kind() switch
                    {
                        SyntaxKind.PreIncrementExpression => IlOperation.Add,
                        SyntaxKind.PreDecrementExpression => IlOperation.Sub,
                        _ => default(IlOperation?)
                    };

                    // @Incomplete: not supported smth
                    if (unaryOperation is null)
                    {
                        throw new NotImplementedException();
                    }

                    instructions.Add(new() { Code = IlOperation.LoadLoc, VariableLink = variableLink }); // val
                    instructions.Add(new() { Code = IlOperation.LoadInt32, LoadInt = 1 }); // val, 1
                    instructions.Add(new() { Code = unaryOperation.Value }); // val + 1

                    if (outputWillBeUsed)
                    {
                        instructions.Add(new() { Code = IlOperation.Duplicate }); // val + 1, val + 1
                    }

                    instructions.Add(new() { Code = IlOperation.SetLoc, VariableLink = variableLink }); // val
                }
                break;

            // x + y, x * y, x && y, x != y, ect...
            case BinaryExpressionSyntax binaryStement:

                var operation = default(IlOperation?);
                var binaryStatementKind = binaryStement.Kind();

                switch (binaryStatementKind)
                {
                    case SyntaxKind.AddExpression:
                        operation = IlOperation.Add;
                        goto default;

                    case SyntaxKind.SubtractExpression:
                        operation = IlOperation.Sub;
                        goto default;

                    case SyntaxKind.MultiplyExpression:
                        operation = IlOperation.Mul;
                        goto default;

                    case SyntaxKind.DivideExpression:
                        operation = IlOperation.Div;
                        goto default;

                    case SyntaxKind.ModuloExpression:
                        operation = IlOperation.Mod;
                        goto default;

                    case SyntaxKind.EqualsExpression:
                        operation = IlOperation.Equals;
                        goto default;

                    case SyntaxKind.NotEqualsExpression:
                        operation = IlOperation.NotEquals;
                        goto default;

                    case SyntaxKind.LessThanExpression:
                        operation = IlOperation.LessThan;
                        goto default;

                    case SyntaxKind.GreaterThanExpression:
                        operation = IlOperation.GreaterThan;
                        goto default;

                    case SyntaxKind.GreaterThanOrEqualExpression:
                    case SyntaxKind.LessThanOrEqualExpression:

                        var typeInfoF = semantic.GetTypeInfo(binaryStement.Right).Type;

                        // @Incomplete:
                        if (typeInfoF == null || !_sharedTypes.TryGetValue(typeInfoF.Name, out var comparisonType))
                        {
                            throw new NotImplementedException();
                        }

                        var comparison = binaryStatementKind == SyntaxKind.GreaterThanOrEqualExpression
                            ? IlOperation.GreaterThan
                            : IlOperation.LessThan;

                        var temp1 = new VariableLink() { Type = comparisonType.Type };
                        var temp2 = new VariableLink() { Type = comparisonType.Type };

                        locals.AddTempLocal(temp1);
                        locals.AddTempLocal(temp2);

                        Traverse(semantic, binaryStement.Left, instructions, locals, outputWillBeUsed: true);

                        instructions.Add(new() { Code = IlOperation.Duplicate });
                        instructions.Add(new() { Code = IlOperation.SetLoc, VariableLink = temp1 });

                        Traverse(semantic, binaryStement.Right, instructions, locals, outputWillBeUsed: true);

                        instructions.Add(new() { Code = IlOperation.Duplicate });
                        instructions.Add(new() { Code = IlOperation.SetLoc, VariableLink = temp2 });

                        // temp1 - first
                        // temp2 - second
                        // stack - first, second

                        // stack - 1/0
                        instructions.Add(new() { Code = comparison });

                        instructions.Add(new() { Code = IlOperation.LoadLoc, VariableLink = temp1 });
                        instructions.Add(new() { Code = IlOperation.LoadLoc, VariableLink = temp2 });

                        // 1/0, first, second
                        instructions.Add(new() { Code = IlOperation.Equals });

                        // 1/0 | 1/0
                        instructions.Add(new() { Code = IlOperation.Bit_OR });
                        break;

                    case SyntaxKind.LogicalAndExpression:
                        operation = IlOperation.Bit_AND;
                        goto default;

                    case SyntaxKind.LogicalOrExpression:
                        operation = IlOperation.Bit_OR;
                        goto default;

                    case SyntaxKind.BitwiseAndExpression:
                        operation = IlOperation.Bit_AND;
                        goto default;

                    case SyntaxKind.BitwiseOrExpression:
                        operation = IlOperation.Bit_OR;
                        goto default;

                    case SyntaxKind.ExclusiveOrExpression:
                        operation = IlOperation.Bit_XOR;
                        goto default;

                    case SyntaxKind.LeftShiftExpression:
                        operation = IlOperation.Shift_Left;
                        goto default;

                    case SyntaxKind.RightShiftExpression:
                        operation = IlOperation.Shift_Right;
                        goto default;

                    // @Incomplete: create label and use goto if null
                    case SyntaxKind.CoalesceExpression:
                        var endLabel = new LabelLink();

                        var typeInfo = semantic.GetTypeInfo(binaryStement.Left);

                        instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = endLabel });

                        Traverse(semantic, binaryStement.Left, instructions, locals, outputWillBeUsed: true);

                        // @Incomplete: unable to make this work
                        if (typeInfo.Type is INamedTypeSymbol namedType
                            && namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
                        {
                            throw new NotImplementedException();

                            var typeArg = namedType.TypeArguments[0];

                            // @Incomplete
                            if (!_sharedTypes.TryGetValue(typeArg.Name, out var nullableType))
                            {
                                throw new NotImplementedException();
                            }

                            var callingGetter = typeof(Nullable<>)
                                .MakeGenericType(nullableType.Type)
                                .GetProperty(nameof(Nullable<int>.HasValue))
                                .GetGetMethod();

                            /*
                            var equals = typeof(object)
                                .GetMethod(nameof(object.Equals), BindingFlags.Static | BindingFlags.Public);
                            */

                            /*
                            instructions.Add(new() { Code = IlOperation.Duplicate });

                            instructions.Add(new() { Code = IlOperation.LoadNull });
                            instructions.Add(new() { Code = IlOperation.NotEquals });
                            */

                            if (instructions[instructions.Count - 1].Code == IlOperation.LoadLoc)
                            {

                                instructions.Add(new()
                                {
                                    Code = IlOperation.Initobj,
                                    Type = typeof(Nullable<>)
                                        .MakeGenericType(nullableType.Type)
                                });

                                /*
                                var nullableCtor = typeof(Nullable<>)
                                        .MakeGenericType(nullableType.Type)
                                        .GetConstructor([nullableType.Type]);

                                instructions.Add(new()
                                {
                                    Code = IlOperation.Newobj,
                                    CtorInfo = nullableCtor
                                });

                                instructions.Add(new()
                                {
                                    Code = IlOperation.LoadLoc,
                                    VariableLink = instructions[instructions.Count - 1].VariableLink,
                                });*/
                            }

                            instructions.Add(new()
                            {
                                Code = IlOperation.Call,
                                CallingMethod = callingGetter
                            }); // result will be the same as for NotEquals
                        }
                        else
                        {
                            instructions.Add(new() { Code = IlOperation.Duplicate });

                            instructions.Add(new() { Code = IlOperation.LoadNull });
                            instructions.Add(new() { Code = IlOperation.NotEquals });
                        }

                        instructions.Add(new() { Code = IlOperation.GotoIf_True, LabelLink = endLabel });

                        // if null
                        // poping first value, because it's clone is null
                        instructions.Add(new() { Code = IlOperation.Pop });
                        Traverse(semantic, binaryStement.Right, instructions, locals, outputWillBeUsed: true);

                        // end
                        instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = endLabel });

                        break;

                    default:

                        // @Incomplete: lack of report
                        if (operation is null)
                        {
                            throw new NotImplementedException();
                        }

                        Traverse(semantic, binaryStement.Left, instructions, locals, outputWillBeUsed: true);
                        Traverse(semantic, binaryStement.Right, instructions, locals, outputWillBeUsed: true);
                        instructions.Add(new() { Code = operation.Value });
                        break;
                }
                break;

            // new[], new[] { ... }
            case ArrayCreationExpressionSyntax arrayCreation:
                {

                    var typeInfo = semantic.GetTypeInfo(arrayCreation);
                    var initializer = arrayCreation.Initializer;
                    var arrayTypeSyntax = arrayCreation.Type;

                    // @Incomplete
                    if (typeInfo.Type is null)
                    {
                        throw new NotImplementedException();
                    }

                    var isRefType = typeInfo.Type is { IsReferenceType: true };
                    var typeName = typeInfo.Type.Name;

                    // @Incomplete: IArrayTypeSymbol.Name returns "", implementation should be differen, but it's ok for now
                    if (typeInfo.Type is IArrayTypeSymbol arrayType
                        && arrayType.ElementType.SpecialType == SpecialType.System_Object
                        && _sharedTypes.TryGetValue(typeof(object[]).Name, out var typeSlice))
                    { }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    var arraySize = initializer?.Expressions.Count ?? 0;

                    if (arrayTypeSyntax.RankSpecifiers.Count != 0)
                    {
                        var rankSpecifier = arrayTypeSyntax.RankSpecifiers[0];

                        if (rankSpecifier.Sizes.Count != 0
                            && rankSpecifier.Sizes[0] is LiteralExpressionSyntax sizeExpression
                            && sizeExpression.Kind() is SyntaxKind.NumericLiteralExpression
                            && sizeExpression.Token.Value is int arraySizeLiteral
                        )
                        {
                            arraySize = arraySizeLiteral;
                        }
                    }

                    // new T[arraySize]
                    instructions.Add(new() { Code = IlOperation.LoadInt32, LoadInt = arraySize });
                    instructions.Add(new() { Code = IlOperation.NewArray, Type = typeSlice.Type });

                    if (initializer is not null)
                    {
                        var initExpressions = initializer.Expressions;

                        for (var arrI = 0; arrI < initExpressions.Count; arrI++)
                        {
                            var expression = initExpressions[arrI];

                            // T[], T[]
                            instructions.Add(new() { Code = IlOperation.Duplicate });

                            // T[], T[], i
                            instructions.Add(new() { Code = IlOperation.LoadInt32, LoadInt = arrI });

                            // T[], T[], i, T
                            Traverse(semantic, expression, instructions, locals, outputWillBeUsed: true);

                            var expressionType = semantic.GetTypeInfo(expression).Type;

                            var needsBoxing = isRefType && expressionType is { IsValueType: true };
                            if (needsBoxing)
                            {
                                _sharedTypes.TryGetValue(expressionType.Name, out var typeSlice2);

                                // T[], T[], (T)V
                                instructions.Add(new() { Code = IlOperation.Box, Type = typeof(object) ?? typeSlice2.Type });

                                //instructions.Add(new() { Code = IlOperation.Box });
                            }

                            // T[], T[i] = T;
                            instructions.Add(new() { Code = IlOperation.SetArrayElement });
                        }
                    }
                }
                break;

            // new()
            case ObjectCreationExpressionSyntax create:
                {
                    var typeSymbol = semantic.GetTypeInfo(create).Type ?? throw new InvalidOperationException();

                    // @Incomplete: fire not allowed type diagnosic
                    if (!IsAllowedNamespace(typeSymbol.ContainingNamespace?.ToDisplayString()))
                    {
                        throw new NotImplementedException();
                    }

                    // @Incomplete: fire not allowed type diagnosic
                    if (!_sharedTypes.TryGetValue(typeSymbol.Name, out var type))
                    {
                        throw new NotImplementedException();
                    }

                    var ctorSymbol = semantic.GetSymbolInfo(create);

                    if (ctorSymbol.Symbol is not IMethodSymbol methodSymbol)
                    {
                        throw new InvalidOperationException();
                    }

                    var found = default(ConstructorInfo);

                    if (type.CtorsMap.TryGetValue(methodSymbol.Parameters.Length, out var methods))
                    {
                        foreach (var (method, parameters) in methods)
                        {
                            var match = true;

                            // @Incomplete: apply the same for ctor handler
                            for (var i = 0; i < parameters.Length; i++)
                            {
                                var parameterSymbol = methodSymbol.Parameters[i].Type;

                                if (!parameters[i].ParameterType.IsGenericParameter
                                    && !(parameterSymbol is INamedTypeSymbol paramType && paramType.IsGenericType))
                                { }
                                else if (
                                    parameters[i].ParameterType.IsGenericParameter
                                    != (parameterSymbol is INamedTypeSymbol paramType2 && paramType2.IsGenericType)
                                )
                                {
                                    match = false;
                                    break;
                                }

                                if (!_sharedTypes.TryGetValue(parameterSymbol.Name, out var parameterTypeSlice))
                                {
                                    match = false;
                                    break;
                                }

                                if (parameters[i].ParameterType != parameterTypeSlice.Type)
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                found = method;
                                break;
                            }
                        }
                    }

                    // @Incomplete: fire not allowed ctor diagnostic
                    if (found == null)
                    {
                        throw new NotImplementedException();
                    }


                    // calling method with args not at the same order as they are declared in the method's signature
                    var orderByArgName = default(Dictionary<string, int>);

                    var orderedArgs = new VariableLink[methodSymbol.Parameters.Length];

                    var paramsArgSettedEarly = false;

                    var ctorArgsCount = create.ArgumentList?.Arguments.Count ?? 0;

                    for (var argI = 0; argI < ctorArgsCount; argI++)
                    {
                        var argumentList = create.ArgumentList!.Arguments;
                        var argument = argumentList[argI];

                        var order = argI;
                        if (argument.NameColon != null)
                        {
                            orderByArgName ??= methodSymbol.Parameters.Select((x, i) => (x, i)).ToDictionary(k => k.x.Name, v => v.i);
                            order = orderByArgName[argument.NameColon.Name.Identifier.ValueText];
                        }

                        var parameter = methodSymbol.Parameters[order];

                        if (order != methodSymbol.Parameters.Length - 1)
                        {
                            if (parameter.IsParams)
                            {
                                paramsArgSettedEarly = true;
                            }
                        }

                        // @Incomplete
                        if (!_sharedTypes.TryGetValue(parameter.Type.Name, out var paramTypeSlice))
                        {
                            throw new NotImplementedException();
                        }

                        if (!paramsArgSettedEarly && parameter.IsParams && parameter.Type is not IArrayTypeSymbol)
                        {
                            var arraySize = argumentList.Count - argI;

                            // new T[arraySize]
                            instructions.Add(new() { Code = IlOperation.LoadInt32, LoadInt = arraySize });
                            instructions.Add(new() { Code = IlOperation.NewArray, Type = paramTypeSlice.Type });

                            var arrI = 0;

                            do
                            {
                                // copy link to array
                                instructions.Add(new() { Code = IlOperation.Duplicate });

                                // destination index
                                instructions.Add(new() { Code = IlOperation.LoadInt32, LoadInt = arrI });

                                arrI += 1;

                                argument = argumentList[argI];

                                var argTypeInfo = semantic.GetTypeInfo(argument.Expression);
                                var needBoxing = parameter.Type.IsReferenceType && argTypeInfo.Type is { IsTupleType: true };

                                Traverse(semantic, argument.Expression, instructions, locals, outputWillBeUsed: true);

                                if (needBoxing)
                                {
                                    instructions.Add(new() { Code = IlOperation.Box });
                                }

                                // storing in array
                                instructions.Add(new() { Code = IlOperation.SetArrayElement });

                            } while (++argI < argumentList.Count);

                            var tempLocal = new VariableLink() { Type = paramTypeSlice.Type };
                            locals.AddTempLocal(tempLocal);

                            instructions.Add(new() { Code = IlOperation.SetLoc, VariableLink = tempLocal });

                            orderedArgs[order] = tempLocal;
                        }
                        else if (argument.Expression is IdentifierNameSyntax localVariableArg)
                        {
                            var varName = localVariableArg.Identifier.ValueText;

                            // @Incomplete: not found variable
                            if (!locals.TryFind(varName, out var variableLink))
                            {
                                throw new NotImplementedException();
                            }

                            orderedArgs[order] = variableLink;
                        }
                        else
                        {
                            var argTypeInfo = semantic.GetTypeInfo(argument.Expression);

                            var needBoxing = parameter.Type.IsReferenceType && argTypeInfo.Type is { IsTupleType: true };
                            var tempLocal = new VariableLink() { Type = paramTypeSlice.Type };

                            locals.AddTempLocal(tempLocal);

                            Traverse(semantic, argument.Expression, instructions, locals, outputWillBeUsed: true);

                            if (needBoxing)
                            {
                                _sharedTypes.TryGetValue(argTypeInfo.Type.Name, out var typeSlice2);

                                instructions.Add(new() { Code = IlOperation.Box, Type = typeSlice2.Type });
                            }

                            instructions.Add(new() { Code = IlOperation.SetLoc, VariableLink = tempLocal });

                            orderedArgs[order] = tempLocal;
                        }
                    }

                    for (var i = 0; i < orderedArgs.Length; i++)
                    {
                        var localAsArg = orderedArgs[i];
                        instructions.Add(new() { Code = IlOperation.LoadLoc, VariableLink = localAsArg });
                    }

                    instructions.Add(new IlInstruction
                    {
                        Code = IlOperation.Newobj,
                        CtorInfo = found
                    });

                    if (!outputWillBeUsed)
                    {
                        instructions.Add(new() { Code = IlOperation.Pop });
                    }
                }
                break;

            // "defered" like operation (the actual operation should be at the end)
            case ReturnStatementSyntax returnStatement:
                if (returnStatement.Expression != null)
                {
                    Traverse(semantic, returnStatement.Expression, instructions, locals, outputWillBeUsed: true);
                }

                instructions.Add(new IlInstruction()
                {
                    Code = IlOperation.Return
                });
                break;
            case LocalDeclarationStatementSyntax locDeclaration:
                {
                    var declaration = locDeclaration.Declaration;
                    Traverse(semantic, declaration, instructions, locals, outputWillBeUsed: true);
                }
                break;

            case VariableDeclarationSyntax varDeclaration:
                {
                    var typeSymbol = semantic.GetTypeInfo(varDeclaration.Type).Type ?? throw new InvalidOperationException();

                    // @Incomplete: fire not allowed type diagnosic
                    if (typeSymbol.ContainingNamespace is not null && !IsAllowedNamespace(typeSymbol.ContainingNamespace?.ToDisplayString()))
                    {
                        throw new NotImplementedException();
                    }

                    Type type;

                    if (typeSymbol is IArrayTypeSymbol arrayType
                            && arrayType.ElementType.SpecialType == SpecialType.System_Object
                            && _sharedTypes.TryGetValue(typeof(object[]).Name, out var paramTypeSlice))
                    {
                        type = typeof(object[]);
                    }
                    else if (typeSymbol is INamedTypeSymbol namedType
                        && namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
                    {
                        var typeArg = namedType.TypeArguments[0];

                        // @Incomplete
                        if (!_sharedTypes.TryGetValue(typeArg.Name, out var nullableType))
                        {
                            throw new NotImplementedException();
                        }

                        type = typeof(Nullable<>).MakeGenericType(nullableType.Type);
                    }
                    else if (_sharedTypes.TryGetValue(typeSymbol.Name, out var typeSlice))
                    {
                        type = typeSlice.Type;
                    }
                    // @Incomplete: fire not allowed type diagnosic
                    else
                    {
                        throw new NotImplementedException();
                    }

                    for (var i = 0; i < varDeclaration.Variables.Count; i++)
                    {
                        var variable = varDeclaration.Variables[i];
                        var name = variable.Identifier.Text;

                        var variableLink = new VariableLink() { Type = type };
                        locals.Set(name, variableLink);

                        if (variable.Initializer is not null)
                        {
                            var initializer = variable.Initializer.Value;

                            Traverse(semantic, initializer, instructions, locals, outputWillBeUsed: true);

                            instructions.Add(new IlInstruction
                            {
                                Code = IlOperation.SetLoc,
                                VariableLink = variableLink,
                            });
                        }
                    }
                }
                break;

            case AssignmentExpressionSyntax assignment:
                {

                    // @Incomplete: handle chain of assignments (a = i = b = 0)
                    if (assignment.Left is not IdentifierNameSyntax variable)
                    {
                        throw new NotImplementedException();
                    }

                    var name = variable.Identifier.Text;
                    var type = semantic.GetTypeInfo(variable);

                    if (type.Type is not ITypeSymbol typeSymbol)
                    {
                        throw new NotImplementedException();
                    }

                    // @Incomplete:
                    if (!_sharedTypes.TryGetValue(typeSymbol.Name, out var varType))
                    {
                        throw new NotImplementedException();
                    }

                    // @Incomplete
                    if (!locals.TryFind(name, out var variableLink))
                    {
                        throw new NotImplementedException();
                    }

                    var operationOnCurrentState = assignment.Kind() switch
                    {
                        SyntaxKind.AddAssignmentExpression => IlOperation.Add,
                        SyntaxKind.SubtractAssignmentExpression => IlOperation.Sub,
                        SyntaxKind.MultiplyAssignmentExpression => IlOperation.Mul,
                        SyntaxKind.DivideAssignmentExpression => IlOperation.Div,
                        SyntaxKind.OrAssignmentExpression => IlOperation.Bit_OR,
                        SyntaxKind.ExclusiveOrAssignmentExpression => IlOperation.Bit_XOR,
                        SyntaxKind.AndAssignmentExpression => IlOperation.Bit_AND,
                        _ => default(IlOperation?)
                    };

                    Traverse(semantic, assignment.Right, instructions, locals, outputWillBeUsed: true);

                    if (operationOnCurrentState is not null)
                    {
                        instructions.Add(new IlInstruction
                        {
                            Code = IlOperation.LoadLoc,
                            VariableLink = variableLink,
                        });

                        instructions.Add(new() { Code = operationOnCurrentState.Value });
                    }

                    instructions.Add(new IlInstruction
                    {
                        Code = IlOperation.SetLoc,
                        VariableLink = variableLink,
                    });
                }
                break;

            case InvocationExpressionSyntax invocation:
                {
                    if (invocation.Expression is not MemberAccessExpressionSyntax accessing)
                    {
                        throw new NotImplementedException();
                    }

                    var variableName = default(string?);

                    if (accessing.Expression is PredefinedTypeSyntax predefined)
                    { }
                    else if (accessing.Expression is IdentifierNameSyntax variableIdentifier)
                    {
                        var identifierSemantic = semantic.GetSymbolInfo(variableIdentifier);
                        if (identifierSemantic.Symbol is { Kind: SymbolKind.Local })
                        {
                            variableName = variableIdentifier.Identifier.ValueText;
                        }
                    }
                    else
                    {
                        Traverse(semantic, accessing.Expression, instructions, locals, outputWillBeUsed: true);
                    }

                    var invocationSymbolInfo = semantic.GetSymbolInfo(invocation);

                    if (invocationSymbolInfo.Symbol is not IMethodSymbol methodSymbol)
                    {
                        throw new NotImplementedException();
                    }

                    if (!IsAllowedNamespace(methodSymbol.ContainingNamespace?.ToDisplayString()))
                    {
                        throw new InvalidExpressionException();
                    }

                    // @Speed: Dictionary<name, (type, fields, ctors, methods, properties)>
                    // @Incomplete: fire not allowed method diagnostic
                    if (!_sharedTypes.TryGetValue(methodSymbol.ContainingType.Name, out var typeSlice))
                    {
                        throw new NotImplementedException();
                    }

                    var found = default(MethodInfo);

                    if (typeSlice.MethodsMap.TryGetValue((methodSymbol.Name, methodSymbol.Parameters.Length), out var methods))
                    {
                        foreach (var (method, parameters) in methods)
                        {
                            var match = true;

                            // @Incomplete: apply the same for ctor handler
                            for (var i = 0; i < parameters.Length; i++)
                            {
                                var parameterSymbol = methodSymbol.Parameters[i].Type;

                                if (!parameters[i].ParameterType.IsGenericParameter
                                    && !(parameterSymbol is INamedTypeSymbol paramType && paramType.IsGenericType))
                                { }
                                else if (
                                    parameters[i].ParameterType.IsGenericParameter
                                    != (parameterSymbol is INamedTypeSymbol paramType2 && paramType2.IsGenericType)
                                )
                                {
                                    match = false;
                                    break;
                                }

                                if (parameterSymbol is IArrayTypeSymbol arrayType
                                    && arrayType.ElementType.SpecialType == SpecialType.System_Object
                                    && _sharedTypes.TryGetValue(typeof(object[]).Name, out var parameterTypeSlice))
                                { }
                                else if (!_sharedTypes.TryGetValue(parameterSymbol.Name, out parameterTypeSlice))
                                {
                                    match = false;
                                    break;
                                }

                                if (parameters[i].ParameterType != parameterTypeSlice.Type)
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                found = method;
                                break;
                            }
                        }
                    }

                    // @Incomplete: fire nto allowed method diagnostic
                    if (found is null)
                    {
                        throw new NotImplementedException();
                    }

                    // load sb
                    if (variableName != null)
                    {
                        // @Incomplete: not found variable
                        if (!locals.TryFind(variableName, out var variableLink))
                        {
                            throw new NotImplementedException();
                        }

                        instructions.Add(new()
                        {
                            Code = IlOperation.LoadLoc,
                            VariableLink = variableLink
                        });
                    }

                    // calling method with args not at the same order as they are declared in the method's signature
                    var orderByArgName = default(Dictionary<string, int>);

                    var orderedArgs = new VariableLink[methodSymbol.Parameters.Length];

                    var paramsArgSettedEarly = false;

                    var argumentList = invocation.ArgumentList.Arguments;
                    for (var argI = 0; argI < argumentList.Count; argI++)
                    {
                        var argument = argumentList[argI];

                        var order = argI;
                        if (argument.NameColon != null)
                        {
                            orderByArgName ??= methodSymbol.Parameters.Select((x, i) => (x, i)).ToDictionary(k => k.x.Name, v => v.i);
                            order = orderByArgName[argument.NameColon.Name.Identifier.ValueText];
                        }

                        var parameter = methodSymbol.Parameters[order];

                        if (order != methodSymbol.Parameters.Length - 1)
                        {
                            if (parameter.IsParams)
                            {
                                paramsArgSettedEarly = true;
                            }
                        }

                        // @Incomplete
                        if (parameter.Type is IArrayTypeSymbol arrayType
                            && arrayType.ElementType.SpecialType == SpecialType.System_Object
                            && _sharedTypes.TryGetValue(typeof(object[]).Name, out var paramTypeSlice))
                        { }
                        else if (!_sharedTypes.TryGetValue(parameter.Type.Name, out paramTypeSlice))
                        {
                            throw new NotImplementedException();
                        }

                        if (!paramsArgSettedEarly && parameter.IsParams && parameter.Type is not IArrayTypeSymbol)
                        {
                            var arraySize = argumentList.Count - argI;

                            // new T[arraySize]
                            instructions.Add(new() { Code = IlOperation.LoadInt32, LoadInt = arraySize });
                            instructions.Add(new() { Code = IlOperation.NewArray, Type = paramTypeSlice.Type });

                            var arrI = 0;

                            var isRefType = parameter.Type is { IsReferenceType: true };

                            do
                            {
                                // copy link to array
                                instructions.Add(new() { Code = IlOperation.Duplicate });

                                // destination index
                                instructions.Add(new() { Code = IlOperation.LoadInt32, LoadInt = arrI });

                                arrI += 1;

                                argument = argumentList[argI];

                                var argTypeInfo = semantic.GetTypeInfo(argument.Expression);
                                var needBoxing = isRefType && argTypeInfo.Type is { IsTupleType: true };

                                Traverse(semantic, argument.Expression, instructions, locals, outputWillBeUsed: true);

                                if (needBoxing)
                                {
                                    instructions.Add(new() { Code = IlOperation.Box });
                                }

                                // storing in array
                                instructions.Add(new() { Code = IlOperation.SetArrayElement });

                            } while (++argI < argumentList.Count);

                            var tempLocal = new VariableLink() { Type = paramTypeSlice.Type };
                            locals.AddTempLocal(tempLocal);

                            instructions.Add(new() { Code = IlOperation.SetLoc, VariableLink = tempLocal });

                            orderedArgs[order] = tempLocal;
                        }
                        else if (argument.Expression is IdentifierNameSyntax localVariableArg)
                        {
                            var varName = localVariableArg.Identifier.ValueText;

                            // @Incomplete: not found variable
                            if (!locals.TryFind(varName, out var variableLink))
                            {
                                throw new NotImplementedException();
                            }

                            orderedArgs[order] = variableLink;
                        }
                        else
                        {
                            var argTypeInfo = semantic.GetTypeInfo(argument.Expression);

                            var needBoxing = parameter.Type.IsReferenceType && argTypeInfo.Type is { IsTupleType: true };
                            var tempLocal = new VariableLink() { Type = paramTypeSlice.Type };

                            locals.AddTempLocal(tempLocal);

                            Traverse(semantic, argument.Expression, instructions, locals, outputWillBeUsed: true);

                            if (needBoxing)
                            {
                                instructions.Add(new() { Code = IlOperation.Box });
                            }

                            instructions.Add(new() { Code = IlOperation.SetLoc, VariableLink = tempLocal });

                            orderedArgs[order] = tempLocal;
                        }
                    }

                    for (var i = 0; i < orderedArgs.Length; i++)
                    {
                        var localAsArg = orderedArgs[i];
                        instructions.Add(new() { Code = IlOperation.LoadLoc, VariableLink = localAsArg });
                    }

                    if (found.Name.Contains("AppendFormat"))
                    {
                        instructions.Add(new()
                        {
                            Code = IlOperation.CallVarArgs,
                            CallingMethod = found,
                        });
                    }
                    else
                    {
                        instructions.Add(new()
                        {
                            Code = typeSlice.Type.IsSealed ? IlOperation.Call : IlOperation.Callvirt,
                            CallingMethod = found,
                        });
                    }

                    if (!outputWillBeUsed && found.ReturnType != typeof(void))
                    {
                        instructions.Add(new() { Code = IlOperation.Pop });
                    }
                }

                break;

            // string, int, float, etc...
            case IdentifierNameSyntax variableIdentifier:
                {

                    // @Incomplete: add args, props, fields
                    var identifierSemantic = semantic.GetSymbolInfo(variableIdentifier);
                    if (identifierSemantic.Symbol is not { Kind: SymbolKind.Local })
                    {
                        throw new NotImplementedException();
                    }

                    var variableName = variableIdentifier.Identifier.ValueText;
                    // @Incomplete: not found variable
                    if (!locals.TryFind(variableName, out var variableLink))
                    {
                        throw new NotImplementedException();
                    }

                    instructions.Add(new()
                    {
                        Code = IlOperation.LoadLoc,
                        VariableLink = variableLink
                    });
                }
                break;
            case LiteralExpressionSyntax literal:
                {
                    IlInstruction? instruction = (SyntaxKind)literal.RawKind switch
                    {
                        SyntaxKind.StringLiteralExpression => new() { Code = IlOperation.LoadStr, LoadString = (string?)literal.Token.Value },
                        SyntaxKind.NumericLiteralExpression => new() { Code = IlOperation.LoadInt32, LoadInt = (int)literal.Token.Value! },
                        SyntaxKind.CharacterLiteralExpression => new() { Code = IlOperation.LoadChar, LoadChar = (char)literal.Token.Value! },
                        SyntaxKind.NullLiteralExpression => new() { Code = IlOperation.LoadNull },
                        SyntaxKind.TrueLiteralExpression => new() { Code = IlOperation.LoadInt32, LoadInt = 1 },
                        SyntaxKind.FalseLiteralExpression => new() { Code = IlOperation.LoadInt32, LoadInt = 0 },
                        _ => default(IlInstruction?)
                    };

                    // @Incomplete: report
                    if (instruction is null)
                    {
                        throw new NotImplementedException();
                    }

                    instructions.Add(instruction.Value);
                }
                break;

            case MemberAccessExpressionSyntax access:
                {
                    var accessSemantic = semantic.GetSymbolInfo(access);

                    if (accessSemantic.Symbol is IFieldSymbol field)
                    {
                        // @Incomplete: can't load with ldsfld, so try to replace known constants with ldc_in,ldstr
                        if (field.IsConst)
                        {
                            throw new NotImplementedException();
                        }

                        // @Incomplete
                        if (!IsAllowedNamespace(field.ContainingNamespace?.ToDisplayString()))
                        {
                            throw new NotImplementedException();
                        }

                        // @Incomplete
                        if (!_sharedTypes.TryGetValue(field.ContainingType.Name, out var type))
                        {
                            throw new NotImplementedException();
                        }

                        _ = type.FieldsMap.TryGetValue(field.Name, out var fieldInfo);

                        if (fieldInfo == null)
                        {
                            throw new NotImplementedException();
                        }

                        if (!field.IsStatic)
                        {
                            Traverse(semantic, access.Expression, instructions, locals, outputWillBeUsed: true);
                        }

                        instructions.Add(new()
                        {
                            Code = field.IsStatic ? IlOperation.LoadFieldStatic : IlOperation.LoadField,
                            FieldInfo = fieldInfo
                        });
                    }
                    else if (accessSemantic.Symbol is IPropertySymbol property)
                    {
                        // @Incomplete
                        if (!IsAllowedNamespace(property.ContainingNamespace?.ToDisplayString()))
                        {
                            throw new NotImplementedException();
                        }

                        // @Incomplete
                        if (!_sharedTypes.TryGetValue(property.ContainingType.Name, out var type))
                        {
                            throw new NotImplementedException();
                        }

                        _ = type.PropsMap.TryGetValue(property.Name, out var propertyInfo);

                        if (propertyInfo == null)
                        {
                            throw new NotImplementedException();
                        }

                        if (!property.IsStatic)
                        {
                            Traverse(semantic, access.Expression, instructions, locals, outputWillBeUsed: true);
                        }

                        instructions.Add(new()
                        {
                            Code = type.Type.IsSealed ? IlOperation.Call : IlOperation.Callvirt,
                            CallingMethod = propertyInfo.GetMethod
                        });
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                }
                break;


            // else if - ElseClauseSyntax -> IfStatementSyntax
            case IfStatementSyntax ifStatement:
                {

                    var exitLabel = new LabelLink();
                    var elseLabel = ifStatement.Else != null ? new LabelLink() : null;

                    instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = exitLabel });

                    if (elseLabel != null)
                    {
                        instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = elseLabel });
                    }

                    // check
                    Traverse(semantic, ifStatement.Condition, instructions, locals, outputWillBeUsed: true);

                    // skip if false
                    instructions.Add(new() { Code = IlOperation.GotoIf_False, LabelLink = elseLabel ?? exitLabel });

                    // body
                    Traverse(semantic, ifStatement.Statement, instructions, locals.NewScope());

                    // else
                    if (ifStatement.Else != null)
                    {
                        instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = elseLabel });
                        Traverse(semantic, ifStatement.Else, instructions, locals);
                    }

                    // exit
                    instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = exitLabel });
                }
                break;
            case ElseClauseSyntax elseStatement:
                {
                    Traverse(semantic, elseStatement.Statement, instructions, locals.NewScope());
                }
                break;

            case WhileStatementSyntax or DoStatementSyntax:
                {
                    var bodyLabel = new LabelLink();
                    var exitLabel = new LabelLink();
                    var checkLabel = new LabelLink();
                    var endOfIter = new LabelLink();

                    instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = bodyLabel });
                    instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = exitLabel });
                    instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = endOfIter });
                    instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = checkLabel });

                    var asDoWhile = node as DoStatementSyntax;
                    var (condition, bodyStatement) = node switch
                    {
                        WhileStatementSyntax whileStatement => (whileStatement.Condition, whileStatement.Statement),
                        _ => (asDoWhile!.Condition, asDoWhile!.Statement)
                    };

                    // first iteration skips check
                    if (node is DoStatementSyntax)
                    {
                        instructions.Add(new() { Code = IlOperation.Goto, LabelLink = bodyLabel });
                    }

                    // check
                    instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = checkLabel });

                    Traverse(semantic, condition, instructions, locals);

                    instructions.Add(new() { Code = IlOperation.GotoIf_True, LabelLink = bodyLabel });
                    instructions.Add(new() { Code = IlOperation.Goto, LabelLink = exitLabel });

                    // body
                    //  entering scope
                    var parentScope = locals;
                    locals = locals.NewScope();

                    instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = bodyLabel });

                    foreach (var childNode in bodyStatement.ChildNodes())
                    {
                        Traverse(semantic, childNode, instructions, locals);
                    }

                    // end of iteration
                    instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = endOfIter });

                    instructions.Add(new() { Code = IlOperation.Goto, LabelLink = checkLabel });

                    // end
                    instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = exitLabel });

                    // exiting scope
                    locals = parentScope;
                }
                break;

            // for (declarations,inits; conditions; increments) { statement }
            case ForStatementSyntax forStatement:
                {
                    // entering scope
                    var parentScope = locals;
                    locals = locals.NewScope();

                    var bodyLabel = new LabelLink();
                    var exitLabel = new LabelLink();
                    var checkLabel = new LabelLink();
                    var endOfIter = new LabelLink();

                    instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = bodyLabel });
                    instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = exitLabel });
                    instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = endOfIter });

                    if (forStatement.Condition != null)
                    {
                        instructions.Add(new() { Code = IlOperation.Label_Define, LabelLink = checkLabel });
                    }

                    if (forStatement.Declaration != null)
                    {
                        Traverse(semantic, forStatement.Declaration, instructions, locals);
                    }

                    // check
                    if (forStatement.Condition != null)
                    {
                        instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = checkLabel });

                        Traverse(semantic, forStatement.Condition, instructions, locals, outputWillBeUsed: true);

                        instructions.Add(new() { Code = IlOperation.GotoIf_True, LabelLink = bodyLabel });
                        instructions.Add(new() { Code = IlOperation.Goto, LabelLink = exitLabel });
                    }

                    // body
                    instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = bodyLabel });

                    foreach (var childNode in forStatement.Statement.ChildNodes())
                    {
                        Traverse(semantic, childNode, instructions, locals);
                    }

                    // increments

                    // @Incomplete: goto to this if continue
                    instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = endOfIter });

                    foreach (var incrementor in forStatement.Incrementors)
                    {
                        Traverse(semantic, incrementor, instructions, locals);
                    }

                    var continueIterateFrom = checkLabel ?? bodyLabel;
                    instructions.Add(new() { Code = IlOperation.Goto, LabelLink = continueIterateFrom });

                    //end
                    instructions.Add(new() { Code = IlOperation.Label_Mark, LabelLink = exitLabel });

                    locals = parentScope;
                }
                break;

            case ForEachStatementSyntax forEachStatement:
                throw new NotImplementedException();
                break;

            // needs to be carefull, only for math expressions
            case ExpressionStatementSyntax expression:
                Traverse(semantic, expression.Expression, instructions, locals);
                break;

            // create report
            default:
                throw new NotImplementedException();
                break;
        }
    }

    public static void TraverseMethod(MethodDeclarationSyntax method, SemanticModel semantic)
    {
        if (semantic.GetDeclaredSymbol(method) is not IMethodSymbol methodSymbol)
        {
            return;
        }

        var skip = methodSymbol.GetAttributes().Find(RunAttribute.Name, RunAttribute.Namespace) is null;

        if (skip || method.Body == null) return;

        {
            // index - depth
            var variables = new VariablesScoper() { AllVariables = [] };
            var ilInstructs = new List<IlInstruction>(64);

            foreach (var node in method.Body.ChildNodes())
            {
                Traverse(semantic, node, ilInstructs, variables);
            }

            /*
            var asmName = new AssemblyName("TempAssembly");
            var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.RunAndCollect);
            var moduleBuilder = asmBuilder.DefineDynamicModule("module");
            var typeBuilder = moduleBuilder.DefineType("TempType", TypeAttributes.Public);

            var methodBuilder = typeBuilder.DefineMethod(
                name: "SayHello",
                returnType: typeof(string),
                attributes: MethodAttributes.Public | MethodAttributes.Static,
                parameterTypes: Type.EmptyTypes
            );
            */


            var methodBuilder = new DynamicMethod(
                name: "SayHello",
                returnType: typeof(string),
                attributes: MethodAttributes.Public | MethodAttributes.Static,
                callingConvention: CallingConventions.Standard,
                parameterTypes: Type.EmptyTypes,
                owner: typeof(object),
                skipVisibility: false
            );

            var il = methodBuilder.GetILGenerator();

            foreach (var variableLink in variables.AllVariables)
            {
                variableLink.LocalBuilder = il.DeclareLocal(variableLink.Type);
            }

            for (var i = 0; i < ilInstructs.Count; i++)
            {
                var instruct = ilInstructs[i];
                var code = instruct.Code;

                switch (code)
                {
                    case IlOperation.Newobj:
                        il.Emit(OpCodes.Newobj, instruct.CtorInfo);
                        break;
                    case IlOperation.Initobj:
                        il.Emit(OpCodes.Initobj, instruct.Type);
                        break;
                    case IlOperation.Box:
                        il.Emit(OpCodes.Box, instruct.Type);
                        break;
                    case IlOperation.NewArray:
                        il.Emit(OpCodes.Newarr, instruct.Type);
                        break;
                    case IlOperation.SetArrayElement:
                        il.Emit(OpCodes.Stelem_Ref);
                        break;
                    case IlOperation.Duplicate:
                        il.Emit(OpCodes.Dup);
                        break;
                    case IlOperation.Return:
                        il.Emit(OpCodes.Ret);
                        break;
                    case IlOperation.CallVarArgs:
                        il.EmitCall(OpCodes.Call, instruct.CallingMethod, [typeof(object)]); // @Remove
                        break;
                    case IlOperation.Call:
                        il.Emit(OpCodes.Call, instruct.CallingMethod);
                        break;
                    case IlOperation.Callvirt:
                        il.Emit(OpCodes.Callvirt, instruct.CallingMethod);
                        break;
                    case IlOperation.LoadLoc:
                        il.Emit(OpCodes.Ldloc, instruct.VariableLink.LocalBuilder);
                        break;
                    case IlOperation.SetLoc:
                        il.Emit(OpCodes.Stloc, instruct.VariableLink.LocalBuilder);
                        break;
                    case IlOperation.Pop:
                        il.Emit(OpCodes.Pop);
                        break;
                    case IlOperation.LoadNull:
                        il.Emit(OpCodes.Ldnull);
                        break;
                    case IlOperation.LoadStr:
                        il.Emit(OpCodes.Ldstr, instruct.LoadString);
                        break;
                    case IlOperation.LoadInt32:
                        il.Emit(OpCodes.Ldc_I4, instruct.LoadInt.Value);
                        break;
                    case IlOperation.LoadInt64:
                        il.Emit(OpCodes.Ldc_I4, instruct.LoadInt.Value);
                        break;
                    case IlOperation.LoadChar:
                        il.Emit(OpCodes.Ldc_I4, instruct.LoadChar.Value);
                        break;
                    case IlOperation.LoadField:
                        il.Emit(OpCodes.Ldfld, instruct.FieldInfo);
                        break;
                    case IlOperation.LoadFieldStatic:
                        il.Emit(OpCodes.Ldsfld, instruct.FieldInfo);
                        break;
                    case IlOperation.Add:
                        il.Emit(OpCodes.Add);
                        break;
                    case IlOperation.Sub:
                        il.Emit(OpCodes.Sub);
                        break;
                    case IlOperation.Mul:
                        il.Emit(OpCodes.Mul);
                        break;
                    case IlOperation.Div:
                        il.Emit(OpCodes.Div);
                        break;
                    case IlOperation.Mod:
                        il.Emit(OpCodes.Rem);
                        break;
                    case IlOperation.Equals:
                        il.Emit(OpCodes.Ceq);
                        break;
                    case IlOperation.NotEquals:
                        il.Emit(OpCodes.Ceq); // bool
                        il.Emit(OpCodes.Ldc_I4_0); // false
                        il.Emit(OpCodes.Ceq); // bool == false
                        break;
                    case IlOperation.LessThan:
                        il.Emit(OpCodes.Clt);
                        break;
                    case IlOperation.GreaterThan:
                        il.Emit(OpCodes.Cgt);
                        break;
                    case IlOperation.Bit_AND:
                        il.Emit(OpCodes.And);
                        break;
                    case IlOperation.Bit_OR:
                        il.Emit(OpCodes.Or);
                        break;
                    case IlOperation.Bit_XOR:
                        il.Emit(OpCodes.Xor);
                        break;
                    case IlOperation.Shift_Left:
                        il.Emit(OpCodes.Shl);
                        break;
                    case IlOperation.Shift_Right:
                        il.Emit(OpCodes.Shr);
                        break;
                    case IlOperation.Label_Define:
                        var label = il.DefineLabel();
                        instruct.LabelLink.Label = label;
                        break;
                    case IlOperation.Label_Mark:
                        il.MarkLabel(instruct.LabelLink.Label.Value);
                        break;
                    case IlOperation.Goto:
                        il.Emit(OpCodes.Br, instruct.LabelLink.Label.Value);
                        break;
                    case IlOperation.GotoIf_True:
                        il.Emit(OpCodes.Brtrue, instruct.LabelLink.Label.Value);
                        break;
                    case IlOperation.GotoIf_False:
                        il.Emit(OpCodes.Brfalse, instruct.LabelLink.Label.Value);
                        break;
                    case IlOperation.UnboxAny:
                        il.Emit(OpCodes.Unbox, instruct.Type);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            var sb = new StringBuilder();

            foreach (var instruction in ilInstructs)
            {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                object obj = instruction.VariableLink ??
                    instruction.Type ??
                    instruction.CtorInfo ??
                    instruction.CallingMethod ??
                    instruction.FieldInfo ??
                    instruction.LoadString ??
                    instruction.LoadInt ??
                    (object)instruction.LoadChar ??
                    instruction.LabelLink;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.


                sb.Append(instruction.Code.ToString()).Append(" ").Append(obj).Append("\n");
            }


            var res = sb.ToString();

            //var methodBytes = methodBuilder.GetMethodBody().GetILAsByteArray();

            //var tempType = typeBuilder.CreateTypeInfo()!.AsType();
            //var sayHello = tempType.GetMethod("SayHello");

            var sayHello = methodBuilder;


            var result = sayHello.Invoke(null, null);
        }
    }

    public static AttributeData? Find(
        this ImmutableArray<AttributeData> attributes,
        string attributeName,
        string attributeNamespace
    )
    {
        for (var i = 0; i < attributes.Length; i++)
        {
            var attributeClass = attributes[i].AttributeClass;
            if (attributeClass?.Name == attributeName
                && attributeClass.ContainingNamespace.Name == attributeNamespace)
            {
                return attributes[i];
            }
        }

        return default;
    }
}

#endif
using Unflat.IndentWriter;
using Unflat.Options;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace Unflat;

internal static class DifferentWay
{
    internal static void GenerateDataReaderParsers(
        SourceProductionContext productionContext,
        ImmutableArray<MatchingModel?> models)
    {
        var token = productionContext.CancellationToken;

        var sourceCode = new StringBuilder();

        foreach (var model in models)
        {
            if (token.IsCancellationRequested) return;

            if (model == null) continue;

            var matchCase = model.Value.MatchingSettings.MatchCase;

            if (matchCase <= MatchCase.None)
            {
                continue;
            }

            var type = model.Value.Type;

            var typeNamespace = !type.Namespace.IsGlobal ? type.Namespace.DisplayString : null;

            var wr = new IndentStackWriter(sourceCode);

            wr.Append($$"""
                using System;
                using System.Data;
                using System.Data.Common;
                using System.Collections.Generic;
                using System.Collections.ObjectModel;
                using System.Runtime.CompilerServices;
                using System.Threading;
                using System.Threading.Tasks;

                {{wr.Scope[
                    typeNamespace == null
                    ? AppendClass(wr, model.Value, matchCase)
                    : wr[$$""" 
                        namespace {{typeNamespace}}
                        {
                            {{AppendClass(wr, model.Value, matchCase)}}
                        }
                        """
                    ]
                ]}}
                """
            );

            var sourceCodeText = sourceCode.ToString();
            sourceCode.Clear();

            var fileName = typeNamespace != null
                ? $"{typeNamespace}.{type.Name}Parser.g.cs"
                : $"{type.Name}Parser.g.cs";

            productionContext.AddSource(fileName, sourceCodeText);
        }
    }

    private static ModelToParse Convert(MatchingModel model, bool setToDefault = false)
    {
        var result = new ModelToParse()
        {
            Type = new TypeToParse()
            {
                DisplayName = model.Type.DisplayString,
            },
            Settables = model.Settables.Select(ParseSettable).ToList(),
            ComplexSettables = setToDefault
                ? []
                : model.Settables
                .Where(x => !x.IsPrimitive && !x.SetToDefault)
                .ToDictionary(
                    ParseSettable,
                    v => Convert(model.Inner![v.Type.DisplayString], v.SetToDefault)
                ),
        };

        foreach (var item in model.Settables)
        {
            if (!item.SetToDefault) continue;

            var settable = ParseSettable(item);

            result.ComplexSettables[settable] = new ModelToParse()
            {
                ComplexSettables = [],
                Settables = [],
                Type = new TypeToParse() { DisplayName = item.Type.DisplayString },
            };
        }

        return result;
    }

    private static SettableToParse ParseSettable(Settable settable)
    {
        return new SettableToParse()
        {
            FieldSource = settable.FieldSource,
            IsComplex = !settable.IsPrimitive,
            IsRequired = settable.Required,
            Name = settable.Name,
            TypeDisplayName = settable.Type.DisplayString,
            SetToDefault = settable.SetToDefault,
        };
    }

    public static IndentStackWriter ClearAndStartNew(StringBuilder sb)
    {
        sb.Clear();
        return new(sb);
    }

    internal static IndentedInterpolatedStringHandler AppendClass(IndentStackWriter _, MatchingModel model, MatchCase matchCase)
    {
        var type = model.Type.DisplayString;

        var sb = new StringBuilder();
        var modelToParse = Convert(model);

        var collected = SettableCrawlerEnumerator2.Collect(modelToParse);

        var wr = ClearAndStartNew(sb);
        SettableCrawlerEnumerator2.RenderParsing(collected, wr);
        var parsing = sb.ToString();
        
        wr = ClearAndStartNew(sb);
        SettableCrawlerEnumerator2.RenderCallIndexesReading(collected, wr);
        var readIndexesCall = sb.ToString();
        
        wr = ClearAndStartNew(sb);
        SettableCrawlerEnumerator2.RenderIndexesReadingMethod(collected, wr);
        var schemaIndexesReading = sb.ToString();

        wr = ClearAndStartNew(sb);
        SettableCrawlerEnumerator2.RenderIndexReadingMethod(collected, wr, matchCase);
        var indexReading = sb.ToString();

        return _.Scope[$$"""
            internal sealed partial class {{model.Type.Name}}Parser
            {
                {{_[AppendReadList(_, type, readIndexesCall, parsing)]}}

                {{_[AppendReadUnbuffered(_, type, readIndexesCall, parsing)]}}

                {{_[AppendReadListAsync(_, type, isValueTask: false, readIndexesCall, parsing)]}}

                {{_[AppendReadListAsync(_, type, isValueTask: true, readIndexesCall, parsing)]}}

                {{_[AppendReadUnbufferedAsync(_, type, readIndexesCall, parsing)]}}

                {{_.Scope[schemaIndexesReading]}}

                {{_.Scope[indexReading]}}
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadUnbuffered(IndentStackWriter _, string type, string readIndexesCall, string parsing)
    {
        return _.Scope[
            $$"""
            internal static IEnumerable<{{type}}> ReadUnbuffered<TReader>(TReader reader)
                where TReader : IDataReader
            {
                if(!reader.Read())
                {
                    yield break;
                }

                {{_.Scope[readIndexesCall]}}
            
                do
                {
                    {{_.Scope[parsing]}}

                    yield return parsed;
                } while(reader.Read());
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadList(IndentStackWriter _, string type, string readIndexesCall, string parsing)
    {
        return _.Scope[
            $$"""
            internal static List<{{type}}> ReadList<TReader>(TReader reader)
                where TReader : IDataReader
            {
                var result = new List<{{type}}>();

                if(!reader.Read())
                {
                    return result;
                }

                {{_.Scope[readIndexesCall]}}
            
                do
                {
                    {{_.Scope[parsing]}}

                    result.Add(parsed);
                } while(reader.Read());
            
                return result;
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadListAsync(IndentStackWriter _, string type, bool isValueTask, string readIndexesCall, string parsing)
    {
        return _.Scope[
            $$"""
            {{_[isValueTask
              ? _[$"internal static async ValueTask<List<{type}>> ReadListAsyncValue<TReader>(TReader reader, CancellationToken token = default)"]
              : _[$"internal static async Task<List<{type}>> ReadListAsync<TReader>(TReader reader, CancellationToken token = default)"]
            ]}}
                where TReader : DbDataReader
            {
                var result = new List<{{type}}>();

                if(!(await reader.ReadAsync(token).ConfigureAwait(false)))
                {
                    return result;
                }
            
                {{_.Scope[readIndexesCall]}}
            
                Task<bool> reading;

                while(true)
                {
                    {{_.Scope[parsing]}}

                    reading = reader.ReadAsync(token);

                    result.Add(parsed);

                    if(!(await reading.ConfigureAwait(false)))
                    {
                        break;
                    }
                }
            
                return result;
            }
            """];
    }

    internal static IndentedInterpolatedStringHandler AppendReadUnbufferedAsync(IndentStackWriter _, string type, string readIndexesCall, string parsing)
    {
        return _.Scope[
            $$"""
            internal static async IAsyncEnumerable<{{type}}> ReadUnbufferedAsync<TReader>(TReader reader, [EnumeratorCancellationAttribute] CancellationToken token = default)
                where TReader : DbDataReader
            {
                if(!(await reader.ReadAsync(token).ConfigureAwait(false)))
                {
                    yield break;
                }

                {{_.Scope[readIndexesCall]}}
            
                Task<bool> reading;

                while(true)
                {
                    {{_.Scope[parsing]}}

                    reading = reader.ReadAsync(token);

                    yield return parsed;

                    if(!(await reading.ConfigureAwait(false)))
                    {
                        break;
                    }
                }
            }
            """];
    }
}

internal struct DefferedCrawlingTarget
{
    public IEnumerator<(SettableToParse link, ModelToParse next)> Complex;
    public int Index;
}

internal readonly struct CrawlerSection2(
    int index,
    ModelToParse source,
    IEnumerator<(SettableToParse link, ModelToParse next)> requiredComplex,
    IEnumerator<(SettableToParse link, ModelToParse next)> optionalComplex,
    bool isRequired,
    int defferedFrom,
    int defferedCount,
    int undefferedCount,
    int iteratedDefferedsCount,
    bool traversedRequireds,
    bool traversedOptionals,
    bool traverseDeffered,
    SettableToParse parentLink,
    int parentIndex
)
{
    public ModelToParse Source => source;
    
    public IEnumerator<(SettableToParse link, ModelToParse next)> RequiredComplex => requiredComplex;

    public IEnumerator<(SettableToParse link, ModelToParse next)> OptionalComplex => optionalComplex;
    
    public int Index => index;
    
    public bool IsRequired => isRequired;

    public bool TraversedRequireds => traversedRequireds;

    public bool TraversedOptionals => traversedOptionals;

    public bool TraverseDeffered => traverseDeffered;

    public int ParentIndex => parentIndex;

    public int DefferedFrom => defferedFrom;

    public int DefferedCount => defferedCount;

    public int UndefferedCount => undefferedCount;

    public int IteratedDefferedsCount => iteratedDefferedsCount;

    public SettableToParse ParentLink => parentLink;
}

internal struct CrawlerSlice(
    ModelToParse source,
    int requiredSimpleIndex,
    int requiredSimpleCount,
    int notRequiredSimpleIndex,
    int notRequiredSimpleCount,
    int columnNameIndex,
    int columnNameLength,
    int accessIndex,
    int accessLenth,
    bool setToDefault = false
)
{
    public SettableToParse ParentLink { get; set; } = default!;

    public string TypeDisplayName { get; set; } = source.Type.DisplayName;

    public int ColumnNameIndex = columnNameIndex;
    public int ColumnNameLength = columnNameLength;
    public int AccessIndex = accessIndex;
    public int AccessLenth = accessLenth;

    public int ParentIndex { get; set; } = -1;
    public bool ParentIsRequired { get; set; }
    public bool IsRequired { get; set; }

    public int RequiredSimpleIndex { get; set; } = requiredSimpleIndex;
    public int RequiredSimpleCount { get; set; } = requiredSimpleCount;

    public readonly int AllRequiredSimpleCount => RequiredSimpleCount + ReqChildsReqSimplesCount;

    public int NotRequiredSimpleIndex { get; set; } = notRequiredSimpleIndex;
    public int NotRequiredSimpleCount { get; set; } = notRequiredSimpleCount;

    public readonly int AllNotRequiredSimpleCount => NotRequiredSimpleCount + NotReqChildsSimplesCount;

    public int ReqChildsReqSimplesCount { get; set; }
    public int NotReqChildsSimplesCount { get; set; }

    public int FirstChildIndex { get; set; } = -1;

    public int LastReqRecursiveChildIndex { get; set; } = -1;
    public int LastRecursiveChildIndex { get; set; } = -1;

    public int FirstRequiredChildIndex { get; set; } = -1;
    public int RequiredChildCount { get; set; }
    public int RequiredRecursiveChildCount { get; set; }

    public readonly int AllRequiredChildCount => RequiredChildCount + RequiredRecursiveChildCount;

    public int FirstOptionalChildeIndex { get; set; } = -1;
    public int OptionalChildCount { get; set; }
    public int OptionalRecursiveChildCount { get; set; }

    public readonly int AllOptionalChildCount => OptionalChildCount + OptionalRecursiveChildCount;

    public bool SetToDefault { get; set; } = setToDefault;
}

internal sealed class SettablesCollected(
    Memory<CrawlerSlice> slices,
    Memory<SettableToParse> requiredPrimitives,
    Memory<SettableToParse> notRequiredPrimitives,
    Memory<char> columnNamesPrefixes,
    Memory<char> accessPrefixes)
{
    public Memory<CrawlerSlice> Slices { get; set; } = slices;
    public Memory<SettableToParse> RequiredPrimitives { get; set; } = requiredPrimitives;
    public Memory<SettableToParse> OptionalPrimitives { get; set; } = notRequiredPrimitives;
    public Memory<char> ColumnNamesPrefixes { get; set; } = columnNamesPrefixes;
    public Memory<char> AccessPrefixes { get; set; } = accessPrefixes;
}

internal static class SettableCrawlerEnumerator2
{
    public static IEnumerator<(SettableToParse link, ModelToParse next)> EnumerateRequired(Dictionary<SettableToParse, ModelToParse> complexSettables)
    {
        var core = complexSettables.Select(static x => (link: x.Key, next: x.Value));
        return core.Where(static x => x.link.IsRequired).GetEnumerator();
    }

    public static IEnumerator<(SettableToParse link, ModelToParse next)> EnumerateOptional(Dictionary<SettableToParse, ModelToParse> complexSettables)
    {
        var core = complexSettables.Select(static x => (link: x.Key, next: x.Value));
        return core.Where(static x => !x.link.IsRequired).GetEnumerator();
    }

    public static SettablesCollected Collect(this ModelToParse root)
    {
        var path = new Stack<CrawlerSection2>();

        var deffered = new List<DefferedCrawlingTarget>();

        var slices = new List<CrawlerSlice>();

        var columnNameStack = new StringBuilder(256);
        var accessStack     = new StringBuilder(256);

        var columnNames = new List<char>(2048);
        var access      = new List<char>(1024);

        var allRequiredSimple    = new List<SettableToParse>();
        var allNotRequiredSimple = new List<SettableToParse>();

        var current = root;

        var requiredComplex = EnumerateRequired(current.ComplexSettables);
        var optionalComplex = EnumerateOptional(current.ComplexSettables);

        var isCurrentRequired = false;
        var parentLink = new SettableToParse() { FieldSource = default, IsComplex = true, IsRequired = false, Name = "", TypeDisplayName = default!, SetToDefault = false };

        var parentIndex     = -1;
        var defferedCount   = 0;
        var undefferedCount = 0;

        var traversedRequireds = false;
        var traversedOptionals = false;
        var traverseDeffered   = false;

        var defferedFrom = 0;
        var iteratedDefferedsCount = 0;

        while(true)
        {

            var reqSimpleCount = 0;
            var simpleCount = 0;

            var index = slices.Count;

            foreach (var settable in current.Settables)
            {
                if (!settable.IsComplex)
                {
                    settable.OwnerIndex = index;
                    (settable.IsRequired
                        ? allRequiredSimple
                        : allNotRequiredSimple
                    ).Add(settable);

                    (settable.IsRequired ? ref reqSimpleCount : ref simpleCount)++;
                }
            }

            var reqSimpleIndex = allRequiredSimple.Count - reqSimpleCount;
            var simpleIndex = allNotRequiredSimple.Count - simpleCount;

            var columnNameIndex = columnNames.Count;
            int columnNameLength;

            {
                var boilerplateLength = Append(columnNames, "col");

                if(parentIndex != -1)
                {
                    boilerplateLength += Append(columnNames, "Inner_");
                }

                //boilerplateLength += Append(columnNames, "_");

                columnNameLength = Append(columnNames, columnNameStack);

                if (columnNameLength != 0)
                {
                    columnNameLength += Append(columnNames, "__");
                }

                columnNameLength += boilerplateLength;
                columnNameLength += Append(columnNames, parentLink.Name);
            }

            var accessIndex = access.Count;
            int accessLength;

            {
                var boilerplateLength = Append(access, "parsed");

                if(parentIndex != -1)
                {
                    boilerplateLength += 1;
                    access.Add('.');
                }

                accessLength = Append(access, accessStack);

                if (accessLength != 0)
                {
                    accessLength += 1;
                    access.Add('.');
                }

                accessLength += boilerplateLength;
                accessLength += Append(access, parentLink.Name);
            }


            slices.Add(new(
                source: current,
                requiredSimpleIndex: reqSimpleIndex,
                requiredSimpleCount: reqSimpleCount,
                notRequiredSimpleIndex: simpleIndex,
                notRequiredSimpleCount: simpleCount,
                columnNameIndex: columnNameIndex,
                columnNameLength: columnNameLength,
                accessIndex: accessIndex,
                accessLenth: accessLength,
                setToDefault: parentLink.SetToDefault
            )
            {
                ParentIndex = parentIndex,
                IsRequired = isCurrentRequired,
                ParentLink = parentLink!,
            });

            while (true)
            {
                var traversedRequriedsBefore = traversedRequireds;

                if(!traversedRequireds)
                {
                    traversedRequireds = !requiredComplex!.MoveNext();
                }

                bool backToParent = isCurrentRequired && traversedRequireds != traversedRequriedsBefore;
                //Debug.Assert(!backToParent || (traversedRequriedsBefore == true && traversedRequireds == false), "Switching should happen when we ended with required complex settables");
                Debug.Assert(!backToParent || path.Count != 0, "backToParent means that we need to unroll, therefore 'path' shouldn't be empty");

                if (!traversedRequireds)
                {
                    var (link, next) = requiredComplex!.Current;

                    var destination = slices.AsSpan();

                    destination[index].RequiredChildCount += 1;

                    // TODO: think about algorithm. Maybe every node should have counter of deffered count
                    defferedCount += 1;

                    {
                        if(columnNameStack.Length != 0)
                        {
                            columnNameStack.Append("__");
                        }

                        columnNameStack.Append(parentLink.Name);

                        if(accessStack.Length != 0)
                        {
                            accessStack.Append('.');
                        }

                        accessStack.Append(parentLink.Name);
                    }

                    path.Push(new(
                        index: index,
                        source: current,
                        requiredComplex: requiredComplex,
                        optionalComplex: optionalComplex,
                        isRequired: isCurrentRequired,
                        traversedRequireds: traversedRequireds,
                        traversedOptionals: traversedOptionals,
                        traverseDeffered: traverseDeffered,
                        parentLink: parentLink!,
                        parentIndex: parentIndex,
                        defferedFrom: defferedFrom,
                        defferedCount: defferedCount,
                        iteratedDefferedsCount: iteratedDefferedsCount, // @Optional, could be setted just to 0
                        undefferedCount: undefferedCount
                    ));

                    current = next;
                    isCurrentRequired = true;
                    parentLink = link;

                    requiredComplex = EnumerateRequired(current.ComplexSettables);
                    optionalComplex = EnumerateOptional(current.ComplexSettables);

                    parentIndex = index;
                    iteratedDefferedsCount = 0;

                    // we need to keep them as they are
                    //undefferedCount = 0;
                    //defferedCount = 0;

                    traversedRequireds = false;
                    traverseDeffered = false;

                    deffered.Add(new() { Complex = optionalComplex, Index = slices.Count });
                }
                else if (
                    (isCurrentRequired || defferedCount == 0) // optionals should firstly end with deffereds and only then iterate through suboptionals
                    && (!backToParent || traverseDeffered)
                    && !(traversedOptionals = traversedOptionals || !optionalComplex.MoveNext())
                )
                {
                    Debug.Assert(traverseDeffered || !isCurrentRequired, "Only requrieds have deffereds");

                    var (link, next) = optionalComplex.Current;

                    // optionals that need to be setted to default don't need it implicitly in code therefore it can be ignored
                    if(link.SetToDefault)
                    {
                        continue;
                    }

                    //undefferedCount += isCurrentRequired ? 1 : 0;

                    var destination = slices.AsSpan();

                    destination[index].OptionalChildCount += 1;

                    {
                        if (columnNameStack.Length != 0)
                        {
                            columnNameStack.Append("__");
                        }

                        columnNameStack.Append(parentLink.Name);

                        if (accessStack.Length != 0)
                        {
                            accessStack.Append('.');
                        }

                        accessStack.Append(parentLink.Name);
                    }

                    path.Push(new(
                        index: index,
                        source: current!,
                        requiredComplex: requiredComplex!,
                        optionalComplex: optionalComplex,
                        isRequired: isCurrentRequired,
                        traversedRequireds: traversedRequireds,
                        traversedOptionals: traversedOptionals,
                        traverseDeffered: traverseDeffered,
                        parentLink: parentLink!,
                        parentIndex: parentIndex,
                        defferedFrom: defferedFrom,
                        defferedCount: defferedCount,
                        iteratedDefferedsCount: iteratedDefferedsCount,
                        undefferedCount: undefferedCount
                    ));

                    current = next;
                    isCurrentRequired = false;
                    parentLink = link;
                    
                    requiredComplex = EnumerateRequired(current.ComplexSettables);
                    optionalComplex = EnumerateOptional(current.ComplexSettables);

                    parentIndex = index;

                    defferedFrom = defferedFrom + defferedCount;

                    // if there's no deffereds, then we should start from 0, otherwise we can overwrite slice if we not move forward 1 step
                    //if (defferedCount != 0) defferedFrom += 1;

                    defferedCount = 0;
                    undefferedCount = 0;
                    iteratedDefferedsCount = 0;

                    traversedRequireds = false;
                    traversedOptionals = false;
                    traverseDeffered = false;
                }
                else if ((!isCurrentRequired || traverseDeffered) && defferedCount != 0 && defferedCount != undefferedCount)
                {
                    if (slices[index].RequiredChildCount == iteratedDefferedsCount)
                    {
                        var poped = path.Pop();

                        if (!poped.IsRequired && isCurrentRequired)
                        {
                            if (defferedCount != 0 && defferedCount == undefferedCount)
                            {
                                deffered.RemoveRange(index: defferedFrom, defferedCount);

                                undefferedCount = 0;
                                defferedCount = 0;
                                defferedFrom = 0;
                            }
                        }
                        else if (!isCurrentRequired)
                        {
                            undefferedCount = poped.UndefferedCount;
                            defferedCount = poped.DefferedCount;
                            defferedFrom = poped.DefferedFrom;
                        }

                        traverseDeffered = poped.TraverseDeffered; // if child is not required but parent is, it means that we processing "defereds"

                        index = poped.Index;
                        current = poped.Source;
                        requiredComplex = poped.RequiredComplex;
                        optionalComplex = poped.OptionalComplex;
                        isCurrentRequired = poped.IsRequired;
                        parentIndex = poped.ParentIndex;
                        parentLink = poped.ParentLink;

                        traversedRequireds = poped.TraversedRequireds;
                        traversedOptionals = poped.TraversedOptionals;

                        iteratedDefferedsCount = poped.IteratedDefferedsCount;

                        Remove(columnNameStack, parentLink.Name.Length, "__".Length);
                        Remove(accessStack, parentLink.Name.Length, ".".Length);

                        continue;
                    }

                    // -1 cause we incrementing undefferdCount first, so we would miss the first one at index 0 and iterate over wrong slices,
                    // that could even not relate to the current slice
                    var defferedSlice = deffered[defferedFrom + undefferedCount /*- 1*/];

#if !DEBUG
                    // helping GC to collect instance of IEnumerator
                    deffered[defferedFrom + undefferedCount /*- 1*/] = default;
#endif

                    // TODO: maybe add onto path
                    // The problem is that parent slices needs to be updated as child elements are added
                    // so the algorothm should take path back to this point through all parents,
                    // while we are referencing maybe deep nested node directly from relatively top-level node
                    Debug.Assert(defferedCount > 0);
                    Debug.Assert(deffered.Count != 0, "There's should be deffered slices");
                    //Debug.Assert(defferedCount <= deffered.Count, "There's can't be more deffered elements wait to unrol than in the buffer");
                    //Debug.Assert(isCurrentRequired == false, "Required are not responsible for dealing with deffereds");

                    //defferedCount -= 1;
                    undefferedCount += 1;

                    iteratedDefferedsCount += 1;

                    {
                        if (columnNameStack.Length != 0)
                        {
                            columnNameStack.Append("__");
                        }

                        columnNameStack.Append(parentLink.Name);

                        if (accessStack.Length != 0)
                        {
                            accessStack.Append('.');
                        }

                        accessStack.Append(parentLink.Name);
                    }

                    path.Push(new(
                        index: index,
                        source: current,
                        requiredComplex: default!, // deffered settables are traversed after the required, so at this point there's no need to traverse "requireds"
                        optionalComplex: optionalComplex,
                        isRequired: isCurrentRequired,
                        traversedRequireds: traversedRequireds,
                        traversedOptionals: traversedOptionals,
                        traverseDeffered: traverseDeffered,
                        parentIndex: parentIndex,
                        defferedFrom: defferedFrom,
                        defferedCount: defferedCount,
                        undefferedCount: undefferedCount,
                        iteratedDefferedsCount: iteratedDefferedsCount,
                        parentLink: parentLink!
                    ));

                    traversedRequireds = true;
                    traversedOptionals = false;
                    traverseDeffered = true;

                    iteratedDefferedsCount = 0;

                    requiredComplex = default; // we shouldn't enumerate requrieds as we already did it
                    optionalComplex = defferedSlice.Complex;
                    isCurrentRequired = true; // otherwise it wouldn't be in deffereds

                    current = default; // idk what the purpose of this

                    parentIndex = index; // the parent is the one who responsible for initiating deffereds processing
                    index = defferedSlice.Index;

                    Debug.Assert(slices[index].ParentIndex == parentIndex);

                    parentLink = slices[index].ParentLink; // it's probably doesn't do anything in that case at least

                    continue; // escaping from processing primitive settables as we already processed them
                }
                else if(path.Count != 0)
                {
                    var poped = path.Pop();

                    var span = slices.AsSpan();

                    if(isCurrentRequired && traverseDeffered)
                    {
                        goto RecoveringStack;
                    }

                    span[index].ParentIsRequired = poped.IsRequired;

                    // TODO: remove later
                    span[poped.Index].ReqChildsReqSimplesCount += (span[index].RequiredSimpleCount + span[index].ReqChildsReqSimplesCount) * (isCurrentRequired ? 1 : 0);
                    span[poped.Index].NotReqChildsSimplesCount += (span[index].NotRequiredSimpleCount + span[index].NotReqChildsSimplesCount) * (!isCurrentRequired ? 1 : 0);

                    // @Keep
                    span[poped.Index].RequiredRecursiveChildCount += (span[index].RequiredChildCount + span[index].RequiredRecursiveChildCount) * (isCurrentRequired ? 1 : 0);

                    span[poped.Index].OptionalRecursiveChildCount += (span[index].OptionalChildCount + span[index].OptionalRecursiveChildCount) * (isCurrentRequired ? 0 : 1);

                    if(span[index].IsRequired)
                    {
                        span[poped.Index].LastReqRecursiveChildIndex = span[index].LastReqRecursiveChildIndex >= 0
                            ? span[index].LastReqRecursiveChildIndex
                            : index;
                    }

                    span[poped.Index].LastRecursiveChildIndex = span[index].LastRecursiveChildIndex >= 0 ? span[index].LastRecursiveChildIndex : index;

                    if (isCurrentRequired && span[poped.Index].FirstRequiredChildIndex < 0)
                    {
                        span[poped.Index].FirstRequiredChildIndex = index;
                    }

                    if(!isCurrentRequired && span[poped.Index].FirstOptionalChildeIndex < 0)
                    {
                        span[poped.Index].FirstOptionalChildeIndex = index;
                    }

                    if(span[poped.Index].FirstChildIndex < 0)
                    {
                        span[poped.Index].FirstChildIndex = index;
                    }

                RecoveringStack:
                    if(!poped.IsRequired && isCurrentRequired)
                    {
                        if(defferedCount != 0 && defferedCount == undefferedCount)
                        {
                            deffered.RemoveRange(index: defferedFrom, defferedCount);

                            undefferedCount = 0;
                            defferedCount = 0;
                            defferedFrom = 0;
                        }
                    }
                    else if(!isCurrentRequired)
                    {
                        undefferedCount = poped.UndefferedCount;
                        defferedCount = poped.DefferedCount;
                        defferedFrom = poped.DefferedFrom;
                    }

                    index = poped.Index;
                    current = poped.Source;
                    requiredComplex = poped.RequiredComplex;
                    optionalComplex = poped.OptionalComplex;
                    isCurrentRequired = poped.IsRequired;
                    parentIndex = poped.ParentIndex;
                    parentLink = poped.ParentLink;

                    traverseDeffered = poped.TraverseDeffered;
                    traversedRequireds = poped.TraversedRequireds;
                    traversedOptionals = poped.TraversedOptionals;
                    iteratedDefferedsCount = poped.IteratedDefferedsCount;

                    Remove(columnNameStack, parentLink.Name.Length, "__".Length);
                    Remove(accessStack, parentLink.Name.Length, ".".Length);

                    continue;
                }
                else
                {
                    goto Exit;
                }

                break;
            }
        }

    Exit:
        Debug.Assert(slices.Count > 0);
        Debug.Assert(deffered.Count == 0);
        //Debug.Assert(slices[0].RequiredSimpleCount + slices[0].ReqChildsReqSimplesCount == allRequiredSimple.Count);
        //Debug.Assert(slices[0].NotRequiredSimpleCount + slices[0].NotReqChildsSimplesCount == allNotRequiredSimple.Count);
        //Debug.Assert(slices.Count == 1 || slices[0].FirstChildIndex == 1);

#if DEBUG
        for(int i = 0; slices.Count > 2 && i < slices.Count; i++)
        {
            var slice = slices[i];

            var requiredChildCount = 0;

            for(var r = i + 1; r <= i + slice.AllRequiredChildCount; r++)
            {
                var requiredSlice = slices[r];
                Debug.Assert(requiredSlice.IsRequired);

                if (slices[r].ParentIndex == i)
                    requiredChildCount++;
            }

            Debug.Assert(requiredChildCount == slice.RequiredChildCount);

            if (slice.FirstOptionalChildeIndex > 0)
            {
                //Debug.Assert(slice.FirstOptionalChildeIndex == i + slice.AllRequiredChildCount + 1);
                Debug.Assert(slices[slice.FirstOptionalChildeIndex].ParentIndex == i);
            }

            var optionalChildCount = 0;

            for(var o = slice.FirstOptionalChildeIndex; o > -1 && o < slices.Count; o++)
            {
                if(slices[o].ParentIndex == i && !slices[o].IsRequired)
                    optionalChildCount ++;
            }

            Debug.Assert(optionalChildCount == slice.OptionalChildCount);
        }

        for(int i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];

            if(!slice.IsRequired)
            {
                Debug.Assert(!slice.SetToDefault, "Optional recursive settables shouldn't be encountered");
            }

            if (!slice.SetToDefault) continue;

            var parentSlice = slices[slice.ParentIndex];
            Debug.Assert(parentSlice.FirstRequiredChildIndex <= i && i <= parentSlice.LastReqRecursiveChildIndex, "loosing link to defaulted settable");
            Debug.Assert(parentSlice.FirstChildIndex <= i, "loosing link to defaulted settable");
        }

        /*
        var columnNamesSpan = columnNames.AsSpan();
        var accessSpan = access.AsSpan();

        for (int i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            var columnName = columnNamesSpan.Slice(slice.ColumnNameIndex, slice.ColumnNameLength);
            var columnNameStr = columnName.ToString();

            var accessPath = accessSpan.Slice(slice.AccessIndex, slice.AccessLenth);
            var accessPathStr = accessPath.ToString();
        }
        */
#endif

        return new SettablesCollected(
            slices: slices.AsMemory(),
            requiredPrimitives: allRequiredSimple.AsMemory(),
            notRequiredPrimitives: allNotRequiredSimple.AsMemory(),
            columnNamesPrefixes: columnNames.AsMemory(),
            accessPrefixes: access.AsMemory()
        );
    }

    public static int Append(List<char> destination, StringBuilder source)
    {
        for(var i = 0; i < source.Length; i++)
            destination.Add(source[i]);

        return source.Length;
    }

    public static int Append(List<char> destination, string source)
    {
        for (var i = 0; i < source.Length; i++)
            destination.Add(source[i]);

        return source.Length;
    }

    public static void Remove(StringBuilder sb, int wordSize, int separatorLength)
    {
        var end = sb.Length - wordSize;

        wordSize += end != 0 ? separatorLength : 0;
        end -= end != 0 ? "__".Length : 0;

        sb.Remove(end, wordSize);
    }

    internal struct PrintingStep
    {
        public int RootIndex;
        public int Depth;

        public int PreviousIndex;
        public int PreviousParentIndex;
        public bool PreviousBraced;
        public bool PreviousHasRequired;
        public bool PreviousRequired;

        public bool RootHaveRequiredMembers;
        public bool RootHaveAnyMembers;
        public bool Inited;

        public int BracesOpened;
    }

    internal static void EndNestings(
        int nestings,
        IndentStackWriter w
    )
    {
        for(; nestings > 0; nestings--)
        {
            w.PopIndent();
            w.Append("\n}");
        }
    }

    internal static void EndStep(
        ref PrintingStep step,
        IndentStackWriter w
    )
    {
        if(!step.Inited)
        {
            return;
        }

        for (; step.BracesOpened > 0; step.BracesOpened -= 1)
        {
            w.Append(")");
        }

        w.Append("\n{\n\t");
        w.TryAddIndent();
    }

    internal static void PrintStep(
        ref PrintingStep step,
        int sliceIndex,
        int parentIndex,
        IndentStackWriter w,
        string settableName,
        //StringBuilder col,
        Span<char> colstr,
        bool containsRequired,
        bool isRequired
    )
    {
        if(!step.Inited)
        {
            if (!step.RootHaveAnyMembers)
            {
                return;
            }

            step.Inited = true;

            w.Append("if(");
            
            w.Append(colstr).Append("_").Append(settableName).Append(" != -1");

            step.BracesOpened += 1;
        }
        else if (parentIndex == step.PreviousParentIndex)
        {
            // previous -> (col1 != -1 && col2 != -1 ... colN != -1)
            // current -> (col3 != -1 && col4 != -1 ... colM != -1)
            // expect -> (col1 != -1 && col2 != -1 ... colN != -1) || (col3 != -1 && col4 != -1 ... colM != -1)
            var closeAndReopen =
                !step.RootHaveRequiredMembers
                && parentIndex == step.PreviousParentIndex
                && sliceIndex != step.PreviousIndex
                && step.PreviousHasRequired;

            var delimiter = containsRequired
                ? " && "
                : " || ";

            if(closeAndReopen)
            {
                delimiter = ") || (";
            }

            w.Append(delimiter);

            w.Append(colstr).Append("_").Append(settableName).Append(" != -1");
        }
        else if (parentIndex > step.PreviousParentIndex)
        {
            step.Depth += 1;
            step.PreviousParentIndex = parentIndex;

            // TODO: weird...
            var delimiter = step.RootHaveRequiredMembers && containsRequired
                ? " && "
                : " || ";

            w.Append(delimiter);

            if(!step.RootHaveRequiredMembers && containsRequired)
            {
                step.BracesOpened += 1;
                step.PreviousBraced = true;

                w.Append("(");
            }

            w.Append(colstr).Append("_").Append(settableName).Append(" != -1");
        }
        else if(parentIndex < step.PreviousParentIndex)
        {
            step.PreviousParentIndex = parentIndex;

            if (!step.RootHaveRequiredMembers && isRequired)
            {
                step.BracesOpened -= 1;
                w.Append(")");
            }
        }
        else
        {
            Debug.Assert(false, "All cases are handled");
        }

        step.PreviousIndex = sliceIndex;
        step.PreviousParentIndex = parentIndex;
        step.PreviousHasRequired = containsRequired;

        return;
    }

    internal struct ParseStep
    {
        public int Depth;
        public int PreviousParentIndex;
        public bool PreviousIsEmpty;
    }

    internal static void EndParseStep(
        int depth,
        IndentStackWriter w
    )
    {
        for (int i = 0; i < depth; i++)
        {
            w.PopIndent();
            w.Append("\n}");
        }
    }

    internal static void PrintParseStep(
        ref ParseStep step,
        ref Span<CrawlerSlice> slices,
        Span<SettableToParse> settables,
        ref CrawlerSlice current,
        int sliceIndex,
        int parentIndex,
        IndentStackWriter w,
        bool notFirstParentElement,
        bool hasAnyRequired,
        Span<char> colstr,
        string typeName,
        ReadOnlySpan<char> access,
        bool setToDefault
    )
    {
        if(step.Depth == 0)
        { }
        else if (false && step.PreviousParentIndex > parentIndex)
        {
            step.Depth -= 1;

            w.PopIndent();
            w.Append("\n}");
        }
        else if(false && step.PreviousParentIndex == parentIndex && !step.PreviousIsEmpty)
        {
            step.Depth -= 1;

            w.PopIndent();
            w.Append("\n}");
        }

        if (notFirstParentElement)
        {
            w.Append(",\n");
        }

        w.Append(access);

        if(!setToDefault)
        {
            w.Append(" = new ").Append(typeName.AsSpan().TrimEnd('?')).Append("()");
        }
        else
        {
            w.Append(" = default");
        }

        if (hasAnyRequired)
        {
            step.Depth += 1;

            w.Append("\n{\n\t")
                .TryAddIndent();
        }

        for (int i = 0; i < settables.Length; i++)
        {
            if(i != 0)
            {
                w.Append(",\n");
            }

            var settable = settables[i];
            var settableName = settable.Name;

            w.Append(settableName).Append(" = reader[");

            w.Append(colstr);
            w.Append("_");
            w.Append(settableName);

            w.Append("] is ").Append(settable.TypeDisplayName.AsSpan().TrimEnd('?'))
                .Append(" p").Append(colstr).Append("_").Append(settable.Name).Append(" ? p").Append(colstr).Append("_").Append(settable.Name).Append(" : default");
        }

        if(current.ParentIndex >= 0 && current.LastReqRecursiveChildIndex < 0)
        {
            if(settables.Length > 0)
            {
                step.Depth -= 1;
                Debug.Assert(step.Depth >= 0, "Closed more scope braces than opened");

                w.PopIndent();
                w.Append("\n}");
            }

            var ccurrent = slices[current.ParentIndex];

            // TODO:: Probably I need to set PreviousParentIndex to the last ParentIndex value after we loop this
            while(ccurrent.LastReqRecursiveChildIndex == sliceIndex && ccurrent.ParentIndex >= 0)
            {
                step.Depth -= 1;
                Debug.Assert(step.Depth >= 0, "Closed more scope braces than opened");

                w.PopIndent();
                w.Append("\n}");

                ccurrent = slices[ccurrent.ParentIndex];
            }
        }

        step.PreviousParentIndex = parentIndex;
        step.PreviousIsEmpty = !hasAnyRequired;
    }

    public static void RenderParsing(SettablesCollected collected, IndentStackWriter w, CancellationToken token = default)
    {
#if true
        var slices   = collected.Slices.Span;
        var required = collected.RequiredPrimitives.Span;
        var optional = collected.OptionalPrimitives.Span;

        var columnNames = collected.ColumnNamesPrefixes.Span;
        var accesses    = collected.AccessPrefixes.Span;

        int depth = 0;

        ref var first = ref slices[0];

        if(false && slices.Length > 100)
        {
            w.InternalStringBuilder.EnsureCapacity(180_000);
        }

        // Main looping
        for (int i = 0; i < slices.Length && !token.IsCancellationRequested; i++)
        {
            var root = slices[i];

            // Object that is returned from parsing should be created anyway
            if (i == 0) goto AfterCheck;

            // required parts of this model should be already parsed by loop after label "AfterCheck"
            if (root.IsRequired) goto ParsingOptionals;

            var checkingStopwatch = Stopwatch.StartNew();

            if(i != 0)
            {
                w.Append("\n\n");
            }

            PrintingStep step = default;

            step.Inited = false;
            step.PreviousParentIndex = root.ParentIndex;
            step.RootIndex = i;
            step.RootHaveRequiredMembers = root.AllRequiredSimpleCount > 0;
            step.RootHaveAnyMembers = root.AllRequiredSimpleCount > 0 || root.AllNotRequiredSimpleCount > 0;

            if(step.RootHaveAnyMembers)
            {
                depth += 1;
            }

            // Deciding whether to create or not an object: < - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
            //     - When the type contains any required member then we checking all required primitives                |
            //     - Otherwise whe checking any optional primitive members to have a value in data reader               |
            //         - If we encounter optional complex type then we apply that logic recursively - - - - - - - - - - -

            int endOfIteration;

            if (root.LastReqRecursiveChildIndex >= 0)
            {
                endOfIteration = root.LastReqRecursiveChildIndex;
            }
            else if(root.AllRequiredSimpleCount > 0)
            {
                endOfIteration = i;
            }
            else if(root.LastRecursiveChildIndex >= 0)
            {
                endOfIteration = root.LastRecursiveChildIndex;
            }
            else
            {
                endOfIteration = i;
            }

            for(var ch = i; ch <= endOfIteration; ++ch)
            {
                var child = slices[ch];

                if(child.AllRequiredSimpleCount > 0)
                {
                    var columnPref = columnNames.Slice(child.ColumnNameIndex, child.ColumnNameLength);

                    for(var q = child.RequiredSimpleIndex; q < child.RequiredSimpleIndex + child.RequiredSimpleCount; ++q)
                    {
                        PrintStep(ref step, sliceIndex: ch, parentIndex: child.ParentIndex, w, required[q].Name, columnPref, containsRequired: true, isRequired: true);
                    }

                    for(var r = child.FirstRequiredChildIndex; r < child.FirstRequiredChildIndex + child.AllRequiredChildCount; ++r)
                    {
                        var inner = slices[r];

                        columnPref = columnNames.Slice(inner.ColumnNameIndex, inner.ColumnNameLength);

                        for (var q = inner.RequiredSimpleIndex; q < inner.RequiredSimpleIndex + inner.RequiredSimpleCount; ++q)
                        {
                            PrintStep(ref step, sliceIndex: ch, parentIndex: ch, w, required[q].Name, columnPref, containsRequired: true, isRequired: true);
                        }
                    }

                    if (ch == i)
                    {
                        break; // for root only required primitives need to be ckecked
                    }
                }
                else if(child.AllNotRequiredSimpleCount > 0)
                {
                    var endIndex = child.NotRequiredSimpleIndex + child.NotRequiredSimpleCount;

                    var columnPref = columnNames.Slice(child.ColumnNameIndex, child.ColumnNameLength);

                    for (var q = child.NotRequiredSimpleIndex; q < endIndex; q++)
                    {
                        PrintStep(ref step, sliceIndex: ch, parentIndex: child.ParentIndex, w, optional[q].Name, columnPref, containsRequired: false, isRequired: false);
                    }
                }
            }

            EndStep(ref step, w);

        AfterCheck:

            if(root.IsRequired)
            {
                goto ParsingOptionals;
            }

            if(i == 0)
            {
                w.Append($"{root.TypeDisplayName} ");
            }

            var parseStep = default(ParseStep);
            parseStep.PreviousParentIndex = root.ParentIndex;

            {
                var colStr = columnNames.Slice(root.ColumnNameIndex, root.ColumnNameLength);
                var accessPref = accesses.Slice(root.AccessIndex, root.AccessLenth);

                PrintParseStep(
                    step: ref parseStep,
                    slices: ref slices,
                    settables: required.Slice(root.RequiredSimpleIndex, root.RequiredSimpleCount),
                    current: ref root,
                    sliceIndex: i,
                    parentIndex: root.ParentIndex,
                    w: w,
                    notFirstParentElement: false,
                    hasAnyRequired: root.AllRequiredSimpleCount > 0 || root.LastReqRecursiveChildIndex > 0,
                    colstr: colStr,
                    typeName: root.TypeDisplayName,
                    access: accessPref,
                    setToDefault: false
                );
            }

            for (var r = root.FirstRequiredChildIndex; r < root.FirstRequiredChildIndex + root.AllRequiredChildCount; ++r)
            {
                var reqChild = slices[r];

                var notFirstParentElement = false;

                if(i != 0)
                {
                    notFirstParentElement = reqChild.ParentIndex == i
                        ? root.RequiredSimpleCount > 0 || r != root.FirstRequiredChildIndex
                        : slices[reqChild.ParentIndex].RequiredSimpleCount > 0 || r != slices[reqChild.ParentIndex].FirstChildIndex;
                }
                else
                {
                    notFirstParentElement = slices[i].RequiredSimpleCount > 0 || slices[i].FirstRequiredChildIndex != r;
                }

                var colStr = columnNames.Slice(reqChild.ColumnNameIndex, reqChild.ColumnNameLength);

                PrintParseStep(
                    step: ref parseStep,
                    slices: ref slices,
                    settables: required.Slice(reqChild.RequiredSimpleIndex, reqChild.RequiredSimpleCount),
                    current: ref reqChild,
                    sliceIndex: r,
                    parentIndex: reqChild.ParentIndex,
                    w: w,
                    notFirstParentElement: notFirstParentElement,
                    hasAnyRequired: reqChild.AllRequiredSimpleCount > 0 || reqChild.LastReqRecursiveChildIndex > 0,
                    colstr: colStr,
                    typeName: reqChild.TypeDisplayName,
                    access: r == i ? accesses.Slice(root.AccessIndex, root.AccessLenth) : reqChild.ParentLink!.Name.AsSpan(),
                    setToDefault: reqChild.SetToDefault
                );
            }
            
            EndParseStep(parseStep.Depth, w);

            w.Append(";");

        ParsingOptionals:

            // parsing optional primitives of root
            {
                var colStr = columnNames.Slice(root.ColumnNameIndex, root.ColumnNameLength);
                var accessPref = accesses.Slice(root.AccessIndex, root.AccessLenth);

                for (var opt = root.NotRequiredSimpleIndex; opt < root.NotRequiredSimpleIndex + root.NotRequiredSimpleCount; opt++)
                {
                    var settable = optional[opt];

                    w.Append("\n\n");

                    w.Append("if(").Append(colStr).Append("_").Append(settable.Name).Append(" != -1)")
                    .Append("\n{\n\t")
                        .Append(accessPref).Append(".").Append(settable.Name).Append(" = reader[")
                        .Append(colStr).Append("_").Append(settable.Name).Append("] is ")
                        .Append(settable.TypeDisplayName.AsSpan().TrimEnd('?'))
                        .Append(" p").Append(colStr).Append("_").Append(settable.Name).Append(" ? p").Append(colStr).Append("_").Append(settable.Name).Append(" : default;")
                    .Append("\n}");
                }
            }

            // escaping if scopes
            {
                if(i != 0 && root.LastRecursiveChildIndex < 0)
                {
                    // it means we added if(...) { ...
                    if(true && // TODO
                        first.LastReqRecursiveChildIndex < i // we omitted if for the top lvl element
                        && !root.IsRequired
                        && root.AllRequiredSimpleCount + root.AllNotRequiredSimpleCount > 0)
                    {
                        w.PopIndent();
                        w.Append("\n}");

                        depth -= 1;
                    }

                    var scopeOwner = slices[root.ParentIndex];
                    
                    while (scopeOwner.ParentIndex >= 0 && scopeOwner.LastRecursiveChildIndex == i && !scopeOwner.IsRequired)
                    {
                        w.PopIndent();
                        w.Append("\n}");

                        depth -= 1;
                        Debug.Assert(depth >= 0);

                        scopeOwner = slices[scopeOwner.ParentIndex];
                    }
                }
            }
        }

        {
            for (; depth > 0; depth--)
            {
                w.PopIndent();
                w.Append("\n}");
            }
        }
#endif
    }

    public static void RenderCallIndexesReading(SettablesCollected collected, IndentStackWriter w)
    {
        w.Append("ReadSchemaIndexes(reader"); AppendColumnsSequential(collected, ", out int ".AsSpan(), [], w); w.Append(");");
    }

    public static void RenderIndexesReadingMethod(SettablesCollected collected, IndentStackWriter w)
    {
        /*
            public void ReadSchemaIndexes<TReader>(TReader reader, out int col1, out int col2, etc...)
                where TReader : IDataReader
            {
                col1 = -1;
                col2 = -1;
                etc...

                var fieldCount = reader.FieldCount;

                for(var i = 0; i < fieldCount; i++)
                {
                    ReadSchemaIndex(reader, i, ref col1, ref col2, etc...);
                }
            }
        */

        var required  = collected.RequiredPrimitives.Span;
        var optionals = collected.OptionalPrimitives.Span;
        var slices    = collected.Slices.Span;
        var columnPrefixes = collected.ColumnNamesPrefixes.Span;

        w.Append("public static void ReadSchemaIndexes<TReader>(TReader reader"); AppendColumnsSequential(collected, ", out int ".AsSpan(), [], w); w.Append(")\n");
        w.Append("    where TReader : IDataReader");
        w.Append("\n{");
        
        for (var sliceI = 0; sliceI < slices.Length; sliceI++)
        {
            var slice = slices[sliceI];

            var requiredEnd = slice.RequiredSimpleIndex + slice.RequiredSimpleCount;

            var columnPrefix = columnPrefixes.Slice(slice.ColumnNameIndex, slice.ColumnNameLength);

            for (var requiredI = slice.RequiredSimpleIndex; requiredI < requiredEnd; requiredI++)
            {
                var settable = required[requiredI];
                w.Append("\n\t").Append(columnPrefix).Append("_").Append(settable.Name).Append(" = ").Append(settable.FieldSource.TryGetOrder(out var constOrder) ? constOrder.ToString() : "-1").Append(";");
            }

            var optionalEnd = slice.NotRequiredSimpleIndex + slice.NotRequiredSimpleCount;

            for (int optionalI = slice.NotRequiredSimpleIndex; optionalI < optionalEnd; optionalI++)
            {
                var settable = optionals[optionalI];
                w.Append("\n\t").Append(columnPrefix).Append("_").Append(settable.Name).Append(" = ").Append(settable.FieldSource.TryGetOrder(out var constOrder) ? constOrder.ToString() : "-1").Append(";");
            }
        }

        w.Append("\n\n\tvar fieldCount = reader.FieldCount;");
        w.Append("\n\n\tfor(int i = 0; i < fieldCount; i++)");
        w.Append("\n\t{");
        w.Append("\n\t\tReadSchemaIndex(reader.GetName(i), i"); AppendColumnsSequential(collected, ", ref ".AsSpan(), [], w); w.Append(");");
        w.Append("\n\t}");
        w.Append("\n}");
    }

    public static void RenderIndexReadingMethod(SettablesCollected collected, IndentStackWriter w, MatchCase matching)
    {
        var required  = collected.RequiredPrimitives.Span;
        var optionals = collected.OptionalPrimitives.Span;
        var slices    = collected.Slices.Span;
        var columnPrefixes = collected.ColumnNamesPrefixes.Span;

        var groups = new SortedDictionary<int, List<(string variableName, string source)>>();

        var set = matching.Has(MatchCase.IgnoreCase) ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : [];

        for (var sliceI = 0; sliceI < slices.Length; sliceI++)
        {
            var slice = slices[sliceI];

            var requiredEnd = slice.RequiredSimpleIndex + slice.RequiredSimpleCount;

            var columnPrefix = columnPrefixes.Slice(slice.ColumnNameIndex, slice.ColumnNameLength);

            for (var requiredI = slice.RequiredSimpleIndex; requiredI < requiredEnd; requiredI++)
            {
                var settable = required[requiredI];

                if (!settable.FieldSource.TryGetFields(out var sourceFields))
                    continue;

                foreach(var sourceField in sourceFields)
                {
                    foreach (var sourceCase in sourceFields)
                    {
                        set.Add(sourceCase);
                    }
                }

                var variableName = set.Count != 0 ? Concat(columnPrefix, '_', settable.Name.AsSpan()) : default!;

                foreach(var sourceCase in set)
                {
                    (groups.TryGetValue(sourceCase.Length, out var group)
                        ? group
                        : groups[sourceCase.Length] = group = new(set.Count)
                    ).Add((variableName, sourceCase));
                }

                set.Clear();
            }

            var optionalEnd = slice.NotRequiredSimpleIndex + slice.NotRequiredSimpleCount;

            for (int optionalI = slice.NotRequiredSimpleIndex; optionalI < optionalEnd; optionalI++)
            {
                var settable = optionals[optionalI];

                if (!settable.FieldSource.TryGetFields(out var sourceFields))
                    continue;

                foreach (var sourceField in sourceFields)
                {
                    foreach (var sourceCase in sourceFields)
                    {
                        set.Add(sourceCase);
                    }
                }

                var variableName = set.Count != 0 ? Concat(columnPrefix, '_', settable.Name.AsSpan()) : default!;

                foreach (var sourceCase in set)
                {
                    (groups.TryGetValue(sourceCase.Length, out var group)
                        ? group
                        : groups[sourceCase.Length] = group = new(set.Count)
                    ).Add((variableName, sourceCase));
                }

                set.Clear();
            }
        }

        /*
            public static void ReadSchemaIndex(string name, int i, ref int col_1, ref int col_2, etc...)
                where TReader : IDataReader
            {
                switch(name.Length)
                {
                    case 5:
                        if(col_1 != -1 && "col_1".Equals(name))
                        {
                            col_1 = i;
                            break;
                        }

                        if(col_2 != -1 && "col_2".Equals(name))
                        {
                            col_2 = i;
                            break;
                        }
                        break;
                    case etc:
                        etc...
                        break;
                    default:
                        break;
                }
            }
        */

        w.Append("public static void ReadSchemaIndex(string name, int i"); AppendColumnsSequential(collected, ", ref int ".AsSpan(), [], w); w.Append(")");
        w.Append("\n{");
        w.Append("\n\tswitch(name.Length)");
        w.Append("\n\t{");

        var addedFirstCase = false;

        foreach(var groupKeyValue in groups)
        {
            var (caseLength, group) = (groupKeyValue.Key, groupKeyValue.Value);

            if(addedFirstCase)
            {
                w.Append("\n");
            }

            w.Append("\n\t\tcase ").Append(caseLength.ToString()).Append(":");

            for(int i = 0; i < group.Count; i++)
            {
                var (variableName, caseValue) = group[i];

                w.Append("\n\t\t\tif(").Append(variableName).Append(" == -1 && string.Equals(");

                if (caseValue == null)
                {
                    w.Append("null");
                }
                else
                {
                    w.Append("\"").Append(caseValue).Append("\"");
                }

                w.Append(", name");

                if(matching.Has(MatchCase.IgnoreCase))
                {
                    w.Append(", StringComparison.OrdinalIgnoreCase");
                }

                w.Append("))");
                w.Append("\n\t\t\t{");
                w.Append("\n\t\t\t\t").Append(variableName).Append(" = i;");

                if(i != group.Count - 1)
                {
                    w.Append("\n\t\t\t\t").Append("break;");
                }

                w.Append("\n\t\t\t}\n");
            }

            w.Append("\n\t\t\tbreak;");

            addedFirstCase = true;
        }

        if(addedFirstCase)
        {
            w.Append("\n");
        }

        w.Append("\n\t\tdefault:");
        w.Append("\n\t\t\tbreak;");

        w.Append("\n\t}");
        w.Append("\n}");

    }

    public static string Concat(Span<char> left, char middle, ReadOnlySpan<char> right)
    {
        Span<char> result = stackalloc char[left.Length + 1 + right.Length];
        
        left.CopyTo(result);
        
        result[left.Length] = middle;

        right.CopyTo(
            result.Slice(left.Length + 1, right.Length)
        );

        return result.ToString();
    }

    public static void AppendColumnsSequential(SettablesCollected collected, ReadOnlySpan<char> prefix, ReadOnlySpan<char> postfix, IndentStackWriter w)
    {
        var required  = collected.RequiredPrimitives.Span;
        var optionals = collected.OptionalPrimitives.Span;
        var slices    = collected.Slices.Span;
        var columnPrefixes = collected.ColumnNamesPrefixes.Span;

        for(var sliceI = 0; sliceI < slices.Length; sliceI++)
        {
            var slice = slices[sliceI];

            var requiredEnd = slice.RequiredSimpleIndex + slice.RequiredSimpleCount;

            var columnPrefix = columnPrefixes.Slice(slice.ColumnNameIndex, slice.ColumnNameLength);

            for(var requiredI = slice.RequiredSimpleIndex; requiredI < requiredEnd; requiredI++)
            {
                var settable = required[requiredI];
                w.Append(prefix).Append(columnPrefix).Append("_").Append(settable.Name).Append(postfix);
            }

            var optionalEnd = slice.NotRequiredSimpleIndex + slice.NotRequiredSimpleCount;

            for(int optionalI = slice.NotRequiredSimpleIndex; optionalI < optionalEnd; optionalI++)
            {
                var settable = optionals[optionalI];
                w.Append(prefix).Append(columnPrefix).Append("_").Append(settable.Name).Append(postfix);
            }
        }
    }
}

internal sealed class ModelToParse
{
    public required TypeToParse Type { get; set; }
    public required IEnumerable<SettableToParse> Settables { get; set; }
    public required Dictionary<SettableToParse, ModelToParse> ComplexSettables { get; set; }
}

internal sealed class SettableToParse
{
    public required bool IsRequired { get; set; }
    public required bool IsComplex { get; set; }
    public required string Name { get; set; }
    public required string TypeDisplayName { get; set; }
    public required FieldsOrOrder FieldSource { get; set; }
    public required bool SetToDefault { get; set; }
    public int OwnerIndex;
}

internal sealed class TypeToParse
{
    public required string DisplayName { get; set; }
}
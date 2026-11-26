using System;
using System.Collections.Generic;
using System.Linq;

namespace KScript.Runtime
{
    /// <summary>What <c>@inject()</c> asks for: a record of a model, and the name it goes by where it is taken from.</summary>
    public sealed class InjectRequest
    {
        internal InjectRequest(CallContext call, RecordType type, string name, bool named)
        {
            Call = call;
            Type = type;
            Name = name;
            Named = named;
        }

        public CallContext Call { get; }

        /// <summary>The model whose record is wanted.</summary>
        public RecordType Type { get; }

        /// <summary>The name to look under: what <c>from:</c> gives, else the input's name, else the model's name in snake_case.</summary>
        public string Name { get; }

        /// <summary>True when <c>from:</c> gave the name, so only that name will do.</summary>
        public bool Named { get; }

        /// <summary>The record with this key, or the model's not found error (a 404 in a route).</summary>
        public object Find(object key)
        {
            ((IRecordKind)Type.Tag).TryFind(Call, Type, key, out object record);
            return record;
        }
    }

    /// <summary>
    /// The one place <c>@inject()</c> is answered. As the default of an input, <c>post: Post = @inject()</c>, it reads
    /// the model from the input's type and the name from the input; on its own it is given the model,
    /// <c>@inject(Post)</c>. A package that has records to give adds a source with <see cref="AddSource"/>:
    /// KScript.Http gives the records of the route being answered.
    /// </summary>
    public static class Injection
    {
        /// <summary>A place records come from. False when it has nothing to do with this run (no request is being answered).</summary>
        public delegate bool Source(InjectRequest request, out object record);

        /// <summary>The variable <see cref="ShapeMapper"/> declares around an input's default, naming the input. No script can write it.</summary>
        internal const string InputVariable = "\u0001input";

        private static readonly List<Source> sources = new List<Source>();

        public static void AddSource(Source source)
        {
            lock (sources)
            {
                if (!sources.Contains(source)) sources.Add(source);
            }
        }

        /// <summary>The input a default is being worked out for, with the type that declares it.</summary>
        internal sealed record Input(RecordType Owner, FieldDefinition Field);

        /// <summary>The command's name, for the checks that read a call of it.</summary>
        public const string Command = "inject";

        public const string NeedsModel = "@inject() on its own needs the model to find: @inject(Post). Only as the default of an input, post: Post = @inject(), does it read the model from the input.";

        public static string TakesModel(string got) => $"@inject() takes a model, such as @inject(Post), but got {got}.";

        public static string NoModelNamed(string name) => $"@inject() takes a model, and there is no model named {name} here. Import the file that declares it.";

        public static string InputIsNotModel(string input, string type) => $"@inject() fills an input whose type is a model, and '{input}' is {type}.";

        public static string NotModel(string type) => $"@inject() finds the record of a model, and {type} isn't a model.";

        internal static object Inject(CallContext call)
        {
            Input input = call.Scope != null && call.Scope.TryGet(InputVariable, out object held) ? held as Input : null;
            object given = Values.Unwrap(call.Arg(0, "model"));
            string from = call.Has(1, "from") ? NameOf(call.Arg(1, "from")) : null;

            RecordType type;
            if (given is RecordType written) type = written;
            else if (given is BareWord word) throw call.Error(NoModelNamed(word.Name), "Argument");
            else if (given != null) throw call.Error(TakesModel(Values.Describe(given)), "Argument");
            else if (input == null) throw call.Error(NeedsModel, "Argument");
            else
            {
                TypeRef declared = input.Field.Type;
                type = declared is { IsSimple: true, IsBuiltIn: false } ? Shapes.Resolve(declared, input.Owner.Closure, input.Owner.Module) as RecordType : null;
                if (type == null) throw call.Error(InputIsNotModel(input.Field.Name, declared.ToString()), "Type");
            }
            if (type.Tag is not IRecordKind) throw call.Error(NotModel(type.Name), "Type");

            var request = new InjectRequest(call, type, from ?? input?.Field.Name ?? Conventions.ToSnakeCase(type.Name), from != null);
            Source[] all;
            lock (sources) all = sources.ToArray();
            foreach (Source source in all)
            {
                if (source(request, out object record)) return record;
            }
            throw call.Error(input != null
                ? $"{input.Owner.Name} needs '{input.Field.Name}': nothing was passed, and there is no route to take a {type.Name} from."
                : $"There is no route to take a {type.Name} from.", "Context");
        }

        private static string NameOf(object value) => Values.Unwrap(value) is BareWord word ? word.Name : Values.ToStr(value);
    }
}

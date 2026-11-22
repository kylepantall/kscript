using System;

namespace KScript.Metadata
{
    // The vocabulary that describes elements and commands. Packages (KScript.Core, KScript.Terminal...) describe
    // what they provide with these, and the language server reads the same descriptions, so the runtime and the
    // editor can't disagree.

    /// <summary>How an attribute's (or argument's) value is read.</summary>
    public enum AttributeKind
    {
        /// <summary>One expression: <c>test="$n lt 3"</c>.</summary>
        Expression,

        /// <summary>A comma separated list of expressions: <c>is="2, 3"</c>.</summary>
        ExpressionList,

        /// <summary>Text with <c>{ }</c> interpolation: <c>href="/people/{$p.id}"</c>.</summary>
        Template,

        /// <summary>A variable name that the element declares: <c>name="count"</c>.</summary>
        DeclareVariable,

        /// <summary>One or more variable names the element declares, comma separated: <c>each="key, value"</c>.</summary>
        DeclareVariables,

        /// <summary>A variable that must already exist: <c>&lt;set name="count"&gt;</c>.</summary>
        VariableReference,

        /// <summary>Function parameters: <c>params="who, greeting = 'Hello'"</c>.</summary>
        Parameters,

        /// <summary>The name of a function the element declares.</summary>
        DeclareFunction,

        /// <summary>The name of a <c>&lt;fn&gt;</c> function.</summary>
        FunctionReference,

        /// <summary>A comma separated list of function names.</summary>
        FunctionReferences,

        /// <summary>A file path relative to the document.</summary>
        Path,

        /// <summary>A CSS selector.</summary>
        Selector,

        /// <summary>One of a fixed set of words (see the attribute's options).</summary>
        Enum,

        /// <summary>yes/no (also true/false).</summary>
        Bool,

        /// <summary>Plain text.</summary>
        Text,

        /// <summary>Statements run when something happens, such as a click: <c>on_click="count += 1"</c>. <c>$event</c> describes it.</summary>
        Statements,

        /// <summary>A type: a built-in (see the attribute's options), an &lt;enum&gt; or a &lt;type&gt;, with <c>[]</c> for a list and <c>?</c> to allow null.</summary>
        TypeName,

        /// <summary>Types, comma separated or in brackets: <c>with="User, Auditable"</c>.</summary>
        TypeNames,

        /// <summary>The fields of a &lt;type&gt; on one line: <c>login: string, id?: int, tags: string[] = []</c>.</summary>
        Fields,

        /// <summary>An error type: a built-in such as Validation or one an &lt;error&gt; declares. Some attributes take a list: <c>[IO, Network]</c>.</summary>
        ErrorType,
    }

    /// <summary>Which parts of an element's text are used as written rather than interpolated.</summary>
    public enum TextLiterals
    {
        /// <summary>All of it is a template: <c>{ }</c> and <c>$name</c> insert values.</summary>
        None,

        /// <summary>A template, except LaTeX maths between <c>\(</c> and <c>\)</c> or <c>\[</c> and <c>\]</c>, whose braces are kept.</summary>
        Maths,

        /// <summary>None of it: the text is used exactly as written (LaTeX maths, source code).</summary>
        All,
    }

    /// <summary>What an element's content holds.</summary>
    public enum ContentKind
    {
        /// <summary>Nothing: the element is normally self-closing.</summary>
        Empty,

        /// <summary>KScript elements to run.</summary>
        Statements,

        /// <summary>Text with <c>{ }</c> and bare <c>$name</c> references.</summary>
        Template,

        /// <summary>A single expression.</summary>
        Expression,

        /// <summary>Assignment statements separated by <c>;</c>.</summary>
        AssignmentStatements,

        /// <summary>HTML to output, with KScript elements and <c>{ }</c> inside.</summary>
        Markup,
    }

    /// <summary>Where an element can be used.</summary>
    [Flags]
    public enum ElementContext
    {
        Script = 1,
        Page = 2,
        Everywhere = Script | Page,
    }
}

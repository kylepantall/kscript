namespace KScript.Language.Analysis
{
    /// <summary>
    /// Reads a shape written like a type, as packages describe what their commands return
    /// (<see cref="KScript.Metadata.CallableInfo.ReturnShape"/>): <c>string</c>, <c>email</c>, <c>string?</c>,
    /// <c>{ id: string, name: string }</c>, <c>list&lt;{ id: string }&gt;</c> or <c>{ id: string }[]</c>.
    /// </summary>
    public static class ShapeText
    {
        /// <summary>The shape <paramref name="text"/> describes, or null when it isn't written right.</summary>
        public static TypeShape Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            int at = 0;
            TypeShape shape = ReadType(text, ref at);
            SkipSpace(text, ref at);
            return at == text.Length ? shape : null;
        }

        private static TypeShape ReadType(string text, ref int at)
        {
            SkipSpace(text, ref at);
            if (at >= text.Length)
            {
                return null;
            }
            TypeShape shape;
            if (text[at] == '{')
            {
                at++;
                shape = new TypeShape(KType.Map);
                SkipSpace(text, ref at);
                while (at < text.Length && text[at] != '}')
                {
                    string name = ReadWord(text, ref at);
                    SkipSpace(text, ref at);
                    if (name.Length == 0 || at >= text.Length || text[at] != ':')
                    {
                        return null;
                    }
                    at++;
                    TypeShape field = ReadType(text, ref at);
                    if (field == null)
                    {
                        return null;
                    }
                    shape.Fields[name] = new FieldInfo(name, field, null);
                    SkipSpace(text, ref at);
                    if (at < text.Length && text[at] == ',')
                    {
                        at++;
                        SkipSpace(text, ref at);
                    }
                }
                if (at >= text.Length)
                {
                    return null;
                }
                at++;
            }
            else
            {
                string name = ReadWord(text, ref at);
                if (name.Length == 0)
                {
                    return null;
                }
                SkipSpace(text, ref at);
                if (name == KType.List && at < text.Length && text[at] == '<')
                {
                    at++;
                    TypeShape item = ReadType(text, ref at);
                    SkipSpace(text, ref at);
                    if (item == null || at >= text.Length || text[at] != '>')
                    {
                        return null;
                    }
                    at++;
                    shape = new TypeShape(KType.List) { Element = item };
                }
                else
                {
                    shape = name switch
                    {
                        "any" => new TypeShape(KType.Any),
                        "int" or "float" => new TypeShape(KType.Number),
                        _ => new TypeShape(name),
                    };
                }
            }
            SkipSpace(text, ref at);
            // string? may be null: the shape is the same.
            if (at < text.Length && text[at] == '?')
            {
                at++;
            }
            while (at + 1 < text.Length && text[at] == '[' && text[at + 1] == ']')
            {
                at += 2;
                shape = new TypeShape(KType.List) { Element = shape };
            }
            return shape;
        }

        private static string ReadWord(string text, ref int at)
        {
            int start = at;
            while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_'))
            {
                at++;
            }
            return text.Substring(start, at - start);
        }

        private static void SkipSpace(string text, ref int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }
    }
}

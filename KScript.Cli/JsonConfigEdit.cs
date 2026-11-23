using System;
using System.Text;
using System.Text.Json;
using KScript.Projects;

namespace KScript.Cli;

/// <summary>Edits one JSONC property while retaining comments and unrelated text verbatim.</summary>
internal static class JsonConfigEdit
{
    internal static string Quote(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    internal static string SetProperty(string json, string[] path, string value, Func<Exception> expectedObject = null)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        reader.Read();
        for (int depth = 0; depth < path.Length; depth++)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw expectedObject?.Invoke() ?? new ProjectConfigException("Expected an object in kscript.json.");
            int insert = (int)reader.BytesConsumed;
            reader.Read();
            bool empty = reader.TokenType == JsonTokenType.EndObject;
            while (reader.TokenType == JsonTokenType.PropertyName && !reader.ValueTextEquals(path[depth]))
            {
                reader.Read();
                reader.Skip();
                reader.Read();
            }
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                for (int i = path.Length - 1; i > depth; --i) value = "{ " + Quote(path[i]) + ": " + value + " }";
                string newline = json.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string property = newline + new string(' ', (depth + 1) * 2) + Quote(path[depth]) + ": " + value +
                    (empty ? newline + new string(' ', depth * 2) : ",");
                return Encoding.UTF8.GetString(bytes, 0, insert) + property + Encoding.UTF8.GetString(bytes, insert, bytes.Length - insert);
            }
            reader.Read();
            if (depth + 1 < path.Length) continue;
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            int end = (int)reader.BytesConsumed;
            return Encoding.UTF8.GetString(bytes, 0, start) + value + Encoding.UTF8.GetString(bytes, end, bytes.Length - end);
        }
        throw new InvalidOperationException();
    }
}

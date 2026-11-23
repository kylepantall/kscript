using System.Linq;

using KScript.Syntax;

namespace KScript.Pages
{
    /// <summary>The path shared by a model form and its named ModelUpdate declaration.</summary>
    public static class ModelFormPath
    {
        public static string For(string binding)
        {
            if (string.IsNullOrEmpty(binding) || !Lexer.IsIdentStart(binding[0]) || !binding.All(Lexer.IsIdentChar))
                throw new PageException("A model form binding name uses letters, numbers and underscores.");
            return "/_forms/" + binding;
        }
    }
}

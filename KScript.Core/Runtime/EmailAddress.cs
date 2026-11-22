using System.Globalization;

namespace KScript.Runtime
{
    /// <summary>
    /// An email address. Spaces at either end are trimmed and the domain is lower-cased; the part before the @ keeps
    /// its case. <c>.local</c> and <c>.domain</c> give its parts.
    /// </summary>
    public sealed class EmailAddress : ISemanticText
    {
        private const string LocalSymbols = "!#$%&'*+-/=?^_`{|}~.";

        private EmailAddress(string local, string domain)
        {
            Local = local;
            Domain = domain;
        }

        public string Local { get; }
        public string Domain { get; }
        public string Text => Local + "@" + Domain;
        public string TypeName => SemanticTypes.Email;

        public static bool TryParse(string text, out EmailAddress email, out string reason)
        {
            email = null;
            string s = (text ?? string.Empty).Trim();
            reason = Problem(s);
            if (reason != null) return false;
            int at = s.IndexOf('@');
            email = new EmailAddress(s.Substring(0, at), s.Substring(at + 1).ToLowerInvariant());
            return true;
        }

        private static string Problem(string s)
        {
            if (s.Length == 0) return "it is empty";
            if (s.Length > 254) return "it is longer than 254 characters";
            int at = s.IndexOf('@');
            if (at < 0) return "it has no @";
            if (s.IndexOf('@', at + 1) >= 0) return "it has more than one @";
            string local = s.Substring(0, at);
            string domain = s.Substring(at + 1);
            if (local.Length == 0) return "there's nothing before the @";
            if (domain.Length == 0) return "there's nothing after the @";
            if (local.Length > 64) return "the part before the @ is longer than 64 characters";
            char bad = local.FirstOrDefault(c => !char.IsLetterOrDigit(c) && LocalSymbols.IndexOf(c) < 0);
            if (bad != default) return $"'{bad}' can't be used before the @";
            if (local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")) return "the part before the @ can't start or end with a dot, or have two dots in a row";
            return DomainProblem(domain);
        }

        private static string DomainProblem(string domain)
        {
            string ascii;
            try
            {
                ascii = new IdnMapping().GetAscii(domain.ToLowerInvariant());
            }
            catch (ArgumentException)
            {
                return $"'{domain}' isn't a domain";
            }
            string[] labels = ascii.Split('.');
            if (labels.Length < 2) return "the domain needs a dot, as in example.com";
            foreach (string label in labels)
            {
                if (label.Length == 0) return "the domain has an empty part (two dots in a row, or a dot at either end)";
                if (label.Length > 63) return "a part of the domain is longer than 63 characters";
                if (label.StartsWith('-') || label.EndsWith('-')) return "a part of the domain starts or ends with -";
                if (!label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return $"'{domain}' isn't a domain";
            }
            string top = labels[^1];
            if (top.Length < 2 || !(top.All(char.IsAsciiLetter) || top.StartsWith("xn--", StringComparison.Ordinal))) return $"'{top}' isn't a top-level domain";
            return null;
        }

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        public string ToText() => Text;

        public override bool Equals(object obj) => obj is EmailAddress e && e.Text == Text;

        public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);

        public override string ToString() => Text;
    }
}

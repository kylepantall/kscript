using System.Security.Cryptography;
using System.Text;

namespace KScript.Commands
{
    class to_md5 : KScriptCommand
    {
        private readonly string value;

        public to_md5(string value) => this.value = value;

        public override string Calculate() => CalculateMD5Hash(value);

        public string CalculateMD5Hash(string input)
        {
            MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }

            return sb.ToString();
        }

        public override void Validate() => throw new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}

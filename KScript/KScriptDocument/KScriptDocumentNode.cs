using KScript.Document;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Document
{
    public class KScriptDocumentNode : IKScriptDocumentNode
    {
        private KScriptObject Value;

        public KScriptObject GetValue() => (Ignore ? Value : null);

        public void Run()
        {
            if (GetValue() != null) GetValue().Run();
        }

        public bool Ignore { get; set; } = false;

        public KScriptDocumentNode(KScriptObject obj) => Value = obj;
    }
}

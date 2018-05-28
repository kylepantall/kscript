using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class @ref : KScriptObject
    {
        public string directory { get; set; }
        public string @namespace { get; set; }

        public override bool Run()
        {
            IEnumerable<string> files = Directory.EnumerateFiles(directory, ".dll");
            files.ToList().ForEach(item => Assembly.LoadFile(item).GetTypes().Where(i => i.Namespace == @namespace).ToList().ForEach(x => ParentContainer.Parser.LoadedTypes.Add(x.Name, x)));
            return true;
        }

        public override string UsageInformation()
        {
            throw new NotImplementedException();
        }

        public override void Validate()
        {
            throw new NotImplementedException();
        }
    }
}

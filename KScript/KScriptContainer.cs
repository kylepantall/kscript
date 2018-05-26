using KScript.Arguments;
using KScript.Handlers;
using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript
{
    public class KScriptContainer
    {
        public KScriptStringHandler StringHandler { get; }

        public IDictionary<string, def> defs { get; set; }
        public List<@if> Ifs { get; set; }
        public List<echo> echos { get; set; }

        public KScriptProperties Properties { get; } = null;

        public KScriptContainer(KScriptProperties prop)
        {
            defs = new Dictionary<string, def>();
            Ifs = new List<@if>();
            echos = new List<echo>();
            Properties = prop;
            StringHandler = new KScriptStringHandler(this);
        }

        public def this[string id]
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(id)) return defs[id];
                else return null;
            }
            set { defs.Add(new KeyValuePair<string, def>(id, value)); }
        }
    }
}

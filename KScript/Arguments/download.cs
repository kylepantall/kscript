using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class download : KScriptObject
    {

        public string url { get; set; }
        public string destination { get; set; }
        public string share_progress { get; set; }

        public override void Run()
        {
            string URL = url, DEST = destination;

            url = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, url);
            destination = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, destination);

            System.Net.WebClient client = new System.Net.WebClient();
            client.DownloadFile(url, destination);
        }

        public override void Validate()
        {
            throw new KScriptNoValidationNeeded();
        }
    }
}

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
        /// <summary>
        /// The url of the file to download
        /// </summary>
        public string url { get; set; }

        /// <summary>
        /// The destination to save the downloaded file
        /// </summary>
        public string destination { get; set; }

        /// <summary>
        /// Share the download progress to the console
        /// </summary>
        public string share_progress { get; set; }

        public override bool Run()
        {
            string URL = url, DEST = destination;

            url = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, url);
            destination = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, destination);

            System.Net.WebClient client = new System.Net.WebClient();
            client.DownloadFile(HandleCommands(url), HandleCommands(destination));
            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded();
        public override string UsageInformation() => @"Used to download files from an online source to a location on the local machine.";
    }
}

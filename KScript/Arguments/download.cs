using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects]
    public class download : KScriptObject
    {
        /// <summary>
        /// The url of the file to download
        /// </summary>
        /// 
        [KScriptObjects.KScriptProperty("The url of the file to download.", true)]
        [KScriptObjects.KScriptExample(@"<download url=""http://website.com/file.exe"" />")]
        public string url { get; set; }

        /// <summary>
        /// The destination to save the downloaded file
        /// </summary>
        /// 
        [KScriptObjects.KScriptProperty("The destination where the download should be saved to", true)]
        [KScriptObjects.KScriptExample(@"<download destination=""C:\file.exe""/>")]
        public string destination { get; set; }

        /// <summary>
        /// Share the download progress to the console
        /// </summary>
        /// 
        [KScriptObjects.KScriptProperty("Share the download progress with the console. -- STILL IN DEVELOPMENT --", false)]
        [KScriptObjects.KScriptAcceptedOptions("yes", "no", "1", "0", "n", "y", "true", "false", "t", "f")]
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

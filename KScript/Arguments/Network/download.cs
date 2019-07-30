using KScript.KScriptObjects;
using System;

namespace KScript.Arguments
{
    [KScriptNoInnerObjects]
    public class download : KScriptObject
    {
        /// <summary>
        /// The url of the file to download
        /// </summary>
        /// 
        [KScriptProperty("The url of the file to download.", true)]
        [KScriptExample(@"<download url=""http://website.com/file.exe"" />")]
        public string url { get; set; }

        /// <summary>
        /// The destination to save the downloaded file
        /// </summary>
        /// 
        [KScriptProperty("The destination where the download should be saved to", true)]
        [KScriptExample(@"<download destination=""C:\file.exe""/>")]
        public string destination { get; set; }

        /// <summary>
        /// Share the download progress to the console
        /// </summary>
        /// 
        [KScriptProperty("Stores the download progress in the Object store. Must assign an ID. [-- STILL IN DEVELOPMENT --]", false)]
        [KScriptAcceptedOptions("yes", "no", "1", "0", "n", "y", "true", "false", "t", "f")]
        public string share_progress { get; set; }

        [KScriptProperty("Unique ID used by other KScript Objects to retrieve properties about this object.", false)]
        public string id { get; set; }

        public override bool Run()
        {
            string URL = url, DEST = destination;

            URL = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, url);
            DEST = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, destination);

            System.Net.WebClient client = new System.Net.WebClient();
            client.DownloadProgressChanged += Client_DownloadProgressChanged;
            Out("Ok");
            client.DownloadFileAsync(new Uri(HandleCommands(URL)), HandleCommands(DEST));
            return true;
        }

        private void Client_DownloadProgressChanged(object sender, System.Net.DownloadProgressChangedEventArgs e)
        {
            Out(e.ProgressPercentage.ToString() + "%\n");
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("destination", false));
            validator.AddValidator(new KScriptValidationObject("url", false, KScriptValidator.ExpectedInput.URL));
            validator.Validate(this);
        }

        public override string UsageInformation() => @"Used to download files from an online source to a location on the local machine.";
    }
}

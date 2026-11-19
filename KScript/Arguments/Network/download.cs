using KScript.Metadata;
using KScript.KScriptObjects;
using System;
using System.IO;
using System.Net.Http;

namespace KScript.Arguments
{
    [KScriptNoInnerObjects]
    [KScriptDoc("Downloads a file.")]
    [KScriptElement(KScriptContentKind.Empty)]
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

            URL = KScriptVariableHandler.ReturnFormattedVariables(KScript(), url);
            DEST = KScriptVariableHandler.ReturnFormattedVariables(KScript(), destination);

            // Download synchronously: the previous fire-and-forget WebClient download was lost when the script ended first.
            using (HttpClient client = new HttpClient())
            using (Stream source = client.GetStreamAsync(new Uri(HandleCommands(URL))).GetAwaiter().GetResult())
            using (FileStream target = File.Create(HandleCommands(DEST)))
            {
                source.CopyTo(target);
            }

            return true;
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(KScript());
            validator.AddValidator(new KScriptValidationObject("destination", false));
            validator.AddValidator(new KScriptValidationObject("url", false, KScriptValidator.ExpectedInput.URL));
            validator.Validate(this);
        }

        public override string UsageInformation() => @"Used to download files from an online source to a location on the local machine.";
    }
}

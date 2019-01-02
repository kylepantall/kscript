using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using KScript.Document;
using KScript.HTML;
using KScript.KScriptObjects;

namespace KScript.KScriptContainerObjects
{
    public class KScriptHtmlContainerObject : KScriptObject
    {
        [KScriptProperty("Used to define which method to call when a POST occurs.", true)]
        public string method { get; set; }

        [KScriptProperty("The ID is used to retrieve stored details for this object.", true)]
        public string id { get; set; }

        public KScriptHtmlContainerObject(string str) => Contents = str;

        public override bool Run()
        {
            ParentContainer.GetObjectStorageContainer().AddObjectFromUID(id, this);
            WebServer ws = new WebServer(HandleResponse, HandleContext, "http://localhost:8080/");
            ws.Run();

            ProcessStartInfo info = new ProcessStartInfo("http://localhost:8080/");
            Process.Start(info);
            return true;
        }

        public void HandleContext(HttpListenerContext context)
        {
            ParentContainer.ClearGlobalValues(id);

            ParentContainer.AddGlobalValue(id, "formmode", context.Request.HttpMethod);

            if (context.Request.HttpMethod == "POST")
            {
                var request = context.Request;
                if (!request.HasEntityBody)
                {
                    return;
                }
                using (System.IO.Stream body = request.InputStream)
                {
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(body, request.ContentEncoding))
                    {
                        Dictionary<string, string> obtained_values = FormDataHandler.GetFormData(reader.ReadToEnd());
                        obtained_values.ToList().ForEach(i => ParentContainer.AddGlobalValue(id, i.Key, WebUtility.UrlDecode(i.Value)));
                    }
                }
            }
        }

        public string HandleResponse(HttpListenerRequest req)
        {
            foreach (var key in req.QueryString.AllKeys)
            {
                string value = req.QueryString.Get(key);
                if (ParentContainer.GetGlobalValues(id).ContainsKey(key))
                {
                    ParentContainer.GetGlobalValues(id).Remove(key);
                }
                ParentContainer.GetGlobalValues(id).Add(key, value);
            }

            if (req.HttpMethod == "POST")
            {
                List<IKScriptDocumentNode> items = ParentContainer.GetObjectStorageContainer().GetMethodCalls(method);
                items.ForEach(i => i.Run(ParentContainer, null));
            }

            return HandleCommands(Contents);
        }

        public override string UsageInformation() => "Used to load HTML from KScript Object";

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("Contents", false));
            //validator.AddValidator(new KScriptValidationObject("method", false, ParentContainer.GetObjectStorageContainer().GetMethodNames()));
            validator.Validate(this);
        }
    }
}

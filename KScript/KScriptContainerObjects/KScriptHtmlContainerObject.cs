using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using KScript.Document;
using KScript.HTML;
using KScript.KScriptObjects;

namespace KScript.KScriptContainerObjects
{
    public class KScriptHtmlContainerObject : KScriptIDObject
    {
        [KScriptProperty("Used to define which method to call when a POST occurs.", true)]
        public string method { get; set; }

        [KScriptProperty("Used to determine which port to use.", false)]
        public string port { get; set; } = "8080";


        //To-Be Implemented
        [KScriptProperty("The directory to use when obtaining resource files such as .png etc.", false)]
        public string directory { get; set; }

        public KScriptHtmlContainerObject(string str) : base(str)
        {
            Contents = str;
            ValidationType = ValidationTypes.DURING_PARSING;
        }

        public override bool Run()
        {
            string localip = WebServer.GetLocalIPAddress();

            bool is_closed = false;
            using (TcpClient tcpClient = new TcpClient())
            {
                try
                {
                    tcpClient.Connect(localip, int.Parse(port));
                    is_closed = false;
                }
                catch
                {
                    is_closed = true;
                }
            }


            if (is_closed)
            {
                port = WebServer.FreeTcpPort();
            }

            string WebAddress = string.Format("http://{1}:{0}/", port, localip);

            WebServer ws = new WebServer(HandleResponse, HandleContext, WebAddress);
            ws.Run();

            Out(string.Format("Local Web Application is running on: {0}\n", WebAddress));

            ProcessStartInfo info = new ProcessStartInfo(WebAddress);
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
                items.ForEach(i => i.Run(ParentContainer, null, this));
            }

            return HandleCommands(Contents);
        }

        public override string UsageInformation() => "Used to load HTML from KScript Object";

        public override void Validate()
        {
            //KScriptValidator validator = new KScriptValidator(ParentContainer);
            //validator.AddValidator(new KScriptValidationObject("Contents", false));
            //validator.AddValidator(new KScriptValidationObject("id", false));
            //validator.AddValidator(new KScriptValidationObject("method", false, ParentContainer.GetObjectStorageContainer().GetMethodNames()));
            //validator.Validate(this);
        }
    }
}

﻿using KScript.Document;
using KScript.HTML;
using KScript.KScriptObjects;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;

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
            SetValidationType(ValidationTypes.DURING_PARSING);
            Contents = str;
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
            KScript().ClearGlobalValues(id);

            KScript().AddGlobalValue(id, "formmode", context.Request.HttpMethod);

            if (context.Request.HttpMethod == "POST")
            {
                HttpListenerRequest request = context.Request;
                if (!request.HasEntityBody)
                {
                    return;
                }

                using (System.IO.Stream body = request.InputStream)
                {
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(body, request.ContentEncoding))
                    {
                        Dictionary<string, string> obtained_values = FormDataHandler.GetFormData(reader.ReadToEnd());
                        obtained_values.ToList().ForEach(i => KScript().AddGlobalValue(id, i.Key, WebUtility.UrlDecode(i.Value)));
                    }
                }
            }
        }

        public string HandleResponse(HttpListenerRequest req)
        {
            foreach (string key in req.QueryString.AllKeys)
            {
                string value = req.QueryString.Get(key);
                if (KScript().GetGlobalValues(id).ContainsKey(key))
                {
                    KScript().GetGlobalValues(id).Remove(key);
                }
                KScript().GetGlobalValues(id).Add(key, value);
            }

            if (req.HttpMethod == "POST")
            {
                List<IKScriptDocumentNode> items = KScript().GetObjectStorageContainer().GetMethodCalls(method);
                items.ForEach(i => i.Run(KScript(), null, this));
            }

            return HandleCommands(Contents);
        }

        public override string UsageInformation() => "Used to load HTML from KScript Object";

        public override void Validate()
        {
            //KScriptValidator validator = new KScriptValidator(KScript());
            //validator.AddValidator(new KScriptValidationObject("Contents", false));
            //validator.AddValidator(new KScriptValidationObject("id", false));
            //validator.AddValidator(new KScriptValidationObject("method", false, ParentContainer.GetObjectStorageContainer().GetMethodNames()));
            //validator.Validate(this);
        }
    }
}

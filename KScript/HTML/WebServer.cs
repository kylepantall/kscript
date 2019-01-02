namespace KScript.HTML
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading;

    public class WebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<HttpListenerRequest, string> _responderMethod;
        private readonly Action<HttpListenerContext> _handleContext;

        public WebServer(string[] prefixes, Func<HttpListenerRequest, string> method, Action<HttpListenerContext> handle)
        {
            if (!HttpListener.IsSupported)
            {
                throw new NotSupportedException(
                        "Needs Windows XP SP2, Server 2003 or later.");
            }

            if (prefixes == null || prefixes.Length == 0)
            {
                throw new ArgumentException("prefixes");
            }

            prefixes.ToList().ForEach(s => _listener.Prefixes.Add(s));

            _responderMethod = method ?? throw new ArgumentException("method");
            _handleContext = handle;
            _listener.Start();
        }

        public WebServer(Func<HttpListenerRequest, string> method, Action<HttpListenerContext> handle, params string[] prefixes)
            : this(prefixes, method, handle) { }

        public void Run()
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                Console.WriteLine("[loadHtml ~ status] Running...");
                try
                {
                    while (_listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem((c) =>
                        {
                            var ctx = c as HttpListenerContext;
                            try
                            {
                                _handleContext(ctx);
                                string rstr = _responderMethod(ctx.Request);
                                byte[] buf = Encoding.UTF8.GetBytes(rstr);
                                ctx.Response.ContentLength64 = buf.Length;
                                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                            }
                            catch (Exception ex) { throw ex; }
                            finally
                            {
                                ctx.Response.OutputStream.Close();
                            }
                        }, _listener.GetContext());
                    }
                }
                catch { }
            });
        }

        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}

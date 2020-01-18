namespace KScript.HTML
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
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
                throw new NotSupportedException("Needs Windows XP SP2, Server 2003 or later.");
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

        /// <summary>
        /// Returns a free TCP port.
        /// </summary>
        /// <returns></returns>
        public static string FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port.ToString();
        }

        /// <summary>
        /// Returns the Local IP Address for this PC;
        /// </summary>
        /// <returns>localhost IP Address</returns>
        public static string GetLocalIPAddress()
        {
            string localIP;
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("127.0.0.1", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    localIP = endPoint.Address.ToString();
                }
            }
            catch
            {
                localIP = "localhost";
            }
            return localIP;
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
                            HttpListenerContext ctx = c as HttpListenerContext;
                            try
                            {
                                _handleContext(ctx);
                                string rstr = _responderMethod(ctx.Request);
                                byte[] buf = Encoding.UTF8.GetBytes(rstr);
                                ctx.Response.ContentLength64 = buf.Length;
                                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                            }
                            catch (Exception) { }
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

using System.Net.Http;
using System.Net.Http.Headers;

namespace KScript.Commands
{
    public class api : KScriptCommand
    {
        private readonly string api_url = "";

        public api(string url) => api_url = url;

        public override string Calculate()
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            HttpResponseMessage response = client.GetAsync(api_url).Result;
            if (response.IsSuccessStatusCode)
            {
                var reply = response.Content.ReadAsStringAsync().Result;
                return reply;
            }
            else
            {
                return NULL;
            }
        }

        public override void Validate() { }
    }
}

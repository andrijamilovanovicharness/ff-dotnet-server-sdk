using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using io.harness.cfsdk.client.api;
using Newtonsoft.Json.Linq;

namespace io.harness.cfsdk.client.connector
{
    public class EventSource : IService
    {
        private string url;
        private HttpClient httpClient;
        private StreamReader streamReader;
        private IUpdateCallback callback;

        public EventSource(HttpClient httpClient, string url, IUpdateCallback callback)
        {
            this.httpClient = httpClient;
            this.url = url;
            this.callback = callback;
        }

        public void Close()
        {
            Stop();
        }

        public void Start()
        {
            Task.Run(() => StartStreaming());
        }

        public void Stop()
        {
            this.streamReader.Close();
            this.streamReader.Dispose();
            this.streamReader = null;
        }

        private async Task StartStreaming()
        {
            try
            {
                using (this.streamReader = new StreamReader(await this.httpClient.GetStreamAsync(url)))
                {
                    this.callback.OnStreamConnected();

                    while (!streamReader.EndOfStream)
                    {
                        string message = await streamReader.ReadLineAsync();
                        if (!message.Contains("domain")) continue;


                        // parse message
                        JObject jsommessage = JObject.Parse("{" + message + "}");

                        Message msg = new Message();
                        msg.Domain = (string)jsommessage["data"]["domain"];
                        msg.Event = (string)jsommessage["data"]["event"];
                        msg.Identifier = (string)jsommessage["data"]["identifier"];
                        msg.Version = long.Parse((string)jsommessage["data"]["version"]);

                        this.callback.Update(msg);
                    }
                }
            }
            catch (Exception)
            {
                
            }
            finally
            {
                this.callback.OnStreamDisconnected();
            }

        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using io.harness.cfsdk.client.connector;
using io.harness.cfsdk.HarnessOpenAPIService;

namespace io.harness.cfsdk.client.api
{
    internal interface IPollCallback
    {
        void OnPollerReady();
    }
    internal interface IPollingProcessor
    {
        void Stop();
        void StartPolling();
        Task<bool> ReadyAsync();
    }

    internal class PollingProcessor : IPollingProcessor
    {
        private IConnector connector;
        private IRepository repository;
        private IPollCallback callback;
        private Timer pollTimer;
        private Config config;
        private bool isInitialized = false;
        private System.Threading.AutoResetEvent readyEvent;

        public PollingProcessor(IConnector connector, IRepository repository, Config config, IPollCallback callback)
        {
            this.callback = callback;
            this.repository = repository;
            this.connector = connector;
            this.config = config;
            this.readyEvent = new System.Threading.AutoResetEvent(false);
        }
        public async Task<bool> ReadyAsync()
        {
            return await Task.Run(() =>
            {
                this.readyEvent.WaitOne();
                return true;
            });
        }

        public void StartPolling()
        {
            pollTimer = new Timer(new TimerCallback(OnTimedEventAsync), null, 0, this.config.PollIntervalInMiliSeconds);
        }
        public void Stop()
        {
            pollTimer.Dispose();
            pollTimer = null;

        }
        private void ProcessFlags()
        {

            try
            {
                IEnumerable<FeatureConfig> flags = this.connector.GetFlags();
                foreach (FeatureConfig item in flags)
                {
                    repository.SetFlag(item.Feature, item);
                }

            }
            catch (CfClientException)
            {
                // TODO: handle error
            }
        }
        private void ProcessSegments()
        {

            try
            {
                IEnumerable<Segment> segments = this.connector.GetSegments();
                foreach (Segment item in segments)
                {
                    repository.SetSegment(item.Identifier, item);
                }
            }
            catch (CfClientException)
            {
                // TODO: Handle error
            }
        }
        private void OnTimedEventAsync(object source)
        {
            var tasks = new List<Task>();
            tasks.Add(Task.Run(() => ProcessFlags()));
            tasks.Add(Task.Run(() => ProcessSegments()));

            Task.WaitAll(tasks.ToArray());

            if (!isInitialized)
            {
                this.isInitialized = true;
                this.callback.OnPollerReady();
                this.readyEvent.Set();
            }
        }
    }
}

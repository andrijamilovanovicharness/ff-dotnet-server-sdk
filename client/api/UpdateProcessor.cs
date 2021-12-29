﻿using System;
using System.Threading;
using System.Threading.Tasks;
using io.harness.cfsdk.client.connector;
using io.harness.cfsdk.HarnessOpenAPIService;
using Newtonsoft.Json;
using Serilog;

namespace io.harness.cfsdk.client.api
{
    public interface IUpdateCallback
    {
        void Update(Message message);
        void OnStreamConnected();
        void OnStreamDisconnected();
    }
    internal interface IUpdateProcessor
    {
        void Start();
        void Stop();
        void Update(Message message);
    }
    internal class UpdateProcessor : IUpdateCallback, IUpdateProcessor
    {
        private IConnector connector;
        private IRepository repository;
        private IUpdateCallback callback;
        private int pollIntervalInSec;
        private Timer pollTimer;
        private IService service;
        private Config config;

        public UpdateProcessor(IConnector connector, IRepository repository, Config config, IUpdateCallback callback)
        {
            this.callback = callback;
            this.repository = repository;
            this.connector = connector;
            this.config = config;
            this.pollIntervalInSec = config.pollIntervalInSeconds;
        }

        public void Start()
        {
            if (config.streamEnabled)
            {
                this.service = connector.Stream(this);
                this.service.Start();
            }
        }
        public void Stop()
        {
            if (this.service != null)
            {
                this.service.Stop();
            }
        }

        public void Update(Message message)
        {
            //we got a message from server
            Task.Run(() => ProcessMessage(message));
        }
        public void OnStreamConnected()
        {
            this.callback.OnStreamConnected();
        }
        public void OnStreamDisconnected()
        {
            this.callback.OnStreamDisconnected();
            Stop();
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                Start();
            });
        }
        private void ProcessMessage(Message message)
        {
            if (message.Domain.Equals("flag"))
            {
                try
                {
                    if (message.Event.Equals("delete"))
                    {
                        this.repository.DeleteFlag(message.Identifier);
                    }
                    else if (message.Event.Equals("create") || message.Event.Equals("patch"))
                    {
                        FeatureConfig feature = this.connector.GetFlag(message.Identifier);
                        this.repository.SetFlag(message.Identifier, feature);
                    }
                }
                catch(Exception ex)
                {
                    Log.Error($"Error processing flag: {message.Identifier} event: {message.Event}.", ex);
                }
            }
            else if (message.Domain.Equals("target-segment"))
            {
                try
                {
                    if (message.Event.Equals("delete"))
                    {
                        this.repository.DeleteSegment(message.Identifier);
                    }
                    else if (message.Event.Equals("create") || message.Event.Equals("patch"))
                    {
                        Segment segment = this.connector.GetSegment(message.Identifier);
                        this.repository.SetSegment(message.Identifier, segment);
                    }
                }
                catch(Exception ex)
                {
                    Log.Error($"Error processing segment: {message.Identifier} event: {message.Event}.", ex);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using io.harness.cfsdk.client.api;
using io.harness.cfsdk.HarnessOpenAPIService;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace io.harness.cfsdk.client.connector
{
    internal class HarnessConnector : IConnector
    {
        private String token;
        private String environment;
        private String cluster;

        public HttpClient apiHttpClient { get; set; }
        public HttpClient metricHttpClient { get; set; }
        public HttpClient sseHttpClient { get; set; }

        private string apiKey;
        private Config config;
        public HarnessConnector(String apiKey, Config config)
        {
            this.config = config;
            this.apiKey = apiKey;

            this.apiHttpClient = new HttpClient();
            this.apiHttpClient.BaseAddress = new Uri(config.ConfigUrl);
            this.apiHttpClient.Timeout = TimeSpan.FromSeconds(config.ConnectionTimeout);

            this.metricHttpClient = new HttpClient();
            this.metricHttpClient.BaseAddress = new Uri(config.eventUrl);
            this.metricHttpClient.Timeout = TimeSpan.FromSeconds(config.ConnectionTimeout);

            this.sseHttpClient = new HttpClient();
            this.sseHttpClient.DefaultRequestHeaders.Add("API-Key", this.apiKey);
            this.sseHttpClient.DefaultRequestHeaders.Add("Accept", "text /event-stream");
            this.sseHttpClient.Timeout = Timeout.InfiniteTimeSpan;
        }
        public IEnumerable<FeatureConfig> GetFlags()
        {
            try
            {
                Client client = new Client(this.apiHttpClient);
                client.BaseUrl = this.config.ConfigUrl;
                IEnumerable<FeatureConfig> respFeatures = client.ClientEnvFeatureConfigsGetAsync(environment, cluster).GetAwaiter().GetResult();
                return respFeatures;
            }
            catch (ApiException ex)
            {
                // TODO: Reauthenticate
                throw new CfClientException(ex.Message);
            }
        }
        public IEnumerable<Segment> GetSegments()
        {
            try
            {
                Client client = new Client(this.apiHttpClient);
                client.BaseUrl = this.config.ConfigUrl;
                IEnumerable<Segment> respSegments = client.ClientEnvTargetSegmentsGetAsync(environment, cluster).GetAwaiter().GetResult();
                return respSegments;
            }
            catch (ApiException ex)
            {
                // TODO: Reauthenticate
                throw new CfClientException(ex.Message);
            }
        }
        public FeatureConfig GetFlag(string identifier)
        {
            try
            {
                Client client = new Client(this.apiHttpClient);
                client.BaseUrl = this.config.ConfigUrl;
                FeatureConfig feature = client.ClientEnvFeatureConfigsGetAsync(identifier, this.environment, this.cluster).GetAwaiter().GetResult();
                return feature;
            }
            catch (ApiException ex)
            {
                throw new CfClientException(ex.Message);
            }
        }
        public Segment GetSegment(string identifer)
        {
            try
            {
                Client client = new Client(this.apiHttpClient);
                client.BaseUrl = this.config.ConfigUrl;
                Segment segment = client.ClientEnvTargetSegmentsGetAsync(identifer, this.environment, this.cluster).GetAwaiter().GetResult();
                return segment;
            }
            catch (ApiException ex)
            {
                // TODO: Reauthenticate
                throw new CfClientException(ex.Message);
            }
        }
        public IService Stream(IUpdateCallback updater)
        {
            return new EventSource(this.sseHttpClient, updater);
        }
        public void PostMetrics(HarnessOpenMetricsAPIService.Metrics metrics)
        {
            try
            {
                DateTime startTime = DateTime.Now;
                HarnessOpenMetricsAPIService.Client client = new HarnessOpenMetricsAPIService.Client(this.metricHttpClient);

                client.MetricsAsync(environment, cluster, metrics).GetAwaiter().GetResult();

                DateTime endTime = DateTime.Now;
                if ((endTime - startTime).TotalMilliseconds > config.MetricsServiceAcceptableDuration)
                {
                    Log.Warning("Metrics service API duration=[{}]", (endTime - startTime));
                }
            }
            catch (ApiException apiException)
            {
                // TODO: handle exception
            }
        }
        public string Authenticate()
        {
            try
            {
                AuthenticationRequest authenticationRequest = new AuthenticationRequest();
                authenticationRequest.ApiKey = apiKey;
                authenticationRequest.Target = new Target2 { Identifier = "" };

                Client client = new Client(this.apiHttpClient);
                client.BaseUrl = this.config.ConfigUrl;

                AuthenticationResponse response = client.ClientAuthAsync(authenticationRequest).GetAwaiter().GetResult();
                this.token = response.AuthToken;

                var handler = new JwtSecurityTokenHandler();
                SecurityToken jsonToken = handler.ReadToken(this.token);
                JwtSecurityToken JWTToken = (JwtSecurityToken)jsonToken;

                this.environment = JWTToken.Payload["environment"].ToString();
                this.cluster = JWTToken.Payload["clusterIdentifier"].ToString();

                this.apiHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.token);
                this.metricHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.token);
                this.sseHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.token);

                this.sseHttpClient.BaseAddress = new Uri(this.config.ConfigUrl + "/stream?cluster=" + this.cluster);

                return this.token;

            }
            catch (ApiException apiException)
            {
                if (apiException.StatusCode == 401)
                {
                    string errorMsg = "Invalid apiKey " + apiKey + ". Serving default value. ";
                    Log.Error(errorMsg);
                    throw new CfClientException(errorMsg);
                }
                Log.Error("Failed to get auth token {}", apiException.Message);

                throw new CfClientException(apiException.Message);
            }
        }
    }
}

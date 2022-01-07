using io.harness.cfsdk.client.connector;
using io.harness.cfsdk.HarnessOpenAPIService;
using Serilog;
using System.Threading;
using System.Threading.Tasks;

namespace io.harness.cfsdk.client.api
{
    interface IAuthCallback
    {
        void OnAuthenticationSuccess();
    }
    interface IAuthService
    {
        void StartAuthentication();
        void Stop();
    }
    internal class AuthService : IAuthService
    {
        private IConnector connector;
        private Config config;
        private Timer authTimer;
        private IAuthCallback callback;

        public AuthService(IConnector connector, Config config, IAuthCallback callback)
        {
            this.connector = connector;
            this.config = config;
            this.callback = callback;
        }
        public void StartAuthentication()
        {
            // initiate authentication
            authTimer = new Timer(new TimerCallback(OnTimedEvent), null, 0, this.config.PollIntervalInMiliSeconds);
        }
        public void Stop()
        {
            authTimer.Dispose();
            authTimer = null;
        }
        private void OnTimedEvent(object source)
        {
            try
            {
                connector.Authenticate();
                callback.OnAuthenticationSuccess();
                Stop();
                Log.Information("Stopping authentication service");
            }
            catch
            {
                // Exception thrown on Authentication. Timer will retry authentication
                Log.Error($"Exception while authenticating, retry in {this.config.pollIntervalInSeconds}");
            }
        }
    }
}

using io.harness.cfsdk.client.api;
using io.harness.cfsdk.client.connector;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;




namespace TestA
{
    class Observer : IObserver<Event>
    {
        public void OnCompleted()
        {
     
        }

        public void OnError(Exception error)
        {
           
        }

        public void OnNext(Event value)
        {
            Console.WriteLine("Value Observed: " + value);
        }
    }
    class Program 
    {
        static async Task Main(string[] args)
        {
            Observer observe = new Observer();
            Config config;

            // Change this to your API_KEY :
            string API_KEY = "70d4d39e-e50f-4cee-99bf-6fd569138c74";

            //change to your flag id's
            string boolflagname = "andrija_test";

            var local = new LocalConnector("./local");

            config = Config.Builder()
                .SetAnalyticsEnabled()
                .SetStreamEnabled(true)
                .ConfigUrl("https://config.feature-flags.uat.harness.io/api/1.0")
                .EventUrl("https://event.feature-flags.uat.harness.io/api/1.0")
                .Build();

            //CfClient.Instance.Initialize(local);
            CfClient.Instance.Initialize(API_KEY, config);
            CfClient.Instance.Subscribe(observe);


            io.harness.cfsdk.client.dto.Target target =
                io.harness.cfsdk.client.dto.Target.builder()
                .Name("Andrija Test Target") //can change with your target name
                .Identifier("andrija_test_target") //can change with your target identifier
                .build();

            await CfClient.Instance.StartAsync();

            while (true)
            {

                Console.WriteLine("Bool Variation Calculation Command Start ============== " + boolflagname);
                bool result = CfClient.Instance.BoolVariation(boolflagname, target, false);
                Console.WriteLine("Bool Variation value ---->" + result);
                Console.WriteLine("Bool Variation Calculation Command Stop ---------------\n\n\n");

                Thread.Sleep(2000);
            }
        }
    }
}

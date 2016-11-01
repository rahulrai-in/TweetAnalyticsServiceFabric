namespace TweetAnalytics.Web.Controllers
{
    using System;
    using System.Fabric;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.ServiceFabric.Services.Client;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    using TweetAnalytics.Contracts;

    public class HomeController : Controller
    {
        #region Fields

        private readonly long defaultPartitionID = 0;

        private readonly Uri tweetServiceInstance =
            new Uri(FabricRuntime.GetActivationContext().ApplicationName + "/TweetService");

        #endregion

        #region Public Methods and Operators

        public IActionResult About()
        {
            this.ViewData["Message"] = "Your application description page.";
            return this.View();
        }

        public IActionResult Contact()
        {
            this.ViewData["Message"] = "Your contact page.";

            return this.View();
        }

        public IActionResult Error()
        {
            return this.View();
        }

        public IActionResult Index()
        {
            return this.View();
        }

        public async Task<IActionResult> SetSubject(string subject)
        {
            try
            {
                var tokenSource = new CancellationTokenSource();
                var servicePartitionResolver = ServicePartitionResolver.GetDefault();
                var httpClient = new HttpClient();
                var partition =
                    await
                        servicePartitionResolver.ResolveAsync(
                            this.tweetServiceInstance,
                            new ServicePartitionKey(this.defaultPartitionID),
                            tokenSource.Token);
                var ep = partition.GetEndpoint();
                var addresses = JObject.Parse(ep.Address);
                var primaryReplicaAddress = (string)addresses["Endpoints"].First;
                var primaryReplicaUriBuilder = new UriBuilder(primaryReplicaAddress)
                    {
                        Query = $"subject={subject}&operation=queue"
                    };
                var result = await httpClient.GetStringAsync(primaryReplicaUriBuilder.Uri);
                this.ViewBag.SearchTerm = result;
                return this.View();
            }
            catch (Exception e)
            {
                throw;
            }
        }

        public async Task<IActionResult> ViewSentiment()
        {
            try
            {
                var tokenSource = new CancellationTokenSource();
                var servicePartitionResolver = ServicePartitionResolver.GetDefault();
                var httpClient = new HttpClient();
                var partition =
                    await
                        servicePartitionResolver.ResolveAsync(
                            this.tweetServiceInstance,
                            new ServicePartitionKey(this.defaultPartitionID),
                            tokenSource.Token);
                var ep = partition.GetEndpoint();
                var addresses = JObject.Parse(ep.Address);
                var primaryReplicaAddress = (string)addresses["Endpoints"].First;
                var primaryReplicaUriBuilder = new UriBuilder(primaryReplicaAddress) { Query = $"operation=get" };
                var result = await httpClient.GetStringAsync(primaryReplicaUriBuilder.Uri);
                var score = JsonConvert.DeserializeObject<TweetScore>(result);
                this.ViewBag.Score = score.TweetSentimentAverageScore;
                this.ViewBag.TweetCount = score.TweetCount;
                return this.View();
            }
            catch (Exception e)
            {
                throw;
            }
        }

        #endregion
    }
}
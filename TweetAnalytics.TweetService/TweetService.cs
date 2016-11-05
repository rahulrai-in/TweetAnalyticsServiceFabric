using Newtonsoft.Json.Linq;

namespace TweetAnalytics.TweetService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using LinqToTwitter;

    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;

    using Newtonsoft.Json;

    using TweetAnalytics.Contracts;

    public class TweetService : StatefulService, ITweet
    {
        #region Fields

        private readonly StatefulServiceContext context;

        private CancellationToken cancellationToken;

        #endregion

        #region Constructors and Destructors

        public TweetService(StatefulServiceContext context)
            : base(context)
        {
            this.context = context;
        }

        #endregion

        #region Public Methods and Operators

        public async Task<TweetScore> GetAverageSentimentScore()
        {
            if (this.cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var tweetScore = new TweetScore();
            var scoreList = new List<KeyValuePair<string, decimal>>();
            var scoreDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, decimal>>("scoreDictionary");
            using (var tx = this.StateManager.CreateTransaction())
            {
                tweetScore.TweetCount = await scoreDictionary.GetCountAsync(tx);
                var enumerable = await scoreDictionary.CreateEnumerableAsync(tx);
                using (var e = enumerable.GetAsyncEnumerator())
                {
                    while (await e.MoveNextAsync(this.cancellationToken).ConfigureAwait(false))
                    {
                        scoreList.Add(e.Current);
                    }
                }

                tweetScore.TweetSentimentAverageScore = tweetScore.TweetCount == 0 ? 0 : scoreList.Average(x => x.Value);
            }

            return tweetScore;
        }

        public async Task SetTweetSubject(string subject)
        {
            if (this.cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                return;
            }

            using (var tx = this.StateManager.CreateTransaction())
            {
                var scoreDictionary =
                    await this.StateManager.GetOrAddAsync<IReliableDictionary<string, decimal>>("scoreDictionary");
                await scoreDictionary.ClearAsync();
                var topicQueue = await this.StateManager.GetOrAddAsync<IReliableQueue<string>>("topicQueue");
                while (topicQueue.TryDequeueAsync(tx).Result.HasValue)
                {
                }
                await topicQueue.EnqueueAsync(tx, subject);
                await tx.CommitAsync();
            }
        }

        #endregion

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[] { new ServiceReplicaListener(this.CreateInternalListener) };
        }

        protected override async Task RunAsync(CancellationToken token)
        {
            this.cancellationToken = token;
            Task.Factory.StartNew(this.CreateTweetMessages, this.cancellationToken);
            Task.Factory.StartNew(this.ConsumeTweetMessages, this.cancellationToken);
            this.cancellationToken.WaitHandle.WaitOne();
        }

        private void ConsumeTweetMessages()
        {
            var tweetQueue = this.StateManager.GetOrAddAsync<IReliableQueue<string>>("tweetQueue").Result;
            var scoreDictionary =
                this.StateManager.GetOrAddAsync<IReliableDictionary<string, decimal>>("scoreDictionary").Result;
            while (!this.cancellationToken.IsCancellationRequested)
            {
                using (var tx = this.StateManager.CreateTransaction())
                {
                    var message = tweetQueue.TryDequeueAsync(tx).Result;
                    if (message.HasValue)
                    {
                        var score = this.GetTweetSentiment(message.Value);
                        scoreDictionary.AddOrUpdateAsync(tx, message.Value, score, (key, value) => score);
                    }

                    tx.CommitAsync().Wait();
                }

                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        private ICommunicationListener CreateInternalListener(ServiceContext context)
        {
            var internalEndpoint = context.CodePackageActivationContext.GetEndpoint("ProcessingServiceEndpoint");
            var uriPrefix =
                $"{internalEndpoint.Protocol}://+:{internalEndpoint.Port}/{context.PartitionId}/{context.ReplicaOrInstanceId}-{Guid.NewGuid()}/";

            var nodeIP = FabricRuntime.GetNodeContext().IPAddressOrFQDN;

            var uriPublished = uriPrefix.Replace("+", nodeIP);
            return new HttpCommunicationListener(uriPrefix, uriPublished, this.ProcessInternalRequest);
        }

        private void CreateTweetMessages()
        {
            while (!this.cancellationToken.IsCancellationRequested)
            {
                var topicQueue = this.StateManager.GetOrAddAsync<IReliableQueue<string>>("topicQueue").Result;
                using (var tx = this.StateManager.CreateTransaction())
                {
                    var topic = topicQueue.TryDequeueAsync(tx).Result;
                    if (topic.HasValue)
                    {
                        var tweets = this.GetTweetsForSubject(topic.Value);
                        var tweetQueue = this.StateManager.GetOrAddAsync<IReliableQueue<string>>("tweetQueue").Result;
                        foreach (var tweet in tweets)
                        {
                            tweetQueue.EnqueueAsync(tx, tweet).Wait();
                        }
                    }

                    tx.CommitAsync().Wait();
                }

                Thread.Sleep(TimeSpan.FromSeconds(10));
           }
        }

        private decimal GetTweetSentiment(string message)
        {
            decimal score;

            var configurationPackage = context.CodePackageActivationContext.GetConfigurationPackageObject("Config");

            var amlaAccountKey =
                configurationPackage.Settings.Sections["UserSettings"].Parameters["AmlaAccountKey"].Value;

            var accountKey = amlaAccountKey;

            using (var httpClient = new HttpClient())
            {
                var inputTextEncoded = Uri.EscapeUriString(message);

                httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", accountKey);

                var uri = "https://westus.api.cognitive.microsoft.com/text/analytics/v2.0/sentiment";
                var byteData = Encoding.UTF8.GetBytes("{\"documents\": [{\"language\": \"en\",\"id\": \"1\",\"text\": \"" + inputTextEncoded + "\"}]}");

                HttpResponseMessage response;
                using (var content = new ByteArrayContent(byteData))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    var responseTask = httpClient.PostAsync(uri, content);
                    responseTask.Wait();

                    response = responseTask.Result;
                }

                var contentTask = response.Content.ReadAsStringAsync();
                var contentesult = contentTask.Result;

                if (!response.IsSuccessStatusCode)
                {
                    return -1;
                }

                dynamic sentimentResult = JObject.Parse(contentesult);
                score = (decimal)sentimentResult.documents[0].score;
            }

            return score;
        }

        private IEnumerable<string> GetTweetsForSubject(string topic)
        {
            var configurationPackage = this.context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            var accessToken = configurationPackage.Settings.Sections["UserSettings"].Parameters["AccessToken"].Value;
            var accessTokenSecret =
                configurationPackage.Settings.Sections["UserSettings"].Parameters["AccessTokenSecret"].Value;
            var consumerKey = configurationPackage.Settings.Sections["UserSettings"].Parameters["ConsumerKey"].Value;
            var consumerSecret =
                configurationPackage.Settings.Sections["UserSettings"].Parameters["ConsumerSecret"].Value;

            var authorizer = new SingleUserAuthorizer
                {
                    CredentialStore =
                        new SingleUserInMemoryCredentialStore
                            {
                                ConsumerKey = consumerKey,
                                ConsumerSecret = consumerSecret,
                                AccessToken = accessToken,
                                AccessTokenSecret = accessTokenSecret
                            }
                };
            var twitterContext = new TwitterContext(authorizer);
            var searchResults = Enumerable.SingleOrDefault(
                from search in twitterContext.Search
                where (search.Type == SearchType.Search) && (search.Query == topic) && (search.Count == 100)
                select search);
            if ((searchResults != null) && (searchResults.Statuses.Count > 0))
            {
                return searchResults.Statuses.Select(status => status.Text);
            }

            return Enumerable.Empty<string>();
        }

        private async Task ProcessInternalRequest(HttpListenerContext context, CancellationToken cancelRequest)
        {
            string output = string.Empty;
            try
            {
                var operation = context.Request.QueryString["operation"];
                if (operation == "queue")
                {
                    var subject = context.Request.QueryString["subject"];
                    await this.SetTweetSubject(subject);
                    output = $"Added {subject} to Queue";
                }
                else
                {
                    if (operation == "get")
                    {
                        output = JsonConvert.SerializeObject(await this.GetAverageSentimentScore());
                    }
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
            }

            using (var response = context.Response)
            {
                var outBytes = Encoding.UTF8.GetBytes(output);
                response.OutputStream.Write(outBytes, 0, outBytes.Length);
            }
        }
    }
}
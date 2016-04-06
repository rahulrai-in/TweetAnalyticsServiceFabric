using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;

namespace TweetAnalytics.Web.Controllers
{
    using System.Fabric;

    using Microsoft.ServiceFabric.Services.Remoting.Client;

    using TweetAnalytics.Contracts;

    public class HomeController : Controller
    {

        private long defaultPartitionID = 1;
        private Uri tweetServiceInstance = new Uri(FabricRuntime.GetActivationContext().ApplicationName + "/TweetService");
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult SetSubject(string subject)
        {
            var tweetContract = ServiceProxy.Create<ITweet>(this.defaultPartitionID, this.tweetServiceInstance);
            tweetContract.SetTweetSubject(subject).Wait();
            ViewBag.SearchTerm = subject;
            return View();
        }

        public IActionResult ViewSentiment()
        {
            var tweetContract = ServiceProxy.Create<ITweet>(this.defaultPartitionID, this.tweetServiceInstance);
            var score = tweetContract.GetAverageSentimentScore().Result;
            ViewBag.Score = score.TweetSentimentAverageScore;
            ViewBag.TweetCount = score.TweetCount;
            return View();
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}

namespace TweetAnalytics.Contracts
{
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Remoting;

    public interface ITweet : IService
    {
        Task<TweetScore> GetAverageSentimentScore();
        Task SetTweetSubject(string subject);
    }
}
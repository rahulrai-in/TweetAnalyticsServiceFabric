namespace TweetAnalytics.Contracts
{
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Remoting;

    public interface ITweet
    {
        Task<TweetScore> GetAverageSentimentScore();
        Task SetTweetSubject(string subject);
    }
}
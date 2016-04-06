using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TweetAnalytics.Contracts
{
   public class TweetScore
    {
        public long TweetCount { get; set; }
        public decimal TweetSentimentAverageScore { get; set; }
    }
}

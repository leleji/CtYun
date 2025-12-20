using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CtYun.Models
{
    internal class ChallengeData
    {
        [JsonPropertyName("challengeId")]
        public string ChallengeId { get; set; }

        [JsonPropertyName("challengeCode")]
        public string ChallengeCode { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CtYun
{

    public class LoginInfo
    {
        public string UserAccount { get; set; }

        [JsonPropertyName("bondedDevice")]
        public bool BondedDevice { get; set; }

        [JsonPropertyName("secretKey")]
        public string SecretKey { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("tenantId")]
        public int TenantId { get; set; }
    }
}

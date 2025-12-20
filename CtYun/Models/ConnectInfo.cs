using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CtYun
{
    public  class ConnectInfo
    {
        [JsonPropertyName("desktopInfo")]
        public DesktopInfo DesktopInfo { get; set; }
    }

    public class DesktopInfo
    {
        [JsonPropertyName("host")]
        public string Host { get; set; }

        [JsonPropertyName("port")]
        public string Port { get; set; }

        [JsonPropertyName("clinkLvsOutHost")]
        public string ClinkLvsOutHost { get; set; }

        [JsonPropertyName("caCert")]
        public string CaCert { get; set; }

        [JsonPropertyName("clientCert")]
        public string ClientCert { get; set; }

        [JsonPropertyName("clientKey")]
        public string ClientKey { get; set; }
    }
    public class ConnecMessage
    {
        public int type { get; set; }
        public int ssl { get; set; }
        public string host { get; set; }
        public string port { get; set; }
        public string ca { get; set; }
        public string cert { get; set; }
        public string key { get; set; }
        public string servername { get; set; }
    }



}

using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CtYun
{
    public  class ConnectInfo
    {
        public ConnectInfoData data { get; set; }
    }
    public  class ConnectInfoData
    {
        public DesktopInfo desktopInfo { get; set; }
    }
    public  class DesktopInfo
    {
        public string host { get; set; }
        public string port { get; set; }

        public string clinkLvsOutHost { get; set; }

        public string caCert { get; set; }
        public string clientCert { get; set; }
        public string clientKey { get; set; }

        public int desktopId { get; set; }
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CtYun.Models
{
    public class ClientInfo
    {
        [JsonPropertyName("desktopList")]
        public List<Desktop> DesktopList { get; set; }
    }


    public class Desktop
    {
        [JsonPropertyName("desktopId")]
        public string DesktopId { get; set; }

        [JsonPropertyName("desktopName")]
        public string DesktopName { get; set; }

        [JsonPropertyName("desktopCode")]
        public string DesktopCode { get; set; }

        public DesktopInfo DesktopInfo { get; set; }
    }
}

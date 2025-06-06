using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CtYun.Models
{
    public class ClientInfo
    {
        public ClientInfoData data { get; set; }
    }
    public class ClientInfoData()
    {
        public List<DesktopList> desktopList { get; set; }
    }

    public class DesktopList
    {
        public string desktopId { get; set; }
    }
}

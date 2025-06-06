using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CtYun
{
    [JsonSerializable(typeof(ConnectInfo))]
    [JsonSerializable(typeof(ConnecMessage))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}

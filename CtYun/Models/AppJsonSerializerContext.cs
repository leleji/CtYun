using CtYun.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CtYun
{
    //Aot编译需要
    [JsonSerializable(typeof(ClientInfo))]
    [JsonSerializable(typeof(ConnectInfo))]
    [JsonSerializable(typeof(ConnecMessage))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}

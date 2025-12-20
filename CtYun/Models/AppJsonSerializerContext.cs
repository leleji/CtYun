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
    [JsonSerializable(typeof(ConnecMessage))]

    [JsonSerializable(typeof(ResultBase<ChallengeData>))]
    [JsonSerializable(typeof(ResultBase<ClientInfo>))]
    [JsonSerializable(typeof(ResultBase<ConnectInfo>))]
    [JsonSerializable(typeof(ResultBase<bool>))]
    [JsonSerializable(typeof(ResultBase<LoginInfo>))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}

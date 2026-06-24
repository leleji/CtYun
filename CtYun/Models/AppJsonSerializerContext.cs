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
    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(AccountConfig))]
    [JsonSerializable(typeof(StatusResponse))]
    [JsonSerializable(typeof(AccountRuntimeState))]
    [JsonSerializable(typeof(DesktopRuntimeState))]
    [JsonSerializable(typeof(ServiceEvent))]
    [JsonSerializable(typeof(AccountActionResult))]
    [JsonSerializable(typeof(BindDeviceRequest))]
    [JsonSerializable(typeof(ApiMessage))]
    [JsonSerializable(typeof(AdminCredentialFile))]
    [JsonSerializable(typeof(AdminAuthStatusResponse))]
    [JsonSerializable(typeof(AdminLoginRequest))]
    [JsonSerializable(typeof(AdminChangePasswordRequest))]
    [JsonSerializable(typeof(EncryptedConfigFile))]
    [JsonSerializable(typeof(List<AccountRuntimeState>))]
    [JsonSerializable(typeof(List<ServiceEvent>))]

    [JsonSerializable(typeof(ResultBase<ChallengeData>))]
    [JsonSerializable(typeof(ResultBase<ClientInfo>))]
    [JsonSerializable(typeof(ResultBase<ConnectInfo>))]
    [JsonSerializable(typeof(ResultBase<bool>))]
    [JsonSerializable(typeof(ResultBase<LoginInfo>))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}

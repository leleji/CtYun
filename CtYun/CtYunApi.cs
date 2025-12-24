using CtYun.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace CtYun
{
    internal class CtYunApi
    {

        private const string orcUrl = "https://orc.1999111.xyz/ocr";

        private const string version = "103020001";

        private const string deviceType = "60";

        private string _deviceCode;

        private readonly HttpClient client;

        public LoginInfo LoginInfo { get; set; }
        public CtYunApi(string deviceCode)
        {
            _deviceCode = deviceCode;
            var handler = new HttpClientHandler();
            client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("ctg-devicetype", deviceType);
            client.DefaultRequestHeaders.Add("ctg-version", version);
            client.DefaultRequestHeaders.Add("ctg-devicecode", deviceCode);
            client.DefaultRequestHeaders.Add("referer", "https://pc.ctyun.cn/");

        }



        public async Task<bool> LoginAsync(string userphone, string password)
        {
            for (int i = 1; i < 4; i++)
            {
                var genChallengeData = await GetGenChallengeDataAsync();
                if (genChallengeData == null)
                {
                    continue;
                }
                var captchaCode = await GetCaptcha(await GetLoginCaptcha(userphone));
                if (string.IsNullOrEmpty(captchaCode))
                {
                    continue;
                }
                var collection = new List<KeyValuePair<string, string>>
                {
                    new("userAccount", userphone),
                    new ("password", ComputeSha256Hash(password + genChallengeData.ChallengeCode)),
                    new ("sha256Password", ComputeSha256Hash(ComputeSha256Hash(password) + genChallengeData.ChallengeCode)),
                    new ("challengeId", genChallengeData.ChallengeId),
                    new ("captchaCode", captchaCode)
                };
                AddCollection(collection);
                using FormUrlEncodedContent content = new FormUrlEncodedContent(collection);
                ResultBase<LoginInfo> result = await PostAsync("https://desk.ctyun.cn:8810/api/auth/client/login", content, AppJsonSerializerContext.Default.ResultBaseLoginInfo);
                if (result.Success)
                {
                    LoginInfo = result.Data;
                    return true;
                }
              
                Utility.WriteLine(ConsoleColor.Red, $"重试{i}, Login Error:{result.Msg}");
                if (result.Msg == "用户名或密码错误")
                {
                    return false;
                }
            }
            return false;
        }

        public async Task<bool> GetSmsCodeAsync(string userphone)
        {
            for (int i = 0; i < 3; i++)
            {
                var captchaCode = await GetCaptcha(await GetSmsCodeCaptcha());
                if (!string.IsNullOrEmpty(captchaCode))
                {
                    ResultBase<bool> result = await GetAsync("https://desk.ctyun.cn:8810/api/cdserv/client/device/getSmsCode?mobilePhone=" + userphone + "&captchaCode=" + captchaCode, AppJsonSerializerContext.Default.ResultBaseBoolean);
                    if (result.Success)
                    {
                        return true;
                    }
                    Utility.WriteLine(ConsoleColor.Red, $"重试{i}, GetSmsCode Error:{result.Msg}");
                }
            }
            return false;
        }

        public async Task<bool> BindingDeviceAsync(string verificationCode)
        {
            var result = await PostAsync($"https://desk.ctyun.cn:8810/api/cdserv/client/device/binding?verificationCode={verificationCode}&deviceName=Chrome%E6%B5%8F%E8%A7%88%E5%99%A8&deviceCode={_deviceCode}&deviceModel=Windows+NT+10.0%3B+Win64%3B+x64&sysVersion=Windows+NT+10.0%3B+Win64%3B+x64&appVersion=3.2.0&hostName=pc.ctyun.cn&deviceInfo=Win32", null, AppJsonSerializerContext.Default.ResultBaseBoolean);
            if (result.Success)
            {
                return true;
            }
            Utility.WriteLine(ConsoleColor.Red, "BindingDevice Error:" + result.Msg);
            return false;
        }

        private async Task<ChallengeData> GetGenChallengeDataAsync()
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var result = await PostAsync("https://desk.ctyun.cn:8810/api/auth/client/genChallengeData", content, AppJsonSerializerContext.Default.ResultBaseChallengeData);
            if (result.Success)
            {
                return result.Data;
            }
            Utility.WriteLine(ConsoleColor.Red, "GetGenChallengeDataAsync Error:" + result.Msg);
            return null;
        }

        private async Task<byte[]> GetLoginCaptcha(string userphone)
        {
            try
            {
                return await client.GetByteArrayAsync("https://desk.ctyun.cn:8810/api/auth/client/captcha?height=36&width=85&userInfo=" + userphone + "&mode=auto&_t=1749139280909");
            }
            catch (Exception ex)
            {
                Utility.WriteLine(ConsoleColor.Red, "登录验证码获取错误：" + ex.Message);
                return null;
            }
        }

        private async Task<byte[]> GetSmsCodeCaptcha()
        {
            try
            {
                return await GetByteAsync("https://desk.ctyun.cn:8810/api/auth/client/validateCode/captcha?width=120&height=40&_t=1766158569152");
            }
            catch (Exception ex)
            {
                Utility.WriteLine(ConsoleColor.Red, "短信验证码获取错误：" + ex.Message);
                return null;
            }
        }

        private async Task<string> GetCaptcha(byte[] img)
        {
            try
            {
                Utility.WriteLine(ConsoleColor.White, "正在识别验证码.");
                using var request = new HttpRequestMessage(HttpMethod.Post, orcUrl);
                using var content = new MultipartFormDataContent {
                {
                    new StringContent(Convert.ToBase64String(img)),
                    "image"
                } };
                request.Content = content;
                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadAsStringAsync();
                Utility.WriteLine(ConsoleColor.Green, "识别结果：" + result);
                using var doc = JsonDocument.Parse(result);
                return doc.RootElement.GetProperty("data").GetString();
            }
            catch (Exception ex) { 
            
                Utility.WriteLine(ConsoleColor.Red, "验证码识别错误：" + ex.Message);
                return "";
            }
        }

        public async Task<List<Desktop>> GetLlientListAsync()
        {
            try
            {
                using var content = new StringContent("{\"getCnt\":20,\"desktopTypes\":[\"1\",\"2001\",\"2002\",\"2003\"],\"sortType\":\"createTimeV1\"}", Encoding.UTF8, "application/json");
                return (await PostAsync("https://desk.ctyun.cn:8810/api/desktop/client/pageDesktop", content, AppJsonSerializerContext.Default.ResultBaseClientInfo)).Data.DesktopList;
            }
            catch (Exception ex)
            {
                Utility.WriteLine(ConsoleColor.Red, "获取设备信息错误。" + ex.Message);
                return null;
            }
        }

        public async Task<ResultBase<ConnectInfo>> ConnectAsync(string desktopId)
        {
            List<KeyValuePair<string, string>> collection =
            [
                new KeyValuePair<string, string>("objId", desktopId),
                new KeyValuePair<string, string>("objType", "0"),
                new KeyValuePair<string, string>("osType", "15"),
                new KeyValuePair<string, string>("deviceId", deviceType),
                new KeyValuePair<string, string>("vdCommand", ""),
                new KeyValuePair<string, string>("ipAddress", ""),
                new KeyValuePair<string, string>("macAddress", "")
            ];
            AddCollection(collection);
            using var content = new FormUrlEncodedContent(collection);
            return await PostAsync("https://desk.ctyun.cn:8810/api/desktop/client/connect", content, AppJsonSerializerContext.Default.ResultBaseConnectInfo);
        }

        private async Task<ResultBase<T>> GetAsync<T>(string url, JsonTypeInfo<ResultBase<T>> typeInfo)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplySignature(request);
                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync(typeInfo);
            }
            catch (Exception ex)
            {
                return new ResultBase<T>
                {
                    Code = -100,
                    Msg = ex.Message
                };
            }
        }

        private async Task<byte[]> GetByteAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplySignature(request);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        private async Task<ResultBase<T>> PostAsync<T>(string url, HttpContent content, JsonTypeInfo<ResultBase<T>> typeInfo)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                ApplySignature(request);
                request.Content = content;
                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync(typeInfo);
            }
            catch (Exception ex)
            {
                return new ResultBase<T>
                {
                    Code = -100,
                    Msg = ex.Message
                };
            }
        }

        private void ApplySignature(HttpRequestMessage request)
        {
            if (LoginInfo != null)
            {
                var timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
                request.Headers.Add("ctg-userid", LoginInfo.UserId.ToString());
                request.Headers.Add("ctg-tenantid", LoginInfo.TenantId.ToString());
                request.Headers.Add("ctg-timestamp", timestamp);
                request.Headers.Add("ctg-requestid", timestamp);
                var str = $"{deviceType}{timestamp}{LoginInfo.TenantId}{timestamp}{LoginInfo.UserId}{version}{LoginInfo.SecretKey}";
                request.Headers.Add("ctg-signaturestr", ComputeMD5(str));
            }
        }

        private void AddCollection(List<KeyValuePair<string, string>> collection)
        {
            collection.Add(new KeyValuePair<string, string>("deviceCode", _deviceCode));
            collection.Add(new KeyValuePair<string, string>("deviceName", "Chrome浏览器"));
            collection.Add(new KeyValuePair<string, string>("deviceType", deviceType));
            collection.Add(new KeyValuePair<string, string>("deviceModel", "Windows NT 10.0; Win64; x64"));
            collection.Add(new KeyValuePair<string, string>("appVersion", "3.2.0"));
            collection.Add(new KeyValuePair<string, string>("sysVersion", "Windows NT 10.0; Win64; x64"));
            collection.Add(new KeyValuePair<string, string>("clientVersion", version));
        }

        private static string ComputeMD5(string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = MD5.HashData(inputBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        private static string ComputeSha256Hash(string rawData)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(rawData);
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

    }
}

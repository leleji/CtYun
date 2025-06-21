using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CtYun
{
    internal class CtYunApi
    {

        private readonly string crcUrl = "https://orc.xiaoleji.pro/ocr";

        private readonly HttpClient client;
        private readonly LoginInfo loginInfo;

        public CtYunApi(LoginInfo loginInfo)
        {
            this.loginInfo = loginInfo;
            var handler =new HttpClientHandler();
            client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("ctg-devicetype", loginInfo.DeviceType);
            client.DefaultRequestHeaders.Add("ctg-version", loginInfo.Version);
            client.DefaultRequestHeaders.Add("ctg-devicecode", loginInfo.DeviceCode);
            client.DefaultRequestHeaders.Add("referer", "https://pc.ctyun.cn/");
           
        }



        public async Task<bool> LoginAsync()
        {
            var captchaCode = await GetCaptcha();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://desk.ctyun.cn:8810/api/auth/client/login");
            var collection = new List<KeyValuePair<string, string>>
            {
                new("userAccount", loginInfo.UserPhone),
                new("password", loginInfo.Password),
                new("sha256Password", loginInfo.Password),
                new("captchaCode", captchaCode)
            };
            AddCollection(collection);
            var content = new FormUrlEncodedContent(collection);
            request.Content = content;
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var result= await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            // 获取 data 对象
            if (root.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Object)
            {
                if (dataElement.TryGetProperty("secretKey", out JsonElement secretKeyElement) && secretKeyElement.ValueKind == JsonValueKind.String)
                {
                    loginInfo.SecretKey = secretKeyElement.GetString();
                }
                if (dataElement.TryGetProperty("userAccount", out JsonElement userAccountElement) && userAccountElement.ValueKind == JsonValueKind.String)
                {
                    loginInfo.UserAccount = userAccountElement.GetString();
                }

                if (dataElement.TryGetProperty("userId", out JsonElement userIdElement) && userIdElement.ValueKind == JsonValueKind.Number)
                {
                    loginInfo.UserId = userIdElement.GetInt32();
                }
                if (dataElement.TryGetProperty("tenantId", out JsonElement tenantIdElement) && tenantIdElement.ValueKind == JsonValueKind.Number)
                {
                    loginInfo.TenantId = tenantIdElement.GetInt32();
                }
                Console.WriteLine("登录成功.");
                return true;
            }
            else
            {
                Console.WriteLine(result);
            }
            return false;
        }



        private async Task<string> PostEncryptionAsync(string url, List<KeyValuePair<string, string>>  collection)
        {
            var timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("ctg-userid", loginInfo.UserId.ToString());
            request.Headers.Add("ctg-tenantid", loginInfo.TenantId.ToString());
            request.Headers.Add("ctg-timestamp", timestamp);
            request.Headers.Add("ctg-requestid", timestamp);
            var str = $"{loginInfo.DeviceType}{timestamp}{loginInfo.TenantId}{timestamp}{loginInfo.UserId}{loginInfo.Version}{loginInfo.SecretKey}";
            request.Headers.Add("ctg-signaturestr", ComputeMD5(str));
            var content = new FormUrlEncodedContent(collection);
            request.Content = content;
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        private async Task<string> GetEncryptionAsync(string url)
        {
            var timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("ctg-userid", loginInfo.UserId.ToString());
            request.Headers.Add("ctg-tenantid", loginInfo.TenantId.ToString());
            request.Headers.Add("ctg-timestamp", timestamp);
            request.Headers.Add("ctg-requestid", timestamp);
            var str = $"{loginInfo.DeviceType}{timestamp}{loginInfo.TenantId}{timestamp}{loginInfo.UserId}{loginInfo.Version}{loginInfo.SecretKey}";
            request.Headers.Add("ctg-signaturestr", ComputeMD5(str));
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        private async Task<string> GetCaptcha()
        {
            try
            {
                Console.WriteLine("正在识别验证码.");
                var img = await client.GetByteArrayAsync($"https://desk.ctyun.cn:8810/api/auth/client/captcha?height=36&width=85&userInfo={loginInfo.UserPhone}&mode=auto&_t=1749139280909");
                var cdfs = Convert.ToBase64String(img);
                var request = new HttpRequestMessage(HttpMethod.Post, crcUrl);
                var content = new MultipartFormDataContent
                {
                    { new StringContent(Convert.ToBase64String(img)), "image" }
                };
                request.Content = content;
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"识别结果：{result}");
                var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;
                return root.GetProperty("data").GetString();
            }
            catch (Exception ex)
            {
                Console.WriteLine("验证码获取识别错误："+ex.Message);
                return "";
            }


        }


        public async Task<string> GetLlientListAsync()
        {
            try
            {
                var result = await GetEncryptionAsync("https://desk.ctyun.cn:8810/api/desktop/client/list");
                var resultJson = JsonSerializer.Deserialize(result, AppJsonSerializerContext.Default.ClientInfo);
                return resultJson.data.desktopList[0].desktopId;
            }
            catch (Exception ex)
            {
                Console.WriteLine("获取设备信息错误。"+ex.Message);
                return null; 
            }
        }

        public async Task<string> ConnectAsync()
        {
            var collection = new List<KeyValuePair<string, string>>
            {
                new("objId",loginInfo.DesktopId),
                new("objType", "0"),
                new("osType", "15"),
                new("deviceId", "60"),
                new("vdCommand", ""),
                new("ipAddress", ""),
                new("macAddress", ""),
            };
            AddCollection(collection);
            return await PostEncryptionAsync("https://desk.ctyun.cn:8810/api/desktop/client/connect", collection);
        }

        private void AddCollection(List<KeyValuePair<string, string>> collection)
        {
            collection.Add(new("deviceCode", loginInfo.DeviceCode));
            collection.Add(new("deviceName", "Chrome浏览器"));
            collection.Add(new("deviceType", loginInfo.DeviceType));
            collection.Add(new("deviceModel", "Windows NT 10.0; Win64; x64"));
            collection.Add(new("appVersion", "2.7.0"));
            collection.Add(new("sysVersion", "Windows NT 10.0; Win64; x64"));
            collection.Add(new("clientVersion", loginInfo.Version));
        }
        private static string ComputeMD5(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                // 将字节数组转换为 32 位小写十六进制字符串
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2")); // x2 表示两位小写十六进制
                }

                // 如果你想要 16 位 MD5，可以取中间部分：
                // return sb.ToString().Substring(8, 16);

                return sb.ToString(); // 32位小写
            }
        }
    }
}

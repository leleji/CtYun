using CtYun;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

Console.WriteLine("1.0.3");
var connectText = "";

var t = new LoginInfo()
{
    DeviceType = "60",
    DeviceCode = $"web_{GenerateRandomString(32)}",
    Version = "1020700001"
};

if (File.Exists("connect.txt"))
{
    connectText = File.ReadAllText("connect.txt");
}
if (string.IsNullOrEmpty(connectText))
{
    if (IsRunningInContainer())
    {
        // Docker/Linux 环境：使用环境变量
        t.UserPhone = Environment.GetEnvironmentVariable("APP_USER");
        t.Password = ComputeSha256Hash(Environment.GetEnvironmentVariable("APP_PASSWORD"));
        if (string.IsNullOrEmpty(t.UserPhone) || string.IsNullOrEmpty(t.Password))
        {
            Console.WriteLine("错误：必须设置环境变量 APP_USER 和 APP_PASSWORD");
            return;
        }
    }
    else
    {
        // Windows 环境：交互式输入
        Console.Write("请输入账号：");
        t.UserPhone = Console.ReadLine();

        Console.Write("请输入密码：");
        t.Password = ComputeSha256Hash(ReadPassword()); // 隐藏密码输入
    }
    //重试三次
    for (int i = 0; i < 3; i++)
    {

        var cyApi = new CtYunApi(t);
        if (!await cyApi.LoginAsync())
        {
            continue;
        }
        t.DesktopId= await cyApi.GetLlientListAsync();
        connectText = await cyApi.ConnectAsync();
        File.WriteAllText("connect.txt", connectText);
    }
    if (string.IsNullOrEmpty(connectText))
    {
        Console.WriteLine("登录异常..");
        return;
    }
}
Console.WriteLine("connect信息：" + connectText);
byte[] message=null;
var wssHost = "";
try
{
    var connectJson = JsonSerializer.Deserialize(connectText, AppJsonSerializerContext.Default.ConnectInfo);
    var connectMessage = new ConnecMessage
    {
        type = 1,
        ssl = 1,
        host = connectJson.data.desktopInfo.clinkLvsOutHost.Split(":")[0],
        port = connectJson.data.desktopInfo.clinkLvsOutHost.Split(":")[1],
        ca = connectJson.data.desktopInfo.caCert,
        cert = connectJson.data.desktopInfo.clientCert,
        key = connectJson.data.desktopInfo.clientKey,
        servername = $"{connectJson.data.desktopInfo.host}:{connectJson.data.desktopInfo.port}"
    };
    wssHost = connectJson.data.desktopInfo.clinkLvsOutHost;
    message = JsonSerializer.SerializeToUtf8Bytes(connectMessage, AppJsonSerializerContext.Default.ConnecMessage);

}
catch (Exception ex)
{
    Console.WriteLine("connect数据校验错误"+ ex.Message);
    return;
}
while (true)
{

    var uri = new Uri($"wss://{wssHost}/clinkProxy/{t.DesktopId}/MAIN");
    using var client = new ClientWebSocket();
    // 添加 Header
    client.Options.SetRequestHeader("Origin", "https://pc.ctyun.cn");
    client.Options.SetRequestHeader("Pragma", "no-cache");
    client.Options.SetRequestHeader("Cache-Control", "no-cache");
    client.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36");
    client.Options.AddSubProtocol("binary"); // 与 Sec-WebSocket-Protocol 对应
    try
    {
        Console.WriteLine("连接服务器中...");
        await client.ConnectAsync(uri, CancellationToken.None);
        Console.WriteLine("连接成功!");
        //接收消息
        _ = Task.Run(() => ReceiveMessagesAsync(client, CancellationToken.None));

        Console.WriteLine("发送连接信息.");
        await client.SendAsync(message, WebSocketMessageType.Text, true, CancellationToken.None);
        

        await Task.Delay(500);
        await client.SendAsync(Convert.FromBase64String("UkVEUQIAAAACAAAAGgAAAAAAAAABAAEAAAABAAAAEgAAAAkAAAAECAAA"), WebSocketMessageType.Binary, true, CancellationToken.None);

        await Task.Delay(TimeSpan.FromMinutes(5));
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
    }
    catch (Exception ex)
    {
        Console.WriteLine("WebSocket error: " + ex.Message);
    }
    async Task ReceiveMessagesAsync(ClientWebSocket ws, CancellationToken token)
    {
        var recvBuffer = new byte[4096];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(recvBuffer), token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("服务器关闭..");
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                    break;
                }
                else
                {
                    byte[] extracted = new byte[result.Count];
                    Buffer.BlockCopy(recvBuffer, 0, extracted, 0, result.Count);
                    var hex = BitConverter.ToString(extracted).Replace("-", "");
                    if (hex.StartsWith("5245445102", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("收到保活校验消息: " + hex);
                        var e = new Encryption();
                        var data = e.Execute(extracted);
                        Console.WriteLine("发送保活消息.");
                        await client.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);
                        Console.WriteLine("发送保活消息成功。");
                    }
                    else {
                        if (hex.IndexOf("00000000") ==-1)
                        {
                            Console.WriteLine("收到消息: " + hex.Replace("000000000000", ""));
                        }
                    }

                    


                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error receiving message: " + ex.Message);
        }
    }


}

 static string ReadPassword()
{
    var password = new StringWriter();
    ConsoleKeyInfo key;

    do
    {
        key = Console.ReadKey(true);

        if (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Backspace)
        {
            password.Write(key.KeyChar);
            Console.Write("*");
        }
        else if (key.Key == ConsoleKey.Backspace && password.ToString().Length > 0)
        {
            password.GetStringBuilder().Remove(password.ToString().Length - 1, 1);
            Console.Write("\b \b"); // 删除一个字符
        }
    } while (key.Key != ConsoleKey.Enter);

    Console.WriteLine();
    return password.ToString();
}

// 判断是否运行在容器中（Linux）
static bool IsRunningInContainer()
{
    // 简单判断是否存在 /.dockerenv 文件（适用于大多数 Docker 容器）
    return File.Exists("/.dockerenv");
}

string GenerateRandomString(int length)
{
    string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    RandomNumberGenerator rng = RandomNumberGenerator.Create();
    var data = new byte[length];
    rng.GetBytes(data);

    var result = new char[length];
    for (int i = 0; i < length; i++)
    {
        result[i] = chars[data[i] % chars.Length];
    }

    return new string(result);
}
static string ComputeSha256Hash(string rawData)
{
    var bytes = Encoding.UTF8.GetBytes(rawData);
    // 创建 SHA256 实例
    using (var sha256 = SHA256.Create())
    {
        byte[] hash = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}

//ws登录的相关会把已登录t掉线
//var sendLoginInfo = new SendLoginInfo
//{
//    Type = 112,
//    Size = t.BufferSize()
//};
//sendLoginInfo.Data = new byte[sendLoginInfo.Size];
//t.ToBuffer(sendLoginInfo.Data);

//var by=new byte[sendLoginInfo.BufferSize()];
//sendLoginInfo.ToBuffer(by);
//string hex = BitConverter.ToString(by).Replace("-", " ");
using CtYun;
using CtYun.Models;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

Utility.WriteLine(ConsoleColor.Green, $"版本：v {Assembly.GetEntryAssembly().GetName().Version}");
string userphone;
string password;
string devicecode;
if (IsRunningInContainer() || Debugger.IsAttached)
{
    userphone = Environment.GetEnvironmentVariable("APP_USER");
    password = Environment.GetEnvironmentVariable("APP_PASSWORD");
    devicecode = Environment.GetEnvironmentVariable("DEVICECODE");
    if (string.IsNullOrEmpty(userphone) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(devicecode))
    {
        Utility.WriteLine(ConsoleColor.Red, "错误：必须设置环境变量 APP_USER ， APP_PASSWORD ，DEVICECODE");
        return;
    }
}
else
{
    if (!File.Exists("DeviceCode.txt"))
    {
        File.WriteAllText("DeviceCode.txt", "web_" + GenerateRandomString(32));
    }
    devicecode= File.ReadAllText("DeviceCode.txt");
    Utility.WriteLine(ConsoleColor.Yellow, "请输入账号：");
    userphone = Console.ReadLine();
    Utility.WriteLine(ConsoleColor.Yellow, "请输入密码：");
    password = ReadPassword();
}
var cyApi = new CtYunApi(devicecode);
if (!await cyApi.LoginAsync(userphone, password))
{
    return;
}
Utility.WriteLine(ConsoleColor.Green, "登录成功。");
if (!cyApi.LoginInfo.BondedDevice)
{
    if (!(await cyApi.GetSmsCodeAsync(userphone)))
    {
        return;
    }
    Utility.WriteLine(ConsoleColor.Yellow, "请输入短信验证码：");
    var verificationCode = Console.ReadLine();
    if (!(await cyApi.BindingDeviceAsync(verificationCode)))
    {
        return;
    }
    Utility.WriteLine(ConsoleColor.Green, "设备绑定成功。");
}
var desktopList = await cyApi.GetLlientListAsync();
Utility.WriteLine(ConsoleColor.Green, $"获取到 {desktopList.Count} 台电脑");
foreach (Desktop d in desktopList)
{
    var connectResult = await cyApi.ConnectAsync(d.DesktopId);
    if (connectResult.Success && connectResult.Data.DesktopInfo != null)
    {
        d.DesktopInfo = connectResult.Data.DesktopInfo;
        Utility.WriteLine(ConsoleColor.Green, d.DesktopCode + "：connect信息获取成功");
    }
    else
    {
        Utility.WriteLine(ConsoleColor.Red, d.DesktopCode + "：connect信息获取错误该机器将跳过保活,检查电脑是否开机," + connectResult.Msg);
    }
}
if (desktopList.Count((Desktop v) => v.DesktopInfo != null) == 0)
{
    Utility.WriteLine(ConsoleColor.Red, "connect连接信息获取错误，检查电脑是否开机");
    return;
}
Utility.WriteLine(ConsoleColor.Yellow, "日志如果显示[发送保活消息成功。]才算成功。");
foreach (Desktop d2 in desktopList)
{
    _ =Task.Run(async delegate
    {
        await Connec(d2);
    });
}
Console.Read();
static async Task Connec(Desktop desktop)
{
    byte[] message;
    try
    {
        ConnecMessage connectMessage = new ConnecMessage
        {
            type = 1,
            ssl = 1,
            host = desktop.DesktopInfo.ClinkLvsOutHost.Split(":")[0],
            port = desktop.DesktopInfo.ClinkLvsOutHost.Split(":")[1],
            ca = desktop.DesktopInfo.CaCert,
            cert = desktop.DesktopInfo.ClientCert,
            key = desktop.DesktopInfo.ClientKey,
            servername = desktop.DesktopInfo.Host + ":" + desktop.DesktopInfo.Port
        };
        message = JsonSerializer.SerializeToUtf8Bytes(connectMessage, AppJsonSerializerContext.Default.ConnecMessage);
    }
    catch (Exception ex)
    {
        Exception ex2 = ex;
        Utility.WriteLine(ConsoleColor.Red, desktop.DesktopCode + "=>connect数据校验错误" + ex2.Message);
        return;
    }
    while (true)
    {
        var uri = new Uri($"wss://{desktop.DesktopInfo.ClinkLvsOutHost}/clinkProxy/{desktop.DesktopId}/MAIN");
        using var client = new ClientWebSocket();
        // 添加 Header
        client.Options.SetRequestHeader("Origin", "https://pc.ctyun.cn");
        client.Options.SetRequestHeader("Pragma", "no-cache");
        client.Options.SetRequestHeader("Cache-Control", "no-cache");
        client.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36");
        client.Options.AddSubProtocol("binary"); // 与 Sec-WebSocket-Protocol 对应
        try
        {
            Utility.WriteLine(ConsoleColor.Green, desktop.DesktopCode + "=>连接服务器中...");
            await client.ConnectAsync(uri, CancellationToken.None);
            Utility.WriteLine(ConsoleColor.Green, desktop.DesktopCode + "=>连接成功!");
            //接收消息
            _ = Task.Run(() => ReceiveMessagesAsync(client, CancellationToken.None));

            Utility.WriteLine(ConsoleColor.Green, desktop.DesktopCode + "=>发送连接信息.");
            await client.SendAsync(message, WebSocketMessageType.Text, true, CancellationToken.None);


            await Task.Delay(500);
            await client.SendAsync(Convert.FromBase64String("UkVEUQIAAAACAAAAGgAAAAAAAAABAAEAAAABAAAAEgAAAAkAAAAECAAA"), WebSocketMessageType.Binary, true, CancellationToken.None);

            await Task.Delay(TimeSpan.FromMinutes(1));
            Utility.WriteLine(ConsoleColor.Green, desktop.DesktopCode + "=>准备关闭连接重新发送保活信息.");
            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
        catch (Exception ex)
        {
            Utility.WriteLine(ConsoleColor.Red, desktop.DesktopCode + "=>WebSocket error: " + ex.Message);
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
                        Utility.WriteLine(ConsoleColor.Red, desktop.DesktopCode + "=>服务器关闭..");
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
                            Utility.WriteLine(ConsoleColor.Green, desktop.DesktopCode + "=>收到保活校验消息: " + hex);
                            var e = new Encryption();
                            var data = e.Execute(extracted);
                            Utility.WriteLine(ConsoleColor.Green, desktop.DesktopCode + "=>发送保活消息.");
                            await client.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);
                            Utility.WriteLine(ConsoleColor.Yellow, desktop.DesktopCode + "=>发送保活消息成功。");
                        }
                        else
                        {
                            if (hex.IndexOf("00000000") == -1)
                            {
                                Utility.WriteLine(ConsoleColor.White, desktop.DesktopCode + "=>收到消息: " + hex.Replace("000000000000", ""));
                            }
                        }




                    }
                }
            }
            catch (Exception ex)
            {


            }
        }


    }
}
static string GenerateRandomString(int length)
{
    string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    RandomNumberGenerator rng = RandomNumberGenerator.Create();
    byte[] data = new byte[length];
    rng.GetBytes(data);
    char[] result = new char[length];
    for (int i = 0; i < length; i++)
    {
        result[i] = chars[data[i] % chars.Length];
    }
    return new string(result);
}
static bool IsRunningInContainer()
{
    return File.Exists("/.dockerenv");
}
static string ReadPassword()
{
    StringWriter password2 = new StringWriter();
    ConsoleKeyInfo key;
    do
    {
        key = Console.ReadKey(intercept: true);
        if (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Backspace)
        {
            password2.Write(key.KeyChar);
            Console.Write("*");
        }
        else if (key.Key == ConsoleKey.Backspace && password2.ToString().Length > 0)
        {
            password2.GetStringBuilder().Remove(password2.ToString().Length - 1, 1);
            Console.Write("\b \b");
        }
    }
    while (key.Key != ConsoleKey.Enter);
    Console.WriteLine();
    return password2.ToString();
}
	
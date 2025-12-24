using System;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using CtYun;
using CtYun.Models;


CancellationTokenSource GlobalCts = new CancellationTokenSource();


Utility.WriteLine(ConsoleColor.Green, $"版本：v {Assembly.GetEntryAssembly()?.GetName().Version}");

var (userPhone, password, deviceCode) = ResolveCredentials();
if (string.IsNullOrEmpty(userPhone)) return;

var cyApi = new CtYunApi(deviceCode);
if (!await PerformLoginSequence(cyApi, userPhone, password)) return;

var desktopList = await cyApi.GetLlientListAsync();
var activeDesktops = new List<Desktop>();
foreach (var d in desktopList)
{
    var connectResult = await cyApi.ConnectAsync(d.DesktopId);
    if (connectResult.Success && connectResult.Data.DesktopInfo != null)
    {
        d.DesktopInfo = connectResult.Data.DesktopInfo;
        activeDesktops.Add(d);
    }
}

if (activeDesktops.Count == 0) return;

Utility.WriteLine(ConsoleColor.Yellow, "保活任务启动：每 60 秒强制重连一次。");

// 为每台设备开启独立的保活任务
var tasks = activeDesktops.Select(d => KeepAliveWorkerWithForcedReset(d, GlobalCts.Token));

Console.CancelKeyPress += (s, e) => { e.Cancel = true; GlobalCts.Cancel(); };

try { await Task.WhenAll(tasks); }
catch (OperationCanceledException) { Utility.WriteLine(ConsoleColor.Yellow, "程序已停止。"); }


static async Task KeepAliveWorkerWithForcedReset(Desktop desktop, CancellationToken globalToken)
{
    const string InitialPayloadBase64 = "UkVEUQIAAAACAAAAGgAAAAAAAAABAAEAAAABAAAAEgAAAAkAAAAECAAA";
    var uri = new Uri($"wss://{desktop.DesktopInfo.ClinkLvsOutHost}/clinkProxy/{desktop.DesktopId}/MAIN");

    while (!globalToken.IsCancellationRequested)
    {
        // 为本次 60 秒生命周期创建独立的控制源
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);
        sessionCts.CancelAfter(TimeSpan.FromMinutes(1)); // 60秒后自动触发取消

        using var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Origin", "https://pc.ctyun.cn");
        client.Options.AddSubProtocol("binary");

        try
        {
            Utility.WriteLine(ConsoleColor.Cyan, $"[{desktop.DesktopCode}] === 新周期开始，尝试连接 ===");
            await client.ConnectAsync(uri, sessionCts.Token);

            // 1. 发送 Json 握手信息
            var connectMessage = new ConnecMessage
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
            var msgBytes = JsonSerializer.SerializeToUtf8Bytes(connectMessage, AppJsonSerializerContext.Default.ConnecMessage);
            await client.SendAsync(msgBytes, WebSocketMessageType.Text, true, sessionCts.Token);

            // 2. 发送初始保活二进制包
            await Task.Delay(500, sessionCts.Token);
            await client.SendAsync(Convert.FromBase64String(InitialPayloadBase64), WebSocketMessageType.Binary, true, sessionCts.Token);

            // 3. 运行接收循环，直到 60 秒时间到
            Utility.WriteLine(ConsoleColor.Green, $"[{desktop.DesktopCode}] 连接已就绪，保持 60 秒...");

            try
            {
                await ReceiveLoop(client, desktop, sessionCts.Token);
            }
            catch (OperationCanceledException)
            {
                Utility.WriteLine(ConsoleColor.Yellow, $"[{desktop.DesktopCode}] 60秒时间到，准备重连...");
            }
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            Utility.WriteLine(ConsoleColor.Red, $"[{desktop.DesktopCode}] 异常: {ex.Message}");
            await Task.Delay(5000, globalToken); // 出错后等5秒再试，防止死循环刷请求
        }
        finally
        {
            if (client.State == WebSocketState.Open)
            {
                await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Timeout Reset", CancellationToken.None);
            }
        }
    }
}

static async Task ReceiveLoop(ClientWebSocket ws, Desktop desktop, CancellationToken ct)
{
    var buffer = new byte[8192];
    var encryptor = new Encryption();

    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
    {
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
        if (result.MessageType == WebSocketMessageType.Close) break;

        if (result.Count > 0)
        {
            var data = buffer.AsSpan(0, result.Count).ToArray();
            var hex = BitConverter.ToString(data).Replace("-", "");

            if (hex.StartsWith("5245445102", StringComparison.OrdinalIgnoreCase))
            {
                Utility.WriteLine(ConsoleColor.Green,
                    $"[{desktop.DesktopCode}] -> 收到保活校验");
                var response = encryptor.Execute(data);
                await ws.SendAsync(response, WebSocketMessageType.Binary, true, ct);
                Utility.WriteLine(ConsoleColor.DarkGreen, $"[{desktop.DesktopCode}] -> 发送保活响应成功");
            }
        }
    }
}

#region 辅助工具
static (string user, string pwd, string code) ResolveCredentials()
{
    if (IsRunningInContainer() || Debugger.IsAttached)
    {
        return (Environment.GetEnvironmentVariable("APP_USER"),
                Environment.GetEnvironmentVariable("APP_PASSWORD"),
                Environment.GetEnvironmentVariable("DEVICECODE"));
    }
    if (!File.Exists("DeviceCode.txt")) File.WriteAllText("DeviceCode.txt", "web_" + GenerateRandomString(32));
    var code = File.ReadAllText("DeviceCode.txt");
    Console.Write("账号: "); var u = Console.ReadLine();
    Console.Write("密码: "); var p = ReadPassword();
    return (u, p, code);
}

static async Task<bool> PerformLoginSequence(CtYunApi api, string u, string p)
{
    if (!await api.LoginAsync(u, p)) return false;
    if (!api.LoginInfo.BondedDevice)
    {
        await api.GetSmsCodeAsync(u);
        Console.Write("短信验证码: ");
        if (!await api.BindingDeviceAsync(Console.ReadLine())) return false;
    }
    return true;
}

static string GenerateRandomString(int length)
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    return new string(Enumerable.Repeat(chars, length).Select(s => s[RandomNumberGenerator.GetInt32(s.Length)]).ToArray());
}

static bool IsRunningInContainer() => File.Exists("/.dockerenv");

static string ReadPassword()
{
    StringBuilder sb = new StringBuilder();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && sb.Length > 0) { sb.Remove(sb.Length - 1, 1); Console.Write("\b \b"); }
        else if (!char.IsControl(key.KeyChar)) { sb.Append(key.KeyChar); Console.Write("*"); }
    }
    Console.WriteLine();
    return sb.ToString();
}
#endregion

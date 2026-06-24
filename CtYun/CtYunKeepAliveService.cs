using CtYun.Models;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CtYun;

internal sealed class CtYunKeepAliveService : BackgroundService
{
    private readonly CtYunConfigStore _store;
    private readonly SemaphoreSlim _restartLock = new(1, 1);
    private readonly ConcurrentDictionary<string, AccountRuntimeState> _accountStates = new();
    private readonly ConcurrentDictionary<string, PendingBindingSession> _pendingBindings = new();
    private readonly object _eventsLock = new();
    private readonly Queue<ServiceEvent> _events = new();
    private CancellationTokenSource _sessionCts = new();
    private Task _sessionTask = Task.CompletedTask;

    public CtYunKeepAliveService(CtYunConfigStore store)
    {
        _store = store;
    }

    public bool IsRunning => !_sessionTask.IsCompleted && !_sessionCts.IsCancellationRequested;

    public List<AccountRuntimeState> GetAccountStates()
    {
        return _accountStates.Values
            .OrderBy(account => account.Name)
            .Select(Clone)
            .ToList();
    }

    public List<ServiceEvent> GetEvents()
    {
        lock (_eventsLock)
        {
            return _events.Reverse().ToList();
        }
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        await _restartLock.WaitAsync(ct);
        try
        {
            await StopSessionsCoreAsync(ct);
            _accountStates.Clear();
            _sessionCts = new CancellationTokenSource();
            _sessionTask = RunConfiguredAccountsAsync(_sessionCts.Token);
            AddEvent("info", "service", "保活服务已加载最新配置。");
        }
        finally
        {
            _restartLock.Release();
        }
    }

    public async Task StopSessionsAsync(CancellationToken ct)
    {
        await _restartLock.WaitAsync(ct);
        try
        {
            await StopSessionsCoreAsync(ct);
            AddEvent("warning", "service", "保活服务已停止。");
        }
        finally
        {
            _restartLock.Release();
        }
    }

    public async Task<AccountActionResult> TestLoginAsync(AccountConfig account, CancellationToken ct)
    {
        var api = new CtYunApi(account.DeviceCode);
        var loginOk = await api.LoginAsync(account.User, account.Password, ct);
        if (!loginOk)
        {
            return new AccountActionResult
            {
                Success = false,
                Message = "登录失败，请检查账号、密码或验证码识别服务。"
            };
        }

        return new AccountActionResult
        {
            Success = true,
            BondedDevice = api.LoginInfo.BondedDevice,
            Message = api.LoginInfo.BondedDevice ? "登录成功，设备已绑定。" : "登录成功，但当前设备未绑定，需要发送短信验证码。"
        };
    }

    public async Task<AccountActionResult> SendSmsAsync(AccountConfig account, CancellationToken ct)
    {
        var api = new CtYunApi(account.DeviceCode);
        var loginOk = await api.LoginAsync(account.User, account.Password, ct);
        if (!loginOk)
        {
            return new AccountActionResult
            {
                Success = false,
                Message = "登录失败，无法发送短信验证码。"
            };
        }

        if (api.LoginInfo.BondedDevice)
        {
            return new AccountActionResult
            {
                Success = true,
                BondedDevice = true,
                Message = "当前设备已绑定，无需短信验证码。"
            };
        }

        var smsOk = await api.GetSmsCodeAsync(account.User, ct);
        if (smsOk)
        {
            _pendingBindings[AccountKey(account)] = new PendingBindingSession(api, DateTimeOffset.Now.AddMinutes(10));
        }

        return new AccountActionResult
        {
            Success = smsOk,
            SmsSent = smsOk,
            Message = smsOk ? "短信验证码已发送，请在后台输入验证码完成绑定。" : "短信验证码发送失败，请稍后重试。"
        };
    }

    public async Task<AccountActionResult> BindDeviceAsync(AccountConfig account, string verificationCode, CancellationToken ct)
    {
        var api = await GetBindingApiAsync(account, ct);
        if (api == null)
        {
            return new AccountActionResult
            {
                Success = false,
                Message = "登录失败，无法绑定设备。"
            };
        }

        if (api.LoginInfo.BondedDevice)
        {
            return new AccountActionResult
            {
                Success = true,
                BondedDevice = true,
                Message = "当前设备已经绑定。"
            };
        }

        var bindOk = await api.BindingDeviceAsync(verificationCode.Trim(), ct);
        _pendingBindings.TryRemove(AccountKey(account), out _);
        return new AccountActionResult
        {
            Success = bindOk,
            BondedDevice = bindOk,
            Message = bindOk ? "设备绑定成功，保活服务已准备重启。" : "设备绑定失败，请检查短信验证码是否正确。"
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReloadAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopSessionsCoreAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task RunConfiguredAccountsAsync(CancellationToken ct)
    {
        var config = _store.GetConfig();
        if (config.Accounts.Count == 0)
        {
            AddEvent("warning", "service", "还没有账号配置，保活服务等待后台配置。");
            return;
        }

        AddEvent("info", "service", $"开始保活：{config.Accounts.Count} 个账号，每 {config.KeepAliveSeconds} 秒重连。");
        var tasks = config.Accounts.Select(account => RunAccountAsync(account, config.KeepAliveSeconds, ct)).ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            AddEvent("warning", "service", "保活任务已取消。");
        }
        catch (Exception ex)
        {
            AddEvent("error", "service", "保活服务异常：" + ex.Message);
        }
    }

    private async Task RunAccountAsync(AccountConfig account, int keepAliveSeconds, CancellationToken ct)
    {
        var label = AccountLabel(account);
        var api = new CtYunApi(account.DeviceCode);
        SetAccountState(account, "logging-in", "正在登录。");
        AddEvent("info", label, "开始登录。");

        if (!await PerformLoginSequenceAsync(api, account, ct))
        {
            SetAccountState(account, "error", "登录失败或设备尚未绑定。");
            AddEvent("error", label, "登录失败，跳过该账号。");
            return;
        }

        var desktopList = await api.GetLlientListAsync(ct);
        if (desktopList == null || desktopList.Count == 0)
        {
            SetAccountState(account, "warning", "未获取到云电脑。");
            AddEvent("warning", label, "未获取到云电脑。");
            return;
        }

        var activeDesktops = new List<Desktop>();
        foreach (var desktop in desktopList)
        {
            if (desktop.UseStatusText != "运行中")
            {
                AddDesktopState(account, desktop, "warning", $"云电脑状态：{desktop.UseStatusText}。");
            }

            var connectResult = await api.ConnectAsync(desktop.DesktopId, ct);
            if (connectResult.Success && connectResult.Data?.DesktopInfo != null)
            {
                desktop.DesktopInfo = connectResult.Data.DesktopInfo;
                activeDesktops.Add(desktop);
                AddDesktopState(account, desktop, "connected", "已获取连接信息。");
            }
            else
            {
                AddDesktopState(account, desktop, "error", "连接失败：" + connectResult.Msg);
                AddEvent("error", label, $"Connect Error: [{desktop.DesktopId}] {connectResult.Msg}");
            }
        }

        if (activeDesktops.Count == 0)
        {
            SetAccountState(account, "warning", "没有可保活的云电脑。");
            AddEvent("warning", label, "没有可保活的云电脑。");
            return;
        }

        SetAccountState(account, "running", $"保活中，已接管 {activeDesktops.Count} 台云电脑。", activeDesktops.Count);
        AddEvent("info", label, $"保活任务启动，每 {keepAliveSeconds} 秒强制重连一次。");
        var keepAliveTasks = activeDesktops.Select(d => KeepAliveWorkerWithForcedReset(api, account, d, keepAliveSeconds, ct)).ToArray();
        await Task.WhenAll(keepAliveTasks);
    }

    private async Task<bool> PerformLoginSequenceAsync(CtYunApi api, AccountConfig account, CancellationToken ct)
    {
        if (!await api.LoginAsync(account.User, account.Password, ct))
        {
            return false;
        }

        if (api.LoginInfo.BondedDevice)
        {
            return true;
        }

        AddEvent("warning", AccountLabel(account), "当前设备未绑定，请在后台发送短信并输入验证码。");
        return false;
    }

    private async Task<CtYunApi> GetBindingApiAsync(AccountConfig account, CancellationToken ct)
    {
        if (_pendingBindings.TryGetValue(AccountKey(account), out var pending) && pending.ExpiresAt > DateTimeOffset.Now)
        {
            return pending.Api;
        }

        _pendingBindings.TryRemove(AccountKey(account), out _);
        var api = new CtYunApi(account.DeviceCode);
        return await api.LoginAsync(account.User, account.Password, ct) ? api : null;
    }

    private async Task KeepAliveWorkerWithForcedReset(CtYunApi api, AccountConfig account, Desktop desktop, int keepAliveSeconds, CancellationToken globalToken)
    {
        var label = AccountLabel(account);
        var initialPayload = Convert.FromBase64String("UkVEUQIAAAACAAAAGgAAAAAAAAABAAEAAAABAAAAEgAAAAkAAAAECAAA");
        var uri = new Uri($"wss://{desktop.DesktopInfo.ClinkLvsOutHost}/clinkProxy/{desktop.DesktopId}/MAIN");

        while (!globalToken.IsCancellationRequested)
        {
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);
            sessionCts.CancelAfter(TimeSpan.FromSeconds(keepAliveSeconds));

            using var client = new ClientWebSocket();
            client.Options.SetRequestHeader("Origin", "https://pc.ctyun.cn");
            client.Options.AddSubProtocol("binary");

            try
            {
                AddDesktopState(account, desktop, "connecting", "正在建立 WebSocket。");
                await client.ConnectAsync(uri, sessionCts.Token);

                var hostParts = desktop.DesktopInfo.ClinkLvsOutHost.Split(':', 2);
                var connectMessage = new ConnecMessage
                {
                    type = 1,
                    ssl = 1,
                    host = hostParts[0],
                    port = hostParts.Length > 1 ? hostParts[1] : "443",
                    ca = desktop.DesktopInfo.CaCert,
                    cert = desktop.DesktopInfo.ClientCert,
                    key = desktop.DesktopInfo.ClientKey,
                    servername = desktop.DesktopInfo.Host + ":" + desktop.DesktopInfo.Port,
                    oqs = 0
                };

                var msgBytes = JsonSerializer.SerializeToUtf8Bytes(connectMessage, AppJsonSerializerContext.Default.ConnecMessage);
                await client.SendAsync(msgBytes, WebSocketMessageType.Text, true, sessionCts.Token);

                await Task.Delay(500, sessionCts.Token);
                await client.SendAsync(initialPayload, WebSocketMessageType.Binary, true, sessionCts.Token);

                AddDesktopState(account, desktop, "running", $"连接已就绪，本周期保持 {keepAliveSeconds} 秒。");

                try
                {
                    await ReceiveLoop(api, client, account, desktop, sessionCts.Token);
                }
                catch (OperationCanceledException)
                {
                    AddDesktopState(account, desktop, "reconnecting", "周期时间到，准备重连。");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AddDesktopState(account, desktop, "error", "异常：" + ex.Message);
                AddEvent("error", $"{label}/{desktop.DesktopCode}", ex.Message);
                await Task.Delay(5000, globalToken);
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

    private async Task ReceiveLoop(CtYunApi api, ClientWebSocket ws, AccountConfig account, Desktop desktop, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var encryptor = new Encryption();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.Count == 0)
            {
                continue;
            }

            var data = buffer.AsSpan(0, result.Count).ToArray();
            var hex = BitConverter.ToString(data).Replace("-", "");
            if (hex.StartsWith("52454451", StringComparison.OrdinalIgnoreCase))
            {
                var response = encryptor.Execute(data);
                await ws.SendAsync(response, WebSocketMessageType.Binary, true, ct);
                AddDesktopState(account, desktop, "running", "收到保活校验并已响应。");
                continue;
            }

            try
            {
                var infos = SendInfo.FromBuffer(data);
                foreach (var info in infos)
                {
                    if (info.Type == 103)
                    {
                        var payload = Encoding.UTF8.GetBytes("{\"type\":1,\"userName\":\"" + api.LoginInfo.UserName + "\",\"userInfo\":\"\",\"userId\":" + api.LoginInfo.UserId + "}");
                        var byUserName = new SendInfo { Type = 118, Data = payload }.ToBuffer(true);
                        await ws.SendAsync(byUserName, WebSocketMessageType.Binary, true, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                AddEvent("warning", $"{AccountLabel(account)}/{desktop.DesktopCode}", "消息解析失败：" + ex.Message);
            }
        }
    }

    private async Task StopSessionsCoreAsync(CancellationToken waitToken)
    {
        if (_sessionCts.IsCancellationRequested)
        {
            return;
        }

        await _sessionCts.CancelAsync();
        try
        {
            await _sessionTask.WaitAsync(waitToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AddEvent("error", "service", "停止服务时发生异常：" + ex.Message);
        }
    }

    private void SetAccountState(AccountConfig account, string status, string message, int? desktopCount = null)
    {
        var key = AccountKey(account);
        var state = _accountStates.AddOrUpdate(
            key,
            _ => new AccountRuntimeState
            {
                Name = AccountLabel(account),
                User = account.User,
                Status = status,
                Message = message,
                DesktopCount = desktopCount ?? 0,
                UpdatedAt = DateTimeOffset.Now
            },
            (_, current) =>
            {
                current.Status = status;
                current.Message = message;
                if (desktopCount.HasValue)
                {
                    current.DesktopCount = desktopCount.Value;
                }

                current.UpdatedAt = DateTimeOffset.Now;
                return current;
            });

        Utility.WriteLine(ConsoleColor.Cyan, $"[{state.Name}] {message}");
    }

    private void AddDesktopState(AccountConfig account, Desktop desktop, string status, string message)
    {
        var state = _accountStates.GetOrAdd(AccountKey(account), _ => new AccountRuntimeState
        {
            Name = AccountLabel(account),
            User = account.User,
            Status = "running",
            Message = "保活中。",
            UpdatedAt = DateTimeOffset.Now
        });

        lock (state)
        {
            var desktopState = state.Desktops.FirstOrDefault(item => item.DesktopId == desktop.DesktopId);
            if (desktopState == null)
            {
                desktopState = new DesktopRuntimeState
                {
                    DesktopId = desktop.DesktopId,
                    DesktopCode = desktop.DesktopCode,
                    DesktopName = desktop.DesktopName
                };
                state.Desktops.Add(desktopState);
            }

            desktopState.DesktopCode = desktop.DesktopCode;
            desktopState.DesktopName = desktop.DesktopName;
            desktopState.Status = status;
            desktopState.Message = message;
            desktopState.UpdatedAt = DateTimeOffset.Now;
            state.DesktopCount = state.Desktops.Count;
            state.UpdatedAt = DateTimeOffset.Now;
        }

        Utility.WriteLine(ConsoleColor.DarkGreen, $"[{AccountLabel(account)}][{desktop.DesktopCode}] {message}");
    }

    private void AddEvent(string level, string scope, string message)
    {
        var item = new ServiceEvent
        {
            Time = DateTimeOffset.Now,
            Level = level,
            Scope = scope,
            Message = message
        };

        lock (_eventsLock)
        {
            _events.Enqueue(item);
            while (_events.Count > 200)
            {
                _events.Dequeue();
            }
        }

        var color = level switch
        {
            "error" => ConsoleColor.Red,
            "warning" => ConsoleColor.Yellow,
            _ => ConsoleColor.Green
        };
        Utility.WriteLine(color, $"[{scope}] {message}");
    }

    private static AccountRuntimeState Clone(AccountRuntimeState state)
    {
        lock (state)
        {
            return new AccountRuntimeState
            {
                Name = state.Name,
                User = state.User,
                Status = state.Status,
                Message = state.Message,
                DesktopCount = state.DesktopCount,
                UpdatedAt = state.UpdatedAt,
                Desktops = state.Desktops.Select(desktop => new DesktopRuntimeState
                {
                    DesktopId = desktop.DesktopId,
                    DesktopCode = desktop.DesktopCode,
                    DesktopName = desktop.DesktopName,
                    Status = desktop.Status,
                    Message = desktop.Message,
                    UpdatedAt = desktop.UpdatedAt
                }).ToList()
            };
        }
    }

    private static string AccountKey(AccountConfig account) => string.IsNullOrWhiteSpace(account.User) ? AccountLabel(account) : account.User;

    private static string AccountLabel(AccountConfig account) => string.IsNullOrWhiteSpace(account.Name) ? account.User : account.Name;

    private sealed record PendingBindingSession(CtYunApi Api, DateTimeOffset ExpiresAt);
}

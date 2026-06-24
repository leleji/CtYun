using CtYun;
using CtYun.Models;
using Microsoft.AspNetCore.HttpOverrides;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

Utility.WriteLine(ConsoleColor.Green, $"版本：{Assembly.GetEntryAssembly()?.GetName().Version}");

var builder = WebApplication.CreateSlimBuilder(args);
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(urls))
{
    var port = Environment.GetEnvironmentVariable("PORT");
    urls = string.IsNullOrWhiteSpace(port) ? "http://0.0.0.0:8080" : $"http://0.0.0.0:{port}";
}

builder.WebHost.UseUrls(urls);
builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
builder.Services.AddSingleton<CtYunConfigStore>();
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddSingleton<CtYunKeepAliveService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CtYunKeepAliveService>());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    await next();
});
app.UseHsts();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (NeedsCsrfProtection(context.Request) && !IsSafeBrowserWrite(context))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ApiMessage("Unsafe cross-site request blocked."), AppJsonSerializerContext.Default.ApiMessage);
        return;
    }

    await next();
});
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/api/auth"))
    {
        await next();
        return;
    }

    var auth = context.RequestServices.GetRequiredService<AdminAuthService>();
    if (!auth.TryGetSession(context, out var mustChangePassword))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ApiMessage("请先登录管理员后台。"), AppJsonSerializerContext.Default.ApiMessage);
        return;
    }

    if (mustChangePassword)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ApiMessage("首次登录必须先修改管理员密码。"), AppJsonSerializerContext.Default.ApiMessage);
        return;
    }

    await next();
});

app.MapGet("/api/auth/status", (HttpContext context, AdminAuthService auth) => Results.Ok(auth.GetStatus(context)));

app.MapPost("/api/auth/login", IResult (AdminLoginRequest request, HttpContext context, AdminAuthService auth) =>
{
    if (auth.IsLoginLockedOut(context, out var retryAfter))
    {
        return LoginBlocked(context, retryAfter);
    }

    if (!auth.VerifyPassword(request.Password))
    {
        auth.RecordFailedLogin(context, out retryAfter);
        if (retryAfter > TimeSpan.Zero)
        {
            return LoginBlocked(context, retryAfter);
        }

        return Results.Unauthorized();
    }

    auth.RecordSuccessfulLogin(context);
    auth.SignIn(context);
    return Results.Ok(new AdminAuthStatusResponse
    {
        Authenticated = true,
        MustChangePassword = auth.MustChangePassword
    });
});

app.MapPost("/api/auth/change-password", IResult (AdminChangePasswordRequest request, HttpContext context, AdminAuthService auth) =>
{
    if (!auth.TryGetSession(context, out _))
    {
        return Results.Unauthorized();
    }

    if (!auth.TryChangePassword(request.CurrentPassword, request.NewPassword, out var message))
    {
        return Results.BadRequest(new ApiMessage(message));
    }

    auth.SignIn(context);
    return Results.Ok(new ApiMessage(message));
});

app.MapPost("/api/auth/logout", IResult (HttpContext context, AdminAuthService auth) =>
{
    auth.SignOut(context);
    return Results.Ok(new ApiMessage("已退出登录。"));
});

app.MapGet("/api/status", (CtYunConfigStore store, CtYunKeepAliveService service) => Results.Ok(new StatusResponse
{
    Configured = store.GetConfig().Accounts.Count > 0,
    DataDir = store.DataDir,
    ConfigPath = store.ConfigPath,
    KeepAliveSeconds = store.GetConfig().KeepAliveSeconds,
    Running = service.IsRunning,
    Accounts = service.GetAccountStates(),
    Events = service.GetEvents()
}));

app.MapGet("/api/config", (CtYunConfigStore store) => Results.Ok(store.GetSanitizedConfig()));

app.MapPut("/api/config", async (AppConfig config, CtYunConfigStore store, CtYunKeepAliveService service, CancellationToken ct) =>
{
    var normalized = store.Normalize(config, keepExistingPasswords: true);
    await store.SaveAsync(normalized, ct);
    await service.ReloadAsync(ct);
    return Results.Ok(store.GetSanitizedConfig());
});

app.MapPost("/api/accounts/test-login", async Task<IResult> (AccountConfig account, CtYunConfigStore store, CtYunKeepAliveService service, CancellationToken ct) =>
{
    var normalized = store.NormalizeAccount(account, keepExistingPassword: true);
    if (string.IsNullOrWhiteSpace(normalized.User) || string.IsNullOrWhiteSpace(normalized.Password))
    {
        return Results.BadRequest(new ApiMessage("账号和密码不能为空。"));
    }

    normalized.DeviceCode = store.ResolveDeviceCode(normalized);
    var result = await service.TestLoginAsync(normalized, ct);
    return Results.Ok(result);
});

app.MapPost("/api/accounts/send-sms", async Task<IResult> (AccountConfig account, CtYunConfigStore store, CtYunKeepAliveService service, CancellationToken ct) =>
{
    var normalized = store.NormalizeAccount(account, keepExistingPassword: true);
    if (string.IsNullOrWhiteSpace(normalized.User) || string.IsNullOrWhiteSpace(normalized.Password))
    {
        return Results.BadRequest(new ApiMessage("账号和密码不能为空。"));
    }

    normalized.DeviceCode = store.ResolveDeviceCode(normalized);
    var result = await service.SendSmsAsync(normalized, ct);
    return Results.Ok(result);
});

app.MapPost("/api/accounts/bind-device", async Task<IResult> (BindDeviceRequest request, CtYunConfigStore store, CtYunKeepAliveService service, CancellationToken ct) =>
{
    var account = store.NormalizeAccount(request.Account, keepExistingPassword: true);
    if (string.IsNullOrWhiteSpace(account.User) || string.IsNullOrWhiteSpace(account.Password))
    {
        return Results.BadRequest(new ApiMessage("账号和密码不能为空。"));
    }

    if (string.IsNullOrWhiteSpace(request.Code))
    {
        return Results.BadRequest(new ApiMessage("短信验证码不能为空。"));
    }

    account.DeviceCode = store.ResolveDeviceCode(account);
    var result = await service.BindDeviceAsync(account, request.Code, ct);
    if (result.Success)
    {
        await service.ReloadAsync(ct);
    }

    return Results.Ok(result);
});

app.MapPost("/api/service/restart", async (CtYunKeepAliveService service, CancellationToken ct) =>
{
    await service.ReloadAsync(ct);
    return Results.Ok(new ApiMessage("保活服务已重启。"));
});

app.MapPost("/api/service/stop", async (CtYunKeepAliveService service, CancellationToken ct) =>
{
    await service.StopSessionsAsync(ct);
    return Results.Ok(new ApiMessage("保活服务已停止。"));
});

app.MapFallbackToFile("index.html");

await app.RunAsync();

static void ConfigureJson(JsonSerializerOptions options)
{
    options.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
}

static IResult LoginBlocked(HttpContext context, TimeSpan retryAfter)
{
    var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
    context.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
    return Results.Json(
        new ApiMessage("Too many failed login attempts. Try again later."),
        statusCode: StatusCodes.Status429TooManyRequests);
}

static bool NeedsCsrfProtection(HttpRequest request)
{
    if (!request.Path.StartsWithSegments("/api"))
    {
        return false;
    }

    return HttpMethods.IsPost(request.Method) ||
           HttpMethods.IsPut(request.Method) ||
           HttpMethods.IsPatch(request.Method) ||
           HttpMethods.IsDelete(request.Method);
}

static bool IsSafeBrowserWrite(HttpContext context)
{
    if (!context.Request.Headers.TryGetValue("X-CtYun-CSRF", out var csrf) ||
        !StringValuesContain(csrf, "1"))
    {
        return false;
    }

    return HasSameOrigin(context);
}

static bool HasSameOrigin(HttpContext context)
{
    var origin = context.Request.Headers["Origin"].ToString();
    if (!string.IsNullOrWhiteSpace(origin))
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
               IsRequestOrigin(context.Request, originUri);
    }

    var referer = context.Request.Headers["Referer"].ToString();
    if (!string.IsNullOrWhiteSpace(referer))
    {
        return Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) &&
               IsRequestOrigin(context.Request, refererUri);
    }

    return true;
}

static bool IsRequestOrigin(HttpRequest request, Uri uri)
{
    if (IsSameOrigin(request.Scheme, request.Host.Host, request.Host.Port, uri))
    {
        return true;
    }

    foreach (var origin in GetForwardedOrigins(request))
    {
        if (IsForwardedOrigin(uri, origin.Scheme, origin.Host, origin.Port))
        {
            return true;
        }
    }

    return false;
}

static bool IsSameOrigin(string scheme, string host, int? port, Uri uri)
{
    return string.Equals(scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase) &&
           GetPort(scheme, port) == GetPort(uri.Scheme, uri.IsDefaultPort ? null : uri.Port);
}

static bool IsForwardedOrigin(Uri requestOrigin, string scheme, string host, string port)
{
    if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(host))
    {
        return false;
    }

    var candidateHost = host;
    if (!HasPort(candidateHost) &&
        int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var forwardedPort))
    {
        candidateHost = candidateHost.StartsWith("[", StringComparison.Ordinal) || !candidateHost.Contains(':')
            ? $"{candidateHost}:{forwardedPort}"
            : $"[{candidateHost}]:{forwardedPort}";
    }

    return Uri.TryCreate($"{scheme}://{candidateHost}", UriKind.Absolute, out var forwardedUri) &&
           IsSameOrigin(forwardedUri.Scheme, forwardedUri.Host, forwardedUri.IsDefaultPort ? null : forwardedUri.Port, requestOrigin);
}

static IEnumerable<(string Scheme, string Host, string Port)> GetForwardedOrigins(HttpRequest request)
{
    foreach (var forwarded in SplitHeaderValues(request.Headers["Forwarded"]))
    {
        var scheme = GetForwardedParameter(forwarded, "proto");
        var host = GetForwardedParameter(forwarded, "host");
        if (!string.IsNullOrWhiteSpace(host))
        {
            yield return (scheme ?? request.Scheme, host, null);
        }
    }

    var forwardedHosts = SplitHeaderValues(request.Headers["X-Forwarded-Host"]);
    if (forwardedHosts.Count == 0)
    {
        yield break;
    }

    var forwardedProtos = SplitHeaderValues(request.Headers["X-Forwarded-Proto"]);
    var forwardedSchemes = SplitHeaderValues(request.Headers["X-Forwarded-Scheme"]);
    var forwardedPorts = SplitHeaderValues(request.Headers["X-Forwarded-Port"]);

    for (var i = 0; i < forwardedHosts.Count; i++)
    {
        var scheme = GetIndexedValue(forwardedProtos, i) ??
                     GetIndexedValue(forwardedSchemes, i) ??
                     request.Scheme;
        yield return (scheme, forwardedHosts[i], GetIndexedValue(forwardedPorts, i));
    }
}

static List<string> SplitHeaderValues(Microsoft.Extensions.Primitives.StringValues values)
{
    var result = new List<string>();
    foreach (var value in values)
    {
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var clean = TrimForwardedValue(part);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                result.Add(clean);
            }
        }
    }

    return result;
}

static string GetForwardedParameter(string forwarded, string name)
{
    foreach (var segment in forwarded.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var equalsIndex = segment.IndexOf('=');
        if (equalsIndex <= 0)
        {
            continue;
        }

        var parameterName = segment[..equalsIndex].Trim();
        if (string.Equals(parameterName, name, StringComparison.OrdinalIgnoreCase))
        {
            return TrimForwardedValue(segment[(equalsIndex + 1)..]);
        }
    }

    return null;
}

static string GetIndexedValue(List<string> values, int index)
{
    if (values.Count == 0)
    {
        return null;
    }

    return index < values.Count ? values[index] : values[^1];
}

static string TrimForwardedValue(string value)
{
    value = value.Trim();
    return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
        ? value[1..^1]
        : value;
}

static bool HasPort(string host)
{
    return Uri.TryCreate($"http://{host}", UriKind.Absolute, out var uri) && !uri.IsDefaultPort;
}

static int GetPort(string scheme, int? port)
{
    if (port.HasValue)
    {
        return port.Value;
    }

    return string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
}

static bool StringValuesContain(Microsoft.Extensions.Primitives.StringValues values, string expected)
{
    foreach (var value in values)
    {
        if (string.Equals(value, expected, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

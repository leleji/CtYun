# CtYun 云电脑保活

CtYun 用于登录天翼云电脑并维持 WebSocket 保活连接。当前版本内置 Web 管理后台，Docker 启动后无需提前准备配置文件，也不需要 `-it` 交互输入。

## 功能

- Docker 一键启动，首次打开后台即可配置账号。
- 支持多账号保活，每个账号可接管多台云电脑。
- 支持后台保存账号、密码、设备码和保活间隔。
- 首次设备绑定可在后台发送短信验证码并完成绑定。
- 后台展示运行状态、云电脑状态和最近运行日志。
- 配置与设备码持久化在 `/app/data`，重建容器后无需重新绑定。

## Zeabur 一键部署

本仓库已包含 Zeabur 部署所需的根目录 `Dockerfile` 和 `zeabur.yaml` 模板文件。

- 直接从 GitHub 部署：在 Zeabur 新建服务并选择本仓库，Zeabur 会自动识别根目录 `Dockerfile`，按 Docker 方式构建部署。
- 发布为一键部署模板：登录 Zeabur 后执行 `npx zeabur@latest template create -f zeabur.yaml`，再到模板页面复制官方 Deploy 按钮到 README。
- 模板默认使用镜像 `su3817807/ctyun:latest`，暴露 HTTP 端口 `8080`，并挂载 `/app/data` 做持久化存储。

部署完成后打开 Zeabur 分配的域名，在 Web 后台配置账号即可。

## Docker Compose 一键部署

```bash
docker compose up -d
```

启动后访问：

```text
http://localhost:8080
```

在后台完成以下步骤：

1. 使用初始管理员密码 `admin123` 登录，按页面提示先修改密码。
2. 打开“账号配置”，填写账号、密码，设备码可留空。
3. 点击“保存并重启”。
4. 如果状态提示设备未绑定，点击“发送短信”，收到验证码后输入并点击“绑定设备”。
5. 绑定成功后服务会重新加载配置并开始保活。

## Docker Run 部署

```bash
docker run -d \
  --name ctyun \
  --restart unless-stopped \
  -p 8080:8080 \
  -v ctyun-data:/app/data \
  su3817807/ctyun:latest
```

查看日志：

```bash
docker logs -f ctyun
```

## 本地构建镜像

```bash
docker build -t ctyun:local .
docker run -d --name ctyun -p 8080:8080 -v ctyun-data:/app/data ctyun:local
```

## 配置文件

后台保存的配置文件默认位于数据目录：

```text
/app/data/accounts.json
```

也可以通过环境变量指定：

- `CTYUN_DATA_DIR`：数据目录，Docker 默认 `/app/data`。
- `CTYUN_CONFIG`：配置文件完整路径。
- `CTYUN_CONFIG_KEY`：账号配置加密密钥。可填写 32 字节 Base64 值；未设置时会在数据目录生成 `config-encryption.key`。
- `ADMIN_INITIAL_PASSWORD`：管理员后台初始密码，默认 `admin123`；首次登录后会强制修改。
- `APP_USER` / `APP_PASSWORD` / `APP_NAME` / `DEVICECODE`：兼容旧版单账号环境变量，首次启动时会迁移为后台配置。

管理员密码哈希保存到数据目录的 `admin-auth.json`，请和 `/app/data` 一起持久化保存。
如果忘记管理员密码，可以停止服务后删除该文件并重启，系统会重新生成初始密码并再次要求首次修改。
账号配置会以 AES-256-GCM 写入 `accounts.json`；旧版明文配置会在启动时自动迁移。请妥善保存 `CTYUN_CONFIG_KEY` 或数据目录中的 `config-encryption.key`，丢失后将无法解密已保存的账号配置。

手动初始化时仍可使用旧版明文 `accounts.json`，程序首次启动后会自动迁移为加密格式。明文示例：

```json
{
  "keepAliveSeconds": 60,
  "accounts": [
    {
      "name": "main",
      "user": "你的账号",
      "password": "你的密码",
      "deviceCode": "web_自动或手动生成的设备码"
    }
  ]
}
```

`deviceCode` 可不填。程序会为每个账号自动生成设备码，并随账号配置一起加密保存。旧版本生成的 `devices/{账号名}.txt` 仍会被兼容读取并迁移。

## 接口

管理后台使用以下本地 API：

- `GET /api/auth/status`：读取管理员登录状态。
- `POST /api/auth/login`：管理员登录。
- `POST /api/auth/change-password`：修改管理员密码。
- `POST /api/auth/logout`：退出管理员登录。
- `GET /api/status`：运行状态、账号状态、日志。
- `GET /api/config`：读取脱敏配置。
- `PUT /api/config`：保存配置并重启保活。
- `POST /api/accounts/test-login`：测试账号登录。
- `POST /api/accounts/send-sms`：发送设备绑定短信验证码。
- `POST /api/accounts/bind-device`：提交短信验证码并绑定设备。
- `POST /api/service/restart`：重启保活服务。
- `POST /api/service/stop`：停止保活服务。

## 说明

登录图形验证码识别接口方案来自 [sml2h3/ddddocr](https://github.com/sml2h3/ddddocr)。

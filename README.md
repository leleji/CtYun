### windows用户直接下载Releases执行即可。

首次登录需要绑定新设备接收验证码

### docker使用指南
> :warning: **提示：** docker第一次运行不要后台执行，不要加-d运行，第一次软件会生成一个新的设备信息，需要接收短信来进行风控校验，需手动输入，提示设备 **设备绑定成功** 即可后台运行。
```
docker run \
  --name ctyun \
  -e APP_USER="你的账号" \
  -e APP_PASSWORD='你的密码' \
  su3817807/ctyun:latest

```

```
//第一次运行不要加-d
docker run -d \
  --name ctyun \
  -e APP_USER="你的账号" \
  -e APP_PASSWORD='你的密码' \
  su3817807/ctyun:latest

```
### 查看日志检查是否登录并连接成功。

```
docker logs -f ctyun

```


验证码识别api方案来自 https://github.com/sml2h3/ddddocr
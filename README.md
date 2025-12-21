### windows用户直接下载Releases执行即可。

首次登录需要绑定新设备接收验证码,windows生成的设备信息在DeviceCode.txt文件中

### docker使用指南
> :warning: **提示：** docker第一次运行不要后台执行，不要加-d运行，要添加-it， 第一次软件会生成一个新的设备信息，需要接收短信来进行风控校验，需手动输入，提示设备 **设备绑定成功** 即可后台运行。

设备号DEVICECODE是web_加上随机32大小写字母数字_
例如 **web_L53itptDslz6manpE8Uq2Op1OEoKi85t** 
不要填写案例，请自己生成或更改上方案例

Linux 可使用以下代码生成
```
echo "web_$(cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 32 | head -n 1)"
```
```
//第一次初始化运行这个。
docker run -it \
  --name ctyun \
  -e APP_USER="你的账号" \
  -e APP_PASSWORD='你的密码' \
  -e DEVICECODE='设备Id' \
  su3817807/ctyun:latest

```

```
//第一次运行不要加-d
docker run -d \
  --name ctyun \
  -e APP_USER="你的账号" \
  -e APP_PASSWORD='你的密码' \
  -e DEVICECODE='设备Id' \
  su3817807/ctyun:latest

```
### 查看日志检查是否登录并连接成功。

```
docker logs -f ctyun

```


验证码识别api方案来自 https://github.com/sml2h3/ddddocr

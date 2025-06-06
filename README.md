### windows用户直接下载Releases执行即可。

只需要登录一次即可，登录成功会保存缓存数据，

### docker使用指南

```
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



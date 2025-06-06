# 使用官方 .NET 8 运行时镜像作为基础镜像
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base

# 设置工作目录
WORKDIR /app

# 将构建好的发布文件复制到容器中
COPY ./publish/ ./

ENV APP_USER=defaultuser \
    APP_PASSWORD=defaultpass

# 启动程序
ENTRYPOINT ["dotnet", "CtYun.dll"]

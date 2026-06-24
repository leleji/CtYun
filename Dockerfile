FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src
COPY CtYun.sln ./
COPY CtYun/CtYun.csproj CtYun/
RUN dotnet restore CtYun.sln

COPY . .
RUN dotnet publish CtYun/CtYun.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

ENV PORT=8080
ENV CTYUN_DATA_DIR=/app/data

COPY --from=build /app/publish ./

VOLUME ["/app/data"]
EXPOSE 8080

ENTRYPOINT ["dotnet", "CtYun.dll"]

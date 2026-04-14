FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-builder
WORKDIR /src

COPY src/StevensSupportHelper.Server/StevensSupportHelper.Server.csproj ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/StevensSupportHelper.Shared.csproj ./StevensSupportHelper.Shared/
RUN dotnet restore StevensSupportHelper.Server/StevensSupportHelper.Server.csproj

COPY src/StevensSupportHelper.Server/ ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/ ./StevensSupportHelper.Shared/
RUN dotnet publish StevensSupportHelper.Server/StevensSupportHelper.Server.csproj -c Release -o /app/server

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS adminweb-builder
WORKDIR /src

COPY src/StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj ./StevensSupportHelper.AdminWeb/
RUN dotnet restore StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj

COPY src/StevensSupportHelper.AdminWeb/ ./StevensSupportHelper.AdminWeb/
WORKDIR /src/StevensSupportHelper.AdminWeb
RUN dotnet publish -c Release -o /app/adminweb

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0
ENV StevensSupportHelperServer__Provider=Sqlite
ENV StevensSupportHelperServer__DatabasePath=/data/server-state.db
ENV StevensSupportHelperServer__StateFilePath=/data/server-state.json

RUN mkdir /data
VOLUME ["/data"]

COPY --from=server-builder /app/server /app/server
COPY --from=adminweb-builder /app/adminweb /app/adminweb

EXPOSE 5000 5001

ENTRYPOINT ["dotnet", "/app/server/StevensSupportHelper.Server.dll"]

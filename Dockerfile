# Stage 1: Build Server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-builder
WORKDIR /src
COPY src/StevensSupportHelper.Server/StevensSupportHelper.Server.csproj ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/StevensSupportHelper.Shared.csproj ./StevensSupportHelper.Shared/
RUN dotnet restore StevensSupportHelper.Server/StevensSupportHelper.Server.csproj
COPY src/StevensSupportHelper.Server/ ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/ ./StevensSupportHelper.Shared/
RUN dotnet publish StevensSupportHelper.Server/StevensSupportHelper.Server.csproj -c Release -o /app/server

# Stage 2: Build AdminWeb
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS adminweb-builder
WORKDIR /src
COPY src/StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj ./StevensSupportHelper.AdminWeb/
RUN dotnet restore StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj
COPY src/StevensSupportHelper.AdminWeb/ ./StevensSupportHelper.AdminWeb/
WORKDIR /src/StevensSupportHelper.AdminWeb
RUN dotnet publish -c Release -o /app/adminweb

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Create data directory
RUN mkdir /data

# Copy both applications
COPY --from=server-builder /app/server /app/server
COPY --from=adminweb-builder /app/adminweb /app/adminweb

# Server configuration
ENV ASPNETCORE_URLS=http://+:5000
ENV StevensSupportHelperServer__Provider=Sqlite
ENV StevensSupportHelperServer__DatabasePath=/data/server-state.db
ENV StevensSupportHelperServer__StateFilePath=/data/server-state.json
ENV ASPNETCORE_ENVIRONMENT=Production

# AdminWeb configuration
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose ports
EXPOSE 5000

# Default: run server
ENTRYPOINT ["dotnet", "/app/server/StevensSupportHelper.Server.dll"]# Stage 1: Build Server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-builder
WORKDIR /src
COPY src/StevensSupportHelper.Server/StevensSupportHelper.Server.csproj ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/StevensSupportHelper.Shared.csproj ./StevensSupportHelper.Shared/
RUN dotnet restore StevensSupportHelper.Server/StevensSupportHelper.Server.csproj
COPY src/StevensSupportHelper.Server/ ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/ ./StevensSupportHelper.Shared/
RUN dotnet publish StevensSupportHelper.Server/StevensSupportHelper.Server.csproj -c Release -o /app/server

# Stage 2: Build AdminWeb
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS adminweb-builder
WORKDIR /src
COPY src/StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj ./StevensSupportHelper.AdminWeb/
RUN dotnet restore StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj
COPY src/StevensSupportHelper.AdminWeb/ ./StevensSupportHelper.AdminWeb/
WORKDIR /src/StevensSupportHelper.AdminWeb
RUN dotnet publish -c Release -o /app/adminweb

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Create data directory
RUN mkdir /data

# Copy both applications
COPY --from=server-builder /app/server /app/server
COPY --from=adminweb-builder /app/adminweb /app/adminweb

# Server configuration
ENV ASPNETCORE_URLS=http://+:5000
ENV StevensSupportHelperServer__Provider=Sqlite
ENV StevensSupportHelperServer__DatabasePath=/data/server-state.db
ENV StevensSupportHelperServer__StateFilePath=/data/server-state.json
ENV ASPNETCORE_ENVIRONMENT=Production

# AdminWeb configuration
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose ports
EXPOSE 5000

# Default: run server
ENTRYPOINT ["dotnet", "/app/server/StevensSupportHelper.Server.dll"]# Build Server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
WORKDIR /src

COPY src/StevensSupportHelper.Server/StevensSupportHelper.Server.csproj ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/StevensSupportHelper.Shared.csproj ./StevensSupportHelper.Shared/
RUN dotnet restore StevensSupportHelper.Server/StevensSupportHelper.Server.csproj

COPY src/StevensSupportHelper.Server/ ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/ ./StevensSupportHelper.Shared/
RUN dotnet publish StevensSupportHelper.Server/StevensSupportHelper.Server.csproj -c Release -o /app/server

# Build AdminWeb
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS adminweb-build

COPY src/StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj ./StevensSupportHelper.AdminWeb/
RUN dotnet restore StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj

COPY src/StevensSupportHelper.AdminWeb/ ./StevensSupportHelper.AdminWeb/
WORKDIR /src/StevensSupportHelper.AdminWeb
RUN dotnet publish -c Release -o /app/adminweb

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

RUN mkdir /data
VOLUME /data

COPY --from=server-build /app/server /app/server
COPY --from=adminweb-build /app/adminweb /app/adminweb

ENV ASPNETCORE_URLS=http://+:5000
ENV StevensSupportHelperServer__Provider=Sqlite
ENV StevensSupportHelperServer__DatabasePath=/data/server-state.db
ENV StevensSupportHelperServer__StateFilePath=/data/server-state.json
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5000

ENTRYPOINT ["dotnet", "/app/server/StevensSupportHelper.Server.dll"]# Stage 1: Build Server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-builder
WORKDIR /src
COPY src/StevensSupportHelper.Server/StevensSupportHelper.Server.csproj ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/StevensSupportHelper.Shared.csproj ./StevensSupportHelper.Shared/
RUN dotnet restore StevensSupportHelper.Server/StevensSupportHelper.Server.csproj
COPY src/StevensSupportHelper.Server/ ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/ ./StevensSupportHelper.Shared/
RUN dotnet publish StevensSupportHelper.Server/StevensSupportHelper.Server.csproj -c Release -o /app/server

# Stage 2: Build AdminWeb
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS adminweb-builder
WORKDIR /src
COPY src/StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj ./StevensSupportHelper.AdminWeb/
RUN dotnet restore StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj
COPY src/StevensSupportHelper.AdminWeb/ ./StevensSupportHelper.AdminWeb/
WORKDIR /src/StevensSupportHelper.AdminWeb
RUN dotnet publish -c Release -o /app/adminweb

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create data directory (volume mount point)
RUN mkdir /data
VOLUME ["/data"]

# Copy both applications
COPY --from=server-builder /app/server /app/server
COPY --from=adminweb-builder /app/adminweb /app/adminweb

# Server configuration
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENV DOTNET_EnableDiagnostics=0
ENV StevensSupportHelperServer__Provider=Sqlite
ENV StevensSupportHelperServer__DatabasePath=/data/server-state.db
ENV StevensSupportHelperServer__StateFilePath=/data/server-state.json

# AdminWeb configuration
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:5001
ENV Api__BaseUrl=http://localhost:5000

# Expose ports (Server: 5000, AdminWeb: 5001)
EXPOSE 5000 5001

# Default: run server
ENTRYPOINT ["dotnet", "/app/server/StevensSupportHelper.Server.dll"]# Stage 1: Build Server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-builder
WORKDIR /src
COPY src/StevensSupportHelper.Server/StevensSupportHelper.Server.csproj ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/StevensSupportHelper.Shared.csproj ./StevensSupportHelper.Shared/
RUN dotnet restore StevensSupportHelper.Server/StevensSupportHelper.Server.csproj
COPY src/StevensSupportHelper.Server/ ./StevensSupportHelper.Server/
COPY src/StevensSupportHelper.Shared/ ./StevensSupportHelper.Shared/
RUN dotnet publish StevensSupportHelper.Server/StevensSupportHelper.Server.csproj -c Release -o /app/server

# Stage 2: Build AdminWeb
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS adminweb-builder
WORKDIR /src
COPY src/StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj ./StevensSupportHelper.AdminWeb/
RUN dotnet restore StevensSupportHelper.AdminWeb/StevensSupportHelper.AdminWeb.csproj
COPY src/StevensSupportHelper.AdminWeb/ ./StevensSupportHelper.AdminWeb/
WORKDIR /src/StevensSupportHelper.AdminWeb
RUN dotnet publish -c Release -o /app/adminweb

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create data directory (volume mount point)
VOLUME ["/data"]

# Copy both applications
COPY --from=server-builder /app/server /app/server
COPY --from=adminweb-builder /app/adminweb /app/adminweb

# Server configuration
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENV DOTNET_EnableDiagnostics=0
ENV StevensSupportHelperServer__Provider=Sqlite
ENV StevensSupportHelperServer__DatabasePath=/data/server-state.db
ENV StevensSupportHelperServer__StateFilePath=/data/server-state.json

# AdminWeb configuration
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose ports (Server: 5000, AdminWeb: 5001)
EXPOSE 5000 5001

# Default: run server
# To run adminweb: docker run -p 5001:5001 ghcr.io/.../stevens-support-helper adminweb
ENTRYPOINT ["dotnet", "/app/server/StevensSupportHelper.Server.dll"]FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENV DOTNET_EnableDiagnostics=0
ENV StevensSupportHelperServer__Provider=Sqlite
ENV StevensSupportHelperServer__DatabasePath=/data/server-state.db
ENV StevensSupportHelperServer__StateFilePath=/data/server-state.json

RUN mkdir /data
VOLUME ["/data"]

COPY .runtime/docker-server-publish/ ./

EXPOSE 5000

ENTRYPOINT ["dotnet", "StevensSupportHelper.Server.dll"]

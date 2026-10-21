# Build stage
# Runs on the build machine platform and cross-compiles for the target platform, because the .NET SDK
# crashes under QEMU emulation (e.g. building linux/amd64 on Apple Silicon).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH

COPY "src/" "src/"

RUN dotnet publish "/src/WsLink.Api/WsLink.Api.csproj" -c $BUILD_CONFIGURATION -a $TARGETARCH -o /app/publish /p:UseAppHost=false


# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "WsLink.Api.dll"]

# API image. Build context: backend/. Versions are pinned; update them on purpose.
FROM mcr.microsoft.com/dotnet/sdk:10.0.404-noble AS build
WORKDIR /src

# Restore first, so code changes reuse the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Rolehound.Scoring/Rolehound.Scoring.csproj src/Rolehound.Scoring/
COPY src/Rolehound.Connectors/Rolehound.Connectors.csproj src/Rolehound.Connectors/
COPY src/Rolehound.Api/Rolehound.Api.csproj src/Rolehound.Api/
RUN dotnet restore src/Rolehound.Api/Rolehound.Api.csproj -r linux-x64

COPY src/ src/
RUN dotnet publish src/Rolehound.Api/Rolehound.Api.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /app

# Full Ubuntu runtime, not chiseled: it carries ICU (pt-BR formatting), tzdata
# (America/Sao_Paulo) and the libraries QuestPDF's native Skia build links to.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimitPercent=0x46

# The non-root user the .NET images ship with.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rolehound.Api.dll"]

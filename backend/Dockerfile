# API image. Build context: the repository root, as in
#   docker build -f backend/Dockerfile .
# Versions are pinned by tag and digest (the tag says what, the digest makes it exact);
# update both on purpose.
FROM mcr.microsoft.com/dotnet/sdk:10.0.404-noble@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build

# Same layout as the repository, so the analyzers find .editorconfig and its
# "backend/src/**/Migrations" section (EF migrations are generated code).
WORKDIR /repo
COPY .editorconfig ./
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/Rolehound.Scoring/Rolehound.Scoring.csproj backend/src/Rolehound.Scoring/
COPY backend/src/Rolehound.Connectors/Rolehound.Connectors.csproj backend/src/Rolehound.Connectors/
COPY backend/src/Rolehound.Api/Rolehound.Api.csproj backend/src/Rolehound.Api/

# Restore first, so code changes reuse the package layer.
WORKDIR /repo/backend
RUN dotnet restore src/Rolehound.Api/Rolehound.Api.csproj -r linux-x64

COPY backend/src/ src/
RUN dotnet publish src/Rolehound.Api/Rolehound.Api.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /app

# Full Ubuntu runtime, not chiseled: it carries ICU (pt-BR formatting), tzdata
# (America/Sao_Paulo), bash for the health check, and zlib and libstdc++ for
# QuestPDF's native libraries.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble@sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimitPercent=0x46

# The non-root user the .NET images ship with.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rolehound.Api.dll"]

FROM node:22-bookworm AS web-build
WORKDIR /src/apps/web
COPY apps/web/package*.json ./
RUN npm ci
COPY apps/web ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api-build
WORKDIR /src
COPY apps/api/Kanitel.Api.csproj apps/api/
RUN dotnet restore apps/api/Kanitel.Api.csproj
COPY apps/api apps/api
COPY --from=web-build /src/apps/web/dist apps/api/wwwroot
RUN dotnet publish apps/api/Kanitel.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM docker:28-cli AS docker-cli

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends git openssh-client ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    KANITEL_DATA_PATH=/data/kanitel.json \
    KANITEL_WORKSPACES_PATH=/data/workspaces \
    KANITEL_AGENT_RUNNER=docker \
    KANITEL_PUBLIC_API_URL=http://host.docker.internal:8080
VOLUME ["/data"]
COPY --from=api-build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kanitel.Api.dll"]

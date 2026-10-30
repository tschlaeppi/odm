FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Odm.Host/Odm.Host.csproj -c Release -o /app

# Eigenständige Agent-Pakete, die der Server zum Download und für die Installationsskripte anbietet.
# Überspringen (schnellerer Build): --build-arg AGENT_RIDS=""
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS agent
ARG AGENT_RIDS="win-x64 win-arm64 linux-x64 linux-arm64"
RUN apt-get update && apt-get install -y --no-install-recommends zip && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .
RUN mkdir -p /agent-packages && if [ -n "$AGENT_RIDS" ]; then scripts/publish-agent.sh /agent-packages $AGENT_RIDS; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# libldap: nötig für die optionale Anmeldung gegen LDAP/Active Directory (System.DirectoryServices.Protocols)
RUN apt-get update \
    && (apt-get install -y --no-install-recommends libldap2 || apt-get install -y --no-install-recommends libldap-2.5-0) \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
COPY --from=agent /agent-packages ./agent-packages
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Odm.Host.dll"]

# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LocalAIAgentAPI.csproj ./
RUN dotnet restore LocalAIAgentAPI.csproj -r linux-x64
COPY . .
RUN dotnet publish LocalAIAgentAPI.csproj -c Release -r linux-x64 --self-contained false -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends libtesseract5 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY scripts/link-tesseract-linux.sh /tmp/
RUN sh /tmp/link-tesseract-linux.sh /app && rm /tmp/link-tesseract-linux.sh

# Ollama runs on the host, not in this container (Docker Desktop resolves host.docker.internal;
# on Linux add --add-host=host.docker.internal:host-gateway).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Ollama__BaseUrl=http://host.docker.internal:11434
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "LocalAIAgentAPI.dll"]

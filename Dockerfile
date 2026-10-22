# Stage 1: build the React client.
FROM node:22 AS client
WORKDIR /src/client
COPY src/AudiobookOrganizer.Client/package.json src/AudiobookOrganizer.Client/package-lock.json ./
RUN npm ci
COPY src/AudiobookOrganizer.Client/ ./
RUN npm run build

# Stage 2: publish the API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/api
COPY src/AudiobookOrganizer.Api/AudiobookOrganizer.Api.csproj ./
RUN dotnet restore
COPY src/AudiobookOrganizer.Api/ ./
RUN dotnet publish -c Release --no-restore -o /app

# Test stage: runs the backend suite on Linux with the runtime's ffmpeg.
# Run it with `docker build --target test .`; the runtime build doesn't depend on it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS test
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY src/AudiobookOrganizer.Api/ src/AudiobookOrganizer.Api/
COPY src/AudiobookOrganizer.Api.Tests/ src/AudiobookOrganizer.Api.Tests/
COPY docs/fixtures/ docs/fixtures/
RUN dotnet restore src/AudiobookOrganizer.Api.Tests
RUN ffmpeg -version | head -n 1 && dotnet test src/AudiobookOrganizer.Api.Tests --no-restore

# Stage 3: runtime.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=client /src/client/dist ./wwwroot
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data /incoming /library
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Database__Path=/data/audiobooks.db
EXPOSE 8080
ENTRYPOINT ["dotnet", "AudiobookOrganizer.Api.dll"]

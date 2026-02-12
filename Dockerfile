# Build stage — NativeAOT requires clang and linker
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
RUN apk add --no-cache clang build-base zlib-dev zlib-static
WORKDIR /src
COPY src/DrPodcast.csproj src/
RUN dotnet restore src/DrPodcast.csproj -r linux-musl-x64
COPY src/ src/
COPY site/ site/
COPY podcasts.json src/
RUN dotnet publish src/DrPodcast.csproj -c Release -r linux-musl-x64 \
    -p:DebugType=none -p:StripSymbols=true -p:StaticExecutable=true -o /out

# Runtime stage — static binary, no OS needed
FROM scratch
WORKDIR /app
COPY --from=build /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/
COPY --from=build /out/DrPodcast ./
COPY --from=build /out/podcasts.json ./
COPY --from=build /src/site/ site/
USER 1000

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    BASE_URL=http://localhost:8080 \
    REFRESH_INTERVAL_MINUTES=15
HEALTHCHECK --interval=30s --timeout=3s --start-period=60s --retries=3 \
  CMD ["./DrPodcast", "--healthcheck"]
ENTRYPOINT ["./DrPodcast"]

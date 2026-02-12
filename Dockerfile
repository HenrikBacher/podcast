# Build stage — NativeAOT requires clang and linker
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
RUN apk add --no-cache clang build-base zlib-dev
WORKDIR /src
COPY src/DrPodcast.csproj src/
RUN dotnet restore src/DrPodcast.csproj -r linux-musl-x64
COPY src/ src/
COPY site/ site/
COPY podcasts.json src/
RUN dotnet publish src/DrPodcast.csproj -c Release -r linux-musl-x64 \
    -p:DebugType=none -p:StripSymbols=true -o /out

# Runtime stage
FROM alpine:3
RUN apk add --no-cache libstdc++ && \
    addgroup -g 1000 appuser && adduser -D -u 1000 -G appuser appuser
WORKDIR /app
COPY --from=build /out/DrPodcast ./
COPY --from=build /out/podcasts.json ./
COPY --from=build /src/site/ site/
RUN mkdir -p output && chown -R appuser:appuser /app
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    BASE_URL=http://localhost:8080 \
    REFRESH_INTERVAL_MINUTES=15
HEALTHCHECK --interval=30s --timeout=3s --start-period=60s --retries=3 \
  CMD wget -q --spider http://localhost:8080/health || exit 1
ENTRYPOINT ["./DrPodcast"]

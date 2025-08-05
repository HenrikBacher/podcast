# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/out ./
COPY ./../podcasts.json ./
ENV API_KEY=""
ENV BASE_URL=""
ENTRYPOINT ["dotnet", "DrPodcast.dll"]
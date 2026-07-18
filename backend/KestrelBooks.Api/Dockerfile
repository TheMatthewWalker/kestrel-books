# Build context: REPO ROOT (docker build -f backend/KestrelBooks.Api/Dockerfile .)
# so the image contains both the API and the built web app.

# ---- Web build ----
FROM node:20-alpine AS webbuild
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ .
# vite.config outDir points outside /web — override to a local dist for the image
RUN npx vite build --outDir /webdist --emptyOutDir

# ---- API build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/KestrelBooks.Api/KestrelBooks.Api.csproj .
RUN dotnet restore
COPY backend/KestrelBooks.Api/ .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=webbuild /webdist ./wwwroot

# Storage/ holds Data Protection keys and (if not using S3) receipt images —
# ALWAYS mount a volume here or sessions/secrets break on every deploy.
VOLUME /app/Storage

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "KestrelBooks.Api.dll"]

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY KestrelBooks.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Storage/ holds Data Protection keys and (if not using S3) receipt images —
# ALWAYS mount a volume here or sessions/secrets break on every deploy.
VOLUME /app/Storage

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Run as the non-root user baked into the .NET 8 images.
USER app

ENTRYPOINT ["dotnet", "KestrelBooks.Api.dll"]

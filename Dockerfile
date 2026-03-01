# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["RagApi.sln", "./"]
COPY ["src/RagApi.Api/RagApi.Api.csproj", "src/RagApi.Api/"]
COPY ["src/RagApi.Application/RagApi.Application.csproj", "src/RagApi.Application/"]
COPY ["src/RagApi.Domain/RagApi.Domain.csproj", "src/RagApi.Domain/"]
COPY ["src/RagApi.Infrastructure/RagApi.Infrastructure.csproj", "src/RagApi.Infrastructure/"]

# Restore dependencies
RUN dotnet restore

# Copy everything else
COPY . .

# Build and publish
WORKDIR "/src/src/RagApi.Api"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos '' appuser

# Argha - 2026-03-01 - install curl; required by HEALTHCHECK (not in aspnet base image)
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Argha - 2026-03-01 - grant appuser write access to /app so SQLite can create ragapi.db at runtime
RUN chown appuser:appuser /app

USER appuser

COPY --from=build /app/publish .

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/api/system/health || exit 1

ENTRYPOINT ["dotnet", "RagApi.Api.dll"]

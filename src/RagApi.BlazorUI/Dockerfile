# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files for restore
COPY ["RagApi.sln", "./"]
COPY ["src/RagApi.BlazorUI/RagApi.BlazorUI.csproj", "src/RagApi.BlazorUI/"]

# Restore dependencies
RUN dotnet restore src/RagApi.BlazorUI/RagApi.BlazorUI.csproj

# Copy BlazorUI source
COPY src/RagApi.BlazorUI/ src/RagApi.BlazorUI/

# Publish
RUN dotnet publish src/RagApi.BlazorUI/RagApi.BlazorUI.csproj -c Release -o /app/publish

# Runtime stage — nginx serves the static Blazor WASM files
FROM nginx:alpine AS runtime

# Argha - 2026-03-01 - copy SPA nginx config for Blazor deep-link routing
COPY src/RagApi.BlazorUI/nginx.conf /etc/nginx/conf.d/default.conf

# Argha - 2026-03-01 - copy published wwwroot to nginx html directory
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

EXPOSE 80

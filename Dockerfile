# ──────────────────────────────────────────────────────────────────
# Stage 1 — Frontend build (Node.js)
# ──────────────────────────────────────────────────────────────────
FROM node:20-alpine AS frontend
WORKDIR /src/frontend

COPY frontend/package*.json ./
RUN npm ci --prefer-offline

COPY frontend/ ./
# Override outDir so it lands in a predictable location inside the container
RUN npx vite build --outDir /app-static

# ──────────────────────────────────────────────────────────────────
# Stage 2 — Backend build (.NET 8 SDK)
# ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend
WORKDIR /src

COPY backend/ServerCat.sln ./
COPY backend/ServerCat.Core/          ServerCat.Core/
COPY backend/ServerCat.Infrastructure/ ServerCat.Infrastructure/
COPY backend/ServerCat.Api/           ServerCat.Api/

RUN dotnet restore ServerCat.sln

RUN dotnet publish ServerCat.Api/ServerCat.Api.csproj \
    -c Release \
    -o /publish \
    --no-restore \
    -p:UseAppHost=false

# ──────────────────────────────────────────────────────────────────
# Stage 3 — Runtime image
# ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install curl for HEALTHCHECK
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published backend
COPY --from=backend /publish .

# Copy frontend static files into ASP.NET Core's wwwroot
COPY --from=frontend /app-static ./wwwroot/

# Create vault directory (persisted via volume mount)
RUN mkdir -p /app/vault && chmod 700 /app/vault

# Non-root user
RUN addgroup --system servercat && adduser --system --ingroup servercat servercat
RUN chown -R servercat:servercat /app
USER servercat

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -fs http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "ServerCat.Api.dll"]

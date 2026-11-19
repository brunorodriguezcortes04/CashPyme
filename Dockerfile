# syntax=docker/dockerfile:1
# Imagen única: la API .NET sirve también el frontend Angular desde wwwroot.
# Se construye desde la RAÍZ del repo (dockerContext: .).

# 1) Frontend Angular -> archivos estáticos
FROM node:22-alpine AS frontend
WORKDIR /frontend

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build -- --configuration production

# 2) Backend .NET
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Backend.csproj ./
RUN dotnet restore "Backend.csproj"

COPY backend/ ./
RUN dotnet publish "Backend.csproj" -c Release -o /app/publish --no-restore

# 3) Imagen final: API + frontend en wwwroot
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .
COPY --from=frontend /frontend/dist/frontend/browser ./wwwroot

# Render inyecta la variable PORT y espera que el servicio escuche en ese puerto.
EXPOSE 10000
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-10000} dotnet Backend.dll"]

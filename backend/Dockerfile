# Multi-stage build for the AttendanceApi ASP.NET Core project.
# Build context is ./backend (see docker-compose.yml), so paths below are
# relative to the backend/ folder root.

# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy only the csproj first so `dotnet restore` is cached as its own Docker
# layer and only reruns when a package reference actually changes, not on
# every source-code edit.
COPY src/AttendanceApi/AttendanceApi.csproj src/AttendanceApi/
RUN dotnet restore src/AttendanceApi/AttendanceApi.csproj

COPY src/AttendanceApi/ src/AttendanceApi/
RUN dotnet publish src/AttendanceApi/AttendanceApi.csproj \
    -c Release -o /app/publish --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

# Non-root user for defense in depth
RUN adduser -D -u 1000 appuser
COPY --from=build /app/publish .
USER appuser

EXPOSE 8080
ENTRYPOINT ["dotnet", "AttendanceApi.dll"]

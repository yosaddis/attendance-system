FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/AttendanceApi/AttendanceApi.csproj src/AttendanceApi/
RUN dotnet restore src/AttendanceApi/AttendanceApi.csproj

COPY src/AttendanceApi/ src/AttendanceApi/
RUN dotnet publish src/AttendanceApi/AttendanceApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AttendanceApi.dll"]

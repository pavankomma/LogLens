# Build:  docker build -t loglens/viewer -f src/LogLens.Viewer/Dockerfile .
# Run:    docker run -p 5341:5341 -v ./logs:/logs loglens/viewer --tail /logs --api-key s3cret
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/LogLens/LogLens.csproj src/LogLens/
COPY src/LogLens.Viewer/LogLens.Viewer.csproj src/LogLens.Viewer/
RUN dotnet restore src/LogLens.Viewer/LogLens.Viewer.csproj
COPY README.md ./
COPY src/LogLens src/LogLens
COPY src/LogLens.Viewer src/LogLens.Viewer
RUN dotnet publish src/LogLens.Viewer/LogLens.Viewer.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5341
EXPOSE 5341
USER $APP_UID
ENTRYPOINT ["dotnet", "LogLens.Viewer.dll"]

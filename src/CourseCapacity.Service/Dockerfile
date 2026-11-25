FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Shared.Contracts/Shared.Contracts.csproj src/Shared.Contracts/
COPY src/CourseCapacity.Service/CourseCapacity.Service.csproj src/CourseCapacity.Service/
RUN dotnet restore src/CourseCapacity.Service/CourseCapacity.Service.csproj
COPY src/Shared.Contracts src/Shared.Contracts
COPY src/CourseCapacity.Service src/CourseCapacity.Service
RUN dotnet publish src/CourseCapacity.Service/CourseCapacity.Service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080 8081
USER 1654
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=6 \
    CMD curl --fail --silent http://127.0.0.1:8081/health/ready || exit 1
ENTRYPOINT ["dotnet", "CourseCapacity.Service.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Student.Web/Student.Web.csproj src/Student.Web/
RUN dotnet restore src/Student.Web/Student.Web.csproj
COPY src/Student.Web src/Student.Web
RUN dotnet publish src/Student.Web/Student.Web.csproj -c Release -o /app/publish --no-restore

FROM nginx:1.31.10-alpine3.24-slim AS runtime
COPY infrastructure/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/ || exit 1

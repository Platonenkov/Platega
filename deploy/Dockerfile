# Builds one of the demo apps: --build-arg APP=Platega.Demo.Shop or APP=Platega.Demo.Admin
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG APP
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Platega.Client/Platega.Client.csproj src/Platega.Client/
COPY src/Platega.Client.AspNetCore/Platega.Client.AspNetCore.csproj src/Platega.Client.AspNetCore/
COPY samples/Platega.Demo.Data/Platega.Demo.Data.csproj samples/Platega.Demo.Data/
COPY samples/${APP}/${APP}.csproj samples/${APP}/
RUN dotnet restore samples/${APP}/${APP}.csproj

COPY src/ src/
COPY samples/Platega.Demo.Data/ samples/Platega.Demo.Data/
COPY samples/${APP}/ samples/${APP}/
RUN dotnet publish samples/${APP}/${APP}.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG APP
ENV APP_DLL=${APP}.dll \
    ASPNETCORE_HTTP_PORTS=8080 \
    Demo__DatabasePath=/data/demo.db \
    Demo__KeysPath=/data/keys
WORKDIR /app
RUN mkdir -p /data/keys && chown -R $APP_UID /data
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]

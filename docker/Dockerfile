# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY HERM-MAPPER-APP.sln ./
COPY src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj src/HERM-MAPPER-APP/

RUN dotnet restore src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj

COPY src/HERM-MAPPER-APP/ src/HERM-MAPPER-APP/

RUN dotnet publish src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

RUN mkdir -p /app/App_Data /app/output \
    && chown -R ${APP_UID}:${APP_UID} /app/App_Data /app/output

USER ${APP_UID}
EXPOSE 8080

ENTRYPOINT ["dotnet", "HERM-MAPPER-APP.dll"]

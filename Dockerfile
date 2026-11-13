FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json ./
COPY be/ be/
COPY database/ database/
RUN dotnet publish be/DroneOps/DroneOps.API/DroneOps.API.csproj \
    --configuration Release --output /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    PORT=8080 \
    Monitoring__Enabled=false \
    Monitoring__DedicatedProcessRequired=true
COPY --from=build --chown=app:app /out/ ./
USER app
EXPOSE 8080
CMD ["dotnet", "DroneOps.API.dll"]

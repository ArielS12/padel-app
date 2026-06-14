FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY server/src/Padel.Api/Padel.Api.csproj server/src/Padel.Api/
RUN dotnet restore server/src/Padel.Api/Padel.Api.csproj

COPY server/ server/
RUN dotnet publish server/src/Padel.Api/Padel.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Padel.Api.dll"]

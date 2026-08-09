FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY Mahatati.SyncServer.csproj ./
RUN dotnet restore Mahatati.SyncServer.csproj
COPY . ./
RUN dotnet publish Mahatati.SyncServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "Mahatati.SyncServer.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DocIntelApi/DocIntelApi.csproj DocIntelApi/
RUN dotnet restore DocIntelApi/DocIntelApi.csproj

COPY DocIntelApi/ DocIntelApi/
RUN dotnet publish DocIntelApi/DocIntelApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DocIntelApi.dll"]

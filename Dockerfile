FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/BreweryCrawl.Api/BreweryCrawl.Api.csproj src/BreweryCrawl.Api/
RUN dotnet restore src/BreweryCrawl.Api/BreweryCrawl.Api.csproj
COPY src/ src/
RUN dotnet publish src/BreweryCrawl.Api/BreweryCrawl.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ConnectionStrings__Crawls="Data Source=/home/app/crawls.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "BreweryCrawl.Api.dll"]

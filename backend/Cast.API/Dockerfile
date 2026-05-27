FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/Cast.API/Cast.API.csproj      backend/Cast.API/
COPY shared/Cast.Shared/Cast.Shared.csproj  shared/Cast.Shared/
RUN dotnet restore backend/Cast.API/Cast.API.csproj

COPY backend/ backend/
COPY shared/  shared/
RUN dotnet publish backend/Cast.API/Cast.API.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Cast.API.dll"]
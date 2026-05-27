FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY gateway/Cast.Gateway/Cast.Gateway.csproj gateway/Cast.Gateway/
COPY shared/Cast.Shared/Cast.Shared.csproj shared/Cast.Shared/
RUN dotnet restore gateway/Cast.Gateway/Cast.Gateway.csproj

COPY gateway/ gateway/
COPY shared/ shared/
RUN dotnet publish gateway/Cast.Gateway/Cast.Gateway.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Cast.Gateway.dll"]
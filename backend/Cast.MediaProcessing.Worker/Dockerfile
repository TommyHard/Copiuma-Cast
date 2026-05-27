FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/Cast.MediaProcessing.Worker/Cast.MediaProcessing.Worker.csproj backend/Cast.MediaProcessing.Worker/
COPY shared/Cast.Shared/Cast.Shared.csproj shared/Cast.Shared/
RUN dotnet restore backend/Cast.MediaProcessing.Worker/Cast.MediaProcessing.Worker.csproj

COPY backend/ backend/
COPY shared/ shared/
RUN dotnet publish backend/Cast.MediaProcessing.Worker/Cast.MediaProcessing.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Cast.MediaProcessing.Worker.dll"]
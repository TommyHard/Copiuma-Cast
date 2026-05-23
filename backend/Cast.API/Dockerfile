# syntax=docker/dockerfile:1
# ===================================================================
#  Cast.API — ASP.NET Core (.NET 8)
#  Build context MUST be the repository root so that project
#  references such as shared/Cast.Shared resolve correctly:
#
#      docker build -f backend/Cast.API/Dockerfile -t cast-api .
# ===================================================================

# ---- build stage --------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the project files first to maximise layer caching on restore.
COPY backend/Cast.API/Cast.API.csproj      backend/Cast.API/
COPY shared/Cast.Shared/Cast.Shared.csproj  shared/Cast.Shared/
RUN dotnet restore backend/Cast.API/Cast.API.csproj

# Copy the rest of the sources and publish.
COPY backend/ backend/
COPY shared/  shared/
RUN dotnet publish backend/Cast.API/Cast.API.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime stage ------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Kestrel listens on 8080 inside the container (non-root friendly).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Cast.API.dll"]

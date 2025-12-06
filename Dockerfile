# Base stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
# Do NOT set ASPNETCORE_ENVIRONMENT here; default is Production

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Application.csproj", "."]
RUN dotnet restore "./Application.csproj"
COPY . . 
WORKDIR "/src/."
RUN dotnet build "./Application.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Application.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish . 
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
# Do NOT set ASPNETCORE_ENVIRONMENT here; default is Production
ENTRYPOINT ["dotnet", "Application.dll"]

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for layer caching
COPY ECommerce.sln ./
COPY src/ECommerce.Domain/ECommerce.Domain.csproj src/ECommerce.Domain/
COPY src/ECommerce.Application/ECommerce.Application.csproj src/ECommerce.Application/
COPY src/ECommerce.Infrastructure/ECommerce.Infrastructure.csproj src/ECommerce.Infrastructure/
COPY src/ECommerce.Api/ECommerce.Api.csproj src/ECommerce.Api/
COPY tests/ECommerce.Domain.UnitTests/ECommerce.Domain.UnitTests.csproj tests/ECommerce.Domain.UnitTests/
COPY tests/ECommerce.Application.UnitTests/ECommerce.Application.UnitTests.csproj tests/ECommerce.Application.UnitTests/
COPY tests/ECommerce.Api.IntegrationTests/ECommerce.Api.IntegrationTests.csproj tests/ECommerce.Api.IntegrationTests/

# Restore dependencies
RUN dotnet restore ECommerce.sln

# Copy remaining source code
COPY . .

# Build and publish
RUN dotnet publish src/ECommerce.Api/ECommerce.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
USER app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ECommerce.Api.dll"]

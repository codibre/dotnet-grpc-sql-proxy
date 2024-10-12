FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy everything
# copy csproj and restore as distinct layers
COPY . .
RUN dotnet restore
RUN dotnet publish "src/Codibre.GrpcSqlProxy.Api/Codibre.GrpcSqlProxy.Api.csproj"  -c release -o /app --no-restore

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./

ENTRYPOINT ["dotnet", "Codibre.GrpcSqlProxy.Api.dll"]
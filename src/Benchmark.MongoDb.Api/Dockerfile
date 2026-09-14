FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Benchmark.Shared/Benchmark.Shared.csproj Benchmark.Shared/
COPY src/Benchmark.MongoDb.Api/Benchmark.MongoDb.Api.csproj Benchmark.MongoDb.Api/
RUN dotnet restore Benchmark.MongoDb.Api/Benchmark.MongoDb.Api.csproj

COPY src/Benchmark.Shared/ Benchmark.Shared/
COPY src/Benchmark.MongoDb.Api/ Benchmark.MongoDb.Api/
RUN dotnet publish Benchmark.MongoDb.Api/Benchmark.MongoDb.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Benchmark.MongoDb.Api.dll"]

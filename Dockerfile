# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY LayaDotNetPoc.sln ./
COPY src/Laya.Core/Laya.Core.csproj src/Laya.Core/
COPY src/Laya.Console/Laya.Console.csproj src/Laya.Console/
RUN dotnet restore src/Laya.Console/Laya.Console.csproj

COPY src/ src/
RUN dotnet publish src/Laya.Console/Laya.Console.csproj \
    --configuration Release \
    --output /app/publish \
    --self-contained false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Model files are supplied as a read-only volume and reports use a separate volume.
VOLUME ["/models", "/reports"]
ENTRYPOINT ["dotnet", "Laya.Console.dll"]

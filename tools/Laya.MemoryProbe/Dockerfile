# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY LayaDotNetPoc.sln ./
COPY src/Laya.Core/Laya.Core.csproj src/Laya.Core/
COPY src/Shared/Configuration/LayaProfileResolver.cs src/Shared/Configuration/
COPY benchmarks/Laya.MemoryBenchmarks/Analysis/MemoryPolicy.cs benchmarks/Laya.MemoryBenchmarks/Analysis/
COPY benchmarks/Laya.MemoryBenchmarks/Analysis/MemoryPolicyLoader.cs benchmarks/Laya.MemoryBenchmarks/Analysis/
COPY tools/Laya.MemoryProbe/Laya.MemoryProbe.csproj tools/Laya.MemoryProbe/
COPY test-data/phase3a/memory-workloads.json test-data/phase3a/
COPY test-data/phase3a/memory-workloads-manifest.json test-data/phase3a/
RUN dotnet restore tools/Laya.MemoryProbe/Laya.MemoryProbe.csproj

COPY src/ src/
COPY tools/Laya.MemoryProbe/ tools/Laya.MemoryProbe/
COPY benchmarks/Laya.MemoryBenchmarks/Analysis/MemoryPolicy.cs benchmarks/Laya.MemoryBenchmarks/Analysis/
COPY benchmarks/Laya.MemoryBenchmarks/Analysis/MemoryPolicyLoader.cs benchmarks/Laya.MemoryBenchmarks/Analysis/
COPY test-data/phase3a/ test-data/phase3a/
RUN dotnet publish tools/Laya.MemoryProbe/Laya.MemoryProbe.csproj \
    --configuration Release \
    --output /app/publish \
    --self-contained false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 模型唯讀掛載、run evidence 寫入獨立 reports volume。
VOLUME ["/models", "/reports"]
ENTRYPOINT ["dotnet", "Laya.MemoryProbe.dll"]

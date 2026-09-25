FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files to restore dependencies
COPY ["AudioTranscription.slnx", "./"]
COPY ["AudioTranscription.Api/AudioTranscription.Api.csproj", "AudioTranscription.Api/"]
COPY ["AudioTranscription.Domain/AudioTranscription.Domain.csproj", "AudioTranscription.Domain/"]
COPY ["AudioTranscription.Infrastructure/AudioTranscription.Infrastructure.csproj", "AudioTranscription.Infrastructure/"]
COPY ["AudioTranscription.ServiceDefaults/AudioTranscription.ServiceDefaults.csproj", "AudioTranscription.ServiceDefaults/"]

RUN dotnet restore "AudioTranscription.Api/AudioTranscription.Api.csproj"

# Copy full source and build
COPY . .
WORKDIR "/src/AudioTranscription.Api"
RUN dotnet build "AudioTranscription.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AudioTranscription.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose port and start
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AudioTranscription.Api.dll"]

# --- S15-T3: CUDA-enabled image, built with `docker build --target final-cuda ...` -------------
# Separate publish output because Whisper.net.Runtime.Cuda (opt-in via IncludeCudaRuntime, see the
# Infrastructure csproj) should not ship in the default, CPU-only image above.
FROM build AS publish-cuda
RUN dotnet publish "AudioTranscription.Api.csproj" -c Release -o /app/publish-cuda /p:UseAppHost=false /p:IncludeCudaRuntime=true

# CUDA runtime base image instead of dotnet/aspnet: only it carries libcudart/libcublas etc.,
# which Whisper.net.Runtime.Cuda's native library needs at load time.
FROM nvidia/cuda:12.4.1-runtime-ubuntu22.04 AS final-cuda
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends wget ca-certificates \
    && wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends aspnetcore-runtime-10.0 \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

COPY --from=publish-cuda /app/publish-cuda .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# Cuda12 covers newer CUDA toolkits than the plain "Cuda" (11.x) native library; Cpu is the
# fallback if the container ever runs without a GPU attached (S15-T2).
ENV Whisper__RuntimeOrder__0=Cuda12
ENV Whisper__RuntimeOrder__1=Cuda
ENV Whisper__RuntimeOrder__2=Cpu
ENTRYPOINT ["dotnet", "AudioTranscription.Api.dll"]

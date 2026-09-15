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

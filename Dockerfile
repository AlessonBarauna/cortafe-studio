# Cortafé Studio em servidor Linux: .NET 8 + FFmpeg + Python (Whisper, OpenCV, Cortafé FX) + yt-dlp + Node.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CortaFeStudio.sln NuGet.Config ./
COPY src/CortaFeStudio.Api/CortaFeStudio.Api.csproj src/CortaFeStudio.Api/
COPY tests/CortaFeStudio.Tests/CortaFeStudio.Tests.csproj tests/CortaFeStudio.Tests/
RUN dotnet restore src/CortaFeStudio.Api/CortaFeStudio.Api.csproj --configfile NuGet.Config
COPY src/ src/
RUN dotnet publish src/CortaFeStudio.Api/CortaFeStudio.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends \
      python3 python3-venv python3-pip fontconfig fonts-liberation fonts-dejavu-core \
      libgl1 libglib2.0-0 libfontconfig1 libfreetype6 ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=mwader/static-ffmpeg:7.1 /ffmpeg /usr/local/bin/ffmpeg
COPY --from=mwader/static-ffmpeg:7.1 /ffprobe /usr/local/bin/ffprobe
COPY --from=node:22-bookworm-slim /usr/local/bin/node /usr/local/bin/node

RUN python3 -m venv /opt/venv && ln -s /opt/venv/bin/python /usr/local/bin/python
ENV PATH="/opt/venv/bin:${PATH}"
COPY src/CortaFeStudio.Api/scripts/fx/requirements.txt /tmp/fx-requirements.txt
RUN pip install --no-cache-dir faster-whisper "opencv-python-headless==4.10.0.84" yt-dlp -r /tmp/fx-requirements.txt

WORKDIR /app
COPY --from=build /app ./
COPY src/CortaFeStudio.Api/scripts ./scripts
# Modelo RNNoise (redução de ruído de voz, usado pelo filtro arnndn do FFmpeg)
RUN mkdir -p /app/tools/models && curl -fsSL https://raw.githubusercontent.com/GregorR/rnnoise-models/master/beguiling-drafter-2018-08-30/bd.rnnn -o /app/tools/models/rnnoise.rnnn

ENV ASPNETCORE_URLS=http://+:5088 \
    CORTAFE_ALLOW_PRIVATE_NETWORK=1 \
    HF_HOME=/app/storage/hf-cache
VOLUME /app/storage
EXPOSE 5088
ENTRYPOINT ["dotnet", "CortaFeStudio.Api.dll"]

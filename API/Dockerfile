FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Model/ Model/
COPY DAL/ DAL/
COPY API/ API/
RUN dotnet publish API/API.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /data/audio && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 Audio__Directory=/data/audio
EXPOSE 8080
ENTRYPOINT ["dotnet", "API.dll"]

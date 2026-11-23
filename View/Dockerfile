FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Model/ Model/
COPY View/ View/
RUN dotnet publish View/View.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER root
RUN mkdir -p /data/keys && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 DataProtection__Directory=/data/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "View.dll"]

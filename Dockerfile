# The public demo: the web app reading a read-only SQLite snapshot of the data
# (data/mma.db, made with `dotnet run -- export-sqlite ../../data/mma.db` in src/Web).
# Made for Hugging Face Spaces (see deploy/), but runs anywhere:
#   docker build -t mma-hub . && docker run -p 7860:7860 mma-hub

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Web/Web.csproj src/Web/
RUN dotnet restore src/Web/Web.csproj
COPY src/Web/ src/Web/
RUN dotnet publish src/Web/Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Hugging Face Spaces run containers as user 1000, which this image already has.
WORKDIR /app
COPY --from=build --chown=1000:1000 /app .
COPY --chown=1000:1000 data/mma.db data/mma.db
USER 1000

ENV ASPNETCORE_HTTP_PORTS=7860 \
    ASPNETCORE_ENVIRONMENT=Production \
    BehindHttpsProxy=true \
    ConnectionStrings__MmaSqlite="Data Source=/app/data/mma.db;Mode=ReadOnly"

EXPOSE 7860
ENTRYPOINT ["dotnet", "Web.dll"]

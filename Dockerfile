# Contexto de build: la raíz de este repositorio.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/TercerTiempo.Api/TercerTiempo.Api.csproj src/TercerTiempo.Api/
COPY src/TercerTiempo.Application/TercerTiempo.Application.csproj src/TercerTiempo.Application/
COPY src/TercerTiempo.Domain/TercerTiempo.Domain.csproj src/TercerTiempo.Domain/
COPY src/TercerTiempo.Infrastructure/TercerTiempo.Infrastructure.csproj src/TercerTiempo.Infrastructure/
COPY src/TercerTiempo.Repository/TercerTiempo.Repository.csproj src/TercerTiempo.Repository/
RUN dotnet restore src/TercerTiempo.Api/TercerTiempo.Api.csproj
COPY src/ src/
RUN dotnet publish src/TercerTiempo.Api/TercerTiempo.Api.csproj --no-restore -c Release -o /out /p:UseAppHost=false

# Etapa opcional, fuera de la imagen por defecto: docker build --target test .
FROM build AS test
COPY tests/ tests/
RUN dotnet test tests/TercerTiempo.Domain.Tests/TercerTiempo.Domain.Tests.csproj -c Release \
 && dotnet test tests/TercerTiempo.Application.Tests/TercerTiempo.Application.Tests.csproj -c Release

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
# Cultura es-ES (fechas y mensajes) y hora de Europe/Madrid (calendarios de las ligas): Alpine no trae ICU ni tzdata.
RUN apk add --no-cache icu-libs icu-data-full tzdata
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ReverseProxy__Enabled=true \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Europe/Madrid
COPY --from=build /out/ ./
# Escudos, fotos y vídeos (Crests:Directory, Crests:PhotosDirectory, Videos:Directory) viven en el volumen /app/data.
RUN mkdir -p /app/data/crests /app/data/photos /app/data/videos && chown -R app:app /app/data
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=60s --retries=6 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/api/health || exit 1
ENTRYPOINT ["dotnet", "TercerTiempo.Api.dll"]

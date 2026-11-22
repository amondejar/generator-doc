# --- Compilación ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GeneradorDocs.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

# --- Ejecución ---
# Claude no se ejecuta aquí: el trabajo lo hace el agente en la infraestructura de Anthropic.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ENV ASPNETCORE_ENVIRONMENT=Production \
    WORK_DIR=/tmp/trabajos
USER app
WORKDIR /app
COPY --from=build --chown=app /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "GeneradorDocs.dll"]

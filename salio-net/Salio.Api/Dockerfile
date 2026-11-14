# Salio API. Build from salio-net/, not from this folder:
#   docker build -f Salio.Api/Dockerfile -t salio-api:TAG .
#   docker build -f Salio.Api/Dockerfile --target migrate -t salio-migrate:TAG .
#
# Two images come out of this one file:
#   runtime (the default) - the API itself
#   migrate               - applies EF migrations, then exits
# Nothing secret is baked in. The connection string arrives at run time
# as the ConnectionStrings__Default environment variable.

# ---- Build: the SDK, compiler and NuGet. Never shipped. ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so a code change does not re-download every package.
COPY Salio.Domain/Salio.Domain.csproj Salio.Domain/
COPY Salio.Infrastructure/Salio.Infrastructure.csproj Salio.Infrastructure/
COPY Salio.Api/Salio.Api.csproj Salio.Api/
RUN dotnet restore Salio.Api/Salio.Api.csproj

COPY Salio.Domain/ Salio.Domain/
COPY Salio.Infrastructure/ Salio.Infrastructure/
COPY Salio.Api/ Salio.Api/
RUN dotnet publish Salio.Api/Salio.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Two folders the API writes to. They are made here (the runtime image has
# no shell to make them with) and copied in below, owned by the non-root
# user 1654. A named volume mounted over an empty folder takes that
# folder's owner the first time, so the volumes in docker-compose.yml start
# out writable by the API.
#   /dpkeys   - the keys that encrypt the login cookie. Without a volume
#               every restart would make new keys and sign every phone out.
#   /invoices - uploaded supplier invoices (InvoiceStorage__Path).
RUN mkdir -p /dpkeys /invoices

# ---- Migration bundle: one executable holding every migration. ----
FROM build AS bundle
# Same version as Microsoft.EntityFrameworkCore.Design in Salio.Api.csproj.
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
    --project Salio.Infrastructure \
    --startup-project Salio.Api \
    --configuration Release \
    --output /app/efbundle

# ---- migrate: run once per release, before the API starts. ----
# Chiseled: no shell, no package manager, and a non-root user by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS migrate
WORKDIR /app
COPY --from=bundle /app/efbundle .
USER 1654
ENTRYPOINT ["./efbundle"]

# ---- runtime: the API. Last, so it is what a plain docker build makes. ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build --chown=1654:1654 /dpkeys /home/app/.aspnet/DataProtection-Keys
COPY --from=build --chown=1654:1654 /invoices /var/lib/salio/invoices

# 1654 is the image's built-in "app" user. Never root.
USER 1654
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Salio.Api.dll"]

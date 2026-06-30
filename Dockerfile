# Multi-stage build of the whole app into ONE runtime image, matching the repo's
# "run for real" model (quickstart.md / README §3): Node builds the SPA, then the
# ASP.NET Core host serves that static SPA plus /api from wwwroot — no Node at runtime.

# ---- Stage 1: build the React/Vite SPA ----
FROM node:22-alpine AS frontend
WORKDIR /src/frontend
# Restore deps against the lockfile first for layer caching.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
# Emit to a local dist/ (override the config's wwwroot outDir, which lives outside this stage).
RUN npx tsc -b && npx vite build --outDir dist --emptyOutDir

# ---- Stage 2: restore + publish the .NET host ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
# Copy build props + project files first so `restore` caches independently of source churn.
COPY backend/Directory.Build.props backend/Directory.Packages.props ./backend/
COPY backend/src/Domain/Domain.csproj ./backend/src/Domain/
COPY backend/src/Application/Application.csproj ./backend/src/Application/
COPY backend/src/Infrastructure/Infrastructure.csproj ./backend/src/Infrastructure/
COPY backend/src/Web/Web.csproj ./backend/src/Web/
RUN dotnet restore backend/src/Web/Web.csproj
# Now the full backend source, plus the built SPA into the Web project's wwwroot.
COPY backend/ ./backend/
COPY --from=frontend /src/frontend/dist/ ./backend/src/Web/wwwroot/
RUN dotnet publish backend/src/Web/Web.csproj -c Release -o /app --no-restore

# ---- Stage 3: lean runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /app ./
# Production disables the dev-only SpaProxy; Kestrel serves the static SPA from wwwroot.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Cv__StoragePath=/app/cv-data
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobOfferMatcher.Web.dll"]

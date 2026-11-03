FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5020
ENV ASPNETCORE_URLS=http://+:5020

# The app talks to the Docker engine over /var/run/docker.sock. To read it as a
# non-root user, appuser must belong to a group whose GID matches the host socket's
# group. Override DOCKER_GID at build time to match your host (`stat -c '%g' /var/run/docker.sock`).
ARG DOCKER_GID=988
RUN groupadd --gid ${DOCKER_GID} docker && \
    useradd --uid 5678 --gid ${DOCKER_GID} --no-create-home --shell /usr/sbin/nologin appuser && \
    mkdir -p /app/data && chown -R appuser:docker /app
USER appuser

FROM node:22-bookworm-slim AS vuebuild
WORKDIR /src
COPY ["Track.Vue", "Track.Vue"]
WORKDIR /src/Track.Vue
RUN npm ci && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
# Named APP_VERSION, not "version": BuildKit exposes ARGs as env vars in RUN, and MSBuild
# imports env vars as properties case-insensitively, so a lowercase "version" would leak in
# as the Version property and break the build with the branch name on non-tag builds.
ARG APP_VERSION=
# .git is not in the build context, so the SDK can't find the commit for the version itself.
ARG GIT_SHA=
WORKDIR /src
COPY . .
WORKDIR /src/Track.Website
# metadata-action feeds the branch name (e.g. "master") as the version on non-tag builds,
# which is not a valid MSBuild version. Only override when it parses as a real version;
# otherwise fall back to the csproj <VersionPrefix>.
RUN if echo "${APP_VERSION}" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+'; then \
        dotnet publish "Track.Website.csproj" -c Release -o /app/publish "/p:Version=${APP_VERSION}" "/p:SourceRevisionId=${GIT_SHA}"; \
    else \
        dotnet publish "Track.Website.csproj" -c Release -o /app/publish "/p:SourceRevisionId=${GIT_SHA}"; \
    fi

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=vuebuild /src/Track.Website/wwwroot wwwroot
USER root
RUN chown -R appuser:docker /app
USER appuser
ENTRYPOINT ["dotnet", "Track.Website.dll"]

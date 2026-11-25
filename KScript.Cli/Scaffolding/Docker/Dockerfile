# Build the KScript revision supplied by compose.yaml on the builder's native CPU.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim@sha256:1b955f179b02df4f0eb5fadedaa898cbe44d620a64aab52eec72c8e91d1355b5 AS build
ARG TARGETARCH
ARG KSCRIPT_VERSION
WORKDIR /src
RUN --mount=from=kscript_source,target=/checkout \
    --mount=type=cache,id=kscript-runtime-nuget,target=/root/.nuget/packages,sharing=locked \
    cd /checkout && tar --exclude=bin --exclude=obj -cf - Directory.Build.props KScript.* KScriptApp docs | tar -xf - -C /src && \
    cd /src && dotnet publish KScriptApp/KScriptApp.csproj -c Release -a "$TARGETARCH" \
        --self-contained false -p:UseAppHost=false -p:Version="$KSCRIPT_VERSION" -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim@sha256:dccacae9e0e103b5d3ba5fbe442dd8d3316b990cd8f2ec9be0a0ec9db0c0e520
COPY --from=build /out /opt/kscript
RUN printf '#!/bin/sh\nexec dotnet /opt/kscript/KScript.dll "$@"\n' > /usr/local/bin/kscript \
    && chmod 755 /usr/local/bin/kscript \
    && install -d -o app -g app -m 700 /var/lib/kscript
ENV KSCRIPT_SERVE_IN_DOCKER=1 KSCRIPT_ENV=production KSCRIPT_STORAGE_ROOT=/var/lib/kscript/storage KSCRIPT_PROJECT_DATA_ROOT=/var/lib/kscript/project
WORKDIR /app
COPY . .
USER app
EXPOSE {{ port }}
# Probe the framework's built-in runtime asset, including on API-only sites.
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s \
    CMD bash -ec 'exec 3<>/dev/tcp/127.0.0.1/{{ port }}; printf "GET /_ks/live.js HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3; read -r reply <&3; [[ "$reply" == HTTP/*" 200 "* ]]'
ENTRYPOINT ["kscript"]
CMD ["serve", "--host", "0.0.0.0", "--migrate"]

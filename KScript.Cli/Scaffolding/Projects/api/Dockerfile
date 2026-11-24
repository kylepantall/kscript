--- make
when: docker
---
# {{ title }} in a container: docker compose up (see compose.yaml), or docker build -t {{ kebab }} .
FROM debian:bookworm-slim

RUN apt-get update \
  && apt-get install -y --no-install-recommends ca-certificates curl libicu72 \
  && rm -rf /var/lib/apt/lists/*

# The kscript CLI, from the latest release. Pin one with --build-arg KSCRIPT_VERSION=v0.6.1.
ARG KSCRIPT_VERSION=latest
RUN curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh \
  | KSCRIPT_VERSION=$KSCRIPT_VERSION KSCRIPT_INSTALL=/opt/kscript KSCRIPT_NO_MODIFY_PATH=1 sh
ENV PATH="/opt/kscript/bin:$PATH" KSCRIPT_SERVE_IN_DOCKER=1

WORKDIR /app
COPY . .
RUN kscript check

EXPOSE {{ port }}
CMD ["kscript", "serve", "--host", "0.0.0.0", "--port", "{{ port }}"]

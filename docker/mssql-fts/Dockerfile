# SQL Server with Full-Text Search (S13, ADR 0005). The base image does NOT include Full-Text
# Search: it is a separate apt package, and it lives in its own per-version Microsoft repository
# (not the general "prod" repo the base image already trusts), which must be added explicitly
# before it can be installed - see https://github.com/microsoft/mssql-docker/issues/422.
FROM mcr.microsoft.com/mssql/server:2022-latest

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list \
        -o /etc/apt/sources.list.d/mssql-server-2022.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends mssql-server-fts \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
USER mssql

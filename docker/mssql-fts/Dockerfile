# SQL Server with Full-Text Search (S13, ADR 0005). The base image does NOT include Full-Text
# Search: it is a separate apt package that must be installed explicitly, on top of the base image.
FROM mcr.microsoft.com/mssql/server:2022-latest

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends mssql-server-fts \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
USER mssql

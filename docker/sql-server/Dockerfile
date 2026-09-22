# SQL Server with full-text search.
#
# The official image does not include it, and the search this project implements is built on
# CONTAINS/FREETEXT (ADR-005). The package lives in the SQL Server repository, which the image does
# not configure — it ships only the tools repository — so the repository is added here and the
# package resolves to the same build as the server already in the image.
FROM mcr.microsoft.com/mssql/server:2022-latest

USER root

RUN echo "deb [arch=amd64] https://packages.microsoft.com/ubuntu/22.04/mssql-server-2022 jammy main" \
        > /etc/apt/sources.list.d/mssql-server.list \
    && apt-get update \
    && ACCEPT_EULA=Y DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends mssql-server-fts \
    && rm -rf /var/lib/apt/lists/*

USER mssql

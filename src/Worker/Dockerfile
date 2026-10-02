# Imagem do worker.
#
# Construida a partir da raiz do repositorio:
#
#   docker build -f src/Worker/Dockerfile -t docops-worker .
#
# Processo a parte da API de proposito: retoma de temporizadores, escalonamento de prazos, fila de
# IA, varrimento das integracoes e verificacao de retencao. Nenhum pedido de utilizador espera por
# trabalho periodico.
#
# Varios worker podem correr em paralelo sem repetirem trabalho: cada tarefa toma um bloqueio
# aplicacional antes de correr (`IJobLock`, sobre `sp_getapplock`).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Analyzers/DocOps.Analyzers.csproj      src/Analyzers/
COPY src/Domain/DocOps.Domain.csproj            src/Domain/
COPY src/Application/DocOps.Application.csproj  src/Application/
COPY src/Infrastructure/DocOps.Infrastructure.csproj src/Infrastructure/
COPY src/Worker/DocOps.Worker.csproj            src/Worker/

RUN dotnet restore src/Worker/DocOps.Worker.csproj

# Os documentos de exemplo vivem na raiz, nao em src/: a Infrastructure embebe-os com
# `..\..\samples\*.txt`. Sem esta linha a imagem compila na mesma, porque um padrao de recurso
# embebido que nao encontra nada nao da erro nenhum, e so falha ao semear.
COPY samples/ samples/
COPY src/ src/

RUN dotnet publish src/Worker/DocOps.Worker.csproj -c Release -o /app --no-restore


FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Runtime e nao aspnet: o worker nao serve HTTP nenhum, e uma imagem com ASP.NET Core seria
# superficie a mais sem utilidade.
RUN mkdir -p /data/storage && chown -R app:app /data

USER app

# Le a mesma pasta de armazenamento da API: a importacao de ficheiros e as integracoes mexem nos
# documentos que la estao.
ENV FileStorage__RootPath=/data/storage

VOLUME ["/data/storage"]

COPY --from=build --chown=app:app /app ./

ENTRYPOINT ["dotnet", "DocOps.Worker.dll"]

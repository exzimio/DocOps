# Imagem do worker.
#
# Construída a partir da raiz do repositório:
#
#   docker build -f src/Worker/Dockerfile -t docops-worker .
#
# Processo à parte da API, de propósito: retoma de temporizadores, escalonamento de prazos, fila de
# IA, varrimento das integrações e verificação de retenção. Nenhum pedido de utilizador espera por
# trabalho periódico.
#
# Vários workers podem correr em paralelo sem repetirem trabalho: cada tarefa toma um bloqueio
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

# Os documentos de exemplo vivem na raiz, e não em src/: a Infrastructure embebe-os com
# `..\..\samples\*.txt`. Sem esta linha a imagem compila na mesma, porque um padrão de recurso
# embebido que não encontra nada não dá erro nenhum, e só falha ao semear.
COPY samples/ samples/
COPY src/ src/

RUN dotnet publish src/Worker/DocOps.Worker.csproj -c Release -o /app --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# A imagem do ASP.NET Core, e não a base do .NET, embora o worker não sirva HTTP: a infraestrutura
# que partilha com a API usa o Identity e a proteção de dados, e sem essa framework o processo nem
# arranca.
RUN mkdir -p /data/storage && chown -R app:app /data

USER app

# Lê a mesma pasta de armazenamento da API: a importação de ficheiros e as integrações mexem nos
# documentos que lá estão.
ENV FileStorage__RootPath=/data/storage

VOLUME ["/data/storage"]

COPY --from=build --chown=app:app /app ./

ENTRYPOINT ["dotnet", "DocOps.Worker.dll"]

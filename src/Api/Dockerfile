# Imagem da API.
#
# Construída a partir da raiz do repositório, e não desta pasta: a API depende de quatro projetos
# irmãos, e um contexto limitado a `src/Api` não os alcança.
#
#   docker build -f src/Api/Dockerfile -t docops-api .
#
# Os ficheiros de projeto são copiados antes do código, de propósito: o `restore` é a camada cara, e
# só deve voltar a correr quando as dependências mudam, e não a cada linha escrita.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Analyzers/DocOps.Analyzers.csproj      src/Analyzers/
COPY src/Domain/DocOps.Domain.csproj            src/Domain/
COPY src/Application/DocOps.Application.csproj  src/Application/
COPY src/Infrastructure/DocOps.Infrastructure.csproj src/Infrastructure/
COPY src/Api/DocOps.Api.csproj                  src/Api/

RUN dotnet restore src/Api/DocOps.Api.csproj

# Os documentos de exemplo vivem na raiz, e não em src/: a Infrastructure embebe-os com
# `..\..\samples\*.txt`. Sem esta linha a imagem compila na mesma, porque um padrão de recurso
# embebido que não encontra nada não dá erro nenhum, e só falha ao semear.
COPY samples/ samples/
COPY src/ src/

# `--no-restore` porque a camada acima já o fez; sem isto a cache de dependências não serve de nada.
RUN dotnet publish src/Api/DocOps.Api.csproj -c Release -o /app --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# A imagem de execução não traz `curl` nem `wget`, e sem um deles a verificação de saúde do compose
# acusa o produto de uma avaria que é dela: a aplicação responde, e quem não consegue perguntar é a
# sonda. Instalado com o mínimo e sem deixar a cache do apt para trás.
#
# Troca assumida: um binário a mais numa imagem de produção é superfície a mais. Vale a pena porque
# uma sonda que nunca passa torna o `depends_on: service_healthy` inútil, e isso é pior.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Sem privilégios. A imagem base já traz o utilizador `app`, e usa-se em vez de correr como root,
# porque um processo que só serve HTTP não tem nada que poder escrever fora do que lhe compete.
# A pasta é criada e atribuída **antes** de trocar de utilizador: um volume nomeado herda dono e
# permissões do que a imagem tiver nesse caminho na primeira utilização. Sem isto, o volume nasce de
# root e o processo sem privilégios não consegue escrever nele.
RUN mkdir -p /data/storage && chown -R app:app /data

USER app

# O armazenamento de ficheiros é local (`LocalFileStorage`). Fica num volume, senão os documentos
# carregados desaparecem com o contentor.
ENV ASPNETCORE_URLS=http://+:8080 \
    FileStorage__RootPath=/data/storage

EXPOSE 8080
VOLUME ["/data/storage"]

COPY --from=build --chown=app:app /app ./

ENTRYPOINT ["dotnet", "DocOps.Api.dll"]

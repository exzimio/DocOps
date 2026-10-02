# Imagem da API.
#
# Construida a partir da raiz do repositorio, nao desta pasta: a API depende de quatro projetos
# irmaos, e um contexto limitado a `src/Api` nao os alcanca.
#
#   docker build -f src/Api/Dockerfile -t docops-api .
#
# Os ficheiros de projeto sao copiados antes do codigo, de proposito: o `restore` e a camada cara, e
# so deve voltar a correr quando as dependencias mudam, nao a cada linha escrita.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Analyzers/DocOps.Analyzers.csproj      src/Analyzers/
COPY src/Domain/DocOps.Domain.csproj            src/Domain/
COPY src/Application/DocOps.Application.csproj  src/Application/
COPY src/Infrastructure/DocOps.Infrastructure.csproj src/Infrastructure/
COPY src/Api/DocOps.Api.csproj                  src/Api/

RUN dotnet restore src/Api/DocOps.Api.csproj

# Os documentos de exemplo vivem na raiz, nao em src/: a Infrastructure embebe-os com
# `..\..\samples\*.txt`. Sem esta linha a imagem compila na mesma, porque um padrao de recurso
# embebido que nao encontra nada nao da erro nenhum, e so falha ao semear.
COPY samples/ samples/
COPY src/ src/

# `--no-restore` porque a camada acima ja o fez; sem isto o cache de dependencias nao serve de nada.
RUN dotnet publish src/Api/DocOps.Api.csproj -c Release -o /app --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# A imagem de execucao nao traz `curl` nem `wget`, e sem um deles a verificacao de saude do compose
# acusa o produto de uma avaria que e dela: a aplicacao responde, quem nao consegue perguntar e a
# sonda. Instalado com o minimo e sem deixar o cache do apt para tras.
#
# Troca assumida: um binario a mais numa imagem de producao e superficie a mais. Vale a pena porque
# uma sonda que nunca passa torna o `depends_on: service_healthy` inutil, e isso e pior.
USER root
RUN apt-get update  && apt-get install -y --no-install-recommends curl  && rm -rf /var/lib/apt/lists/*

# Sem privilegios. A imagem base ja traz o utilizador `app`; usa-se em vez de correr como root,
# porque um processo que so serve HTTP nao tem nada que poder escrever fora do que lhe compete.
# A pasta e criada e atribuida **antes** de trocar de utilizador: um volume nomeado herda dono e
# permissoes do que a imagem tiver nesse caminho na primeira utilizacao. Sem isto, o volume nasce de
# root e o processo sem privilegios nao consegue escrever nele.
RUN mkdir -p /data/storage && chown -R app:app /data

USER app

# O armazenamento de ficheiros e local (`LocalFileStorage`). Fica num volume, senao os documentos
# carregados desaparecem com o contentor.
ENV ASPNETCORE_URLS=http://+:8080 \
    FileStorage__RootPath=/data/storage

EXPOSE 8080
VOLUME ["/data/storage"]

COPY --from=build --chown=app:app /app ./

ENTRYPOINT ["dotnet", "DocOps.Api.dll"]

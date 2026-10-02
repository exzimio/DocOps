# Imagem da interface: compila o pacote e serve-o com nginx.
#
# Construida a partir da raiz do repositorio, para o contexto ser o mesmo das outras:
#
#   docker build -f src/Web/Dockerfile -t docops-web .
#
# O `npm ci` vem antes do codigo, de proposito: e a camada cara, e so deve voltar a correr quando as
# dependencias mudam.

FROM node:22-alpine AS build
WORKDIR /app

COPY src/Web/package.json src/Web/package-lock.json ./
RUN npm ci

COPY src/Web/ ./

# `tsc -b && vite build`: os erros de tipo param a imagem aqui, e nao em producao.
RUN npm run build


FROM nginx:1.27-alpine AS runtime

# A configuracao por omissao serve na porta 80 e sem cabecalhos de seguranca. Esta substitui-a.
COPY src/Web/nginx.conf /etc/nginx/conf.d/default.conf
RUN rm -f /etc/nginx/conf.d/default.conf.bak

COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080

# Sem privilegios. A imagem alpine do nginx traz o utilizador `nginx`; os diretorios de que ele
# precisa para escrever ficam-lhe atribuidos, e a porta 8080 nao exige privilegios como a 80 exigiria.
RUN touch /var/run/nginx.pid \
 && chown -R nginx:nginx /var/run/nginx.pid /var/cache/nginx /usr/share/nginx/html

USER nginx

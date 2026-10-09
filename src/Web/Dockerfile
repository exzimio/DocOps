# Imagem da interface: compila o pacote e serve-o com nginx.
#
# Construída a partir da raiz do repositório, para o contexto ser o mesmo das outras:
#
#   docker build -f src/Web/Dockerfile -t docops-web .
#
# O `npm ci` vem antes do código, de propósito: é a camada cara, e só deve voltar a correr quando as
# dependências mudam.

FROM node:22-alpine AS build
WORKDIR /app

COPY src/Web/package.json src/Web/package-lock.json ./
RUN npm ci

COPY src/Web/ ./

# `tsc -b && vite build`: os erros de tipo param a imagem aqui, e não em produção.
RUN npm run build


FROM nginx:1.27-alpine AS runtime

# A configuração por omissão serve na porta 80 e sem cabeçalhos de segurança. Esta substitui-a.
COPY src/Web/nginx.conf /etc/nginx/conf.d/default.conf
RUN rm -f /etc/nginx/conf.d/default.conf.bak

COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080

# Sem privilégios. A imagem alpine do nginx traz o utilizador `nginx`; os diretórios de que ele
# precisa para escrever ficam-lhe atribuídos, e a porta 8080 não exige privilégios como a 80 exigiria.
RUN touch /var/run/nginx.pid \
 && chown -R nginx:nginx /var/run/nginx.pid /var/cache/nginx /usr/share/nginx/html

USER nginx

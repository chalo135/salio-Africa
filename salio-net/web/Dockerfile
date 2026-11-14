# Salio till: build the React app, then serve the files with nginx.
# Build from web/:
#   docker build --build-arg VITE_API_URL= -t salio-web .
#
# VITE_API_URL is baked into the JavaScript at build time; it cannot be
# changed afterwards without a rebuild. Empty means "same address the till
# was loaded from", which is right when Caddy serves /api and the till under
# one domain. Point it elsewhere only if the API lives on another domain.

# ---- Build ----
FROM node:22-alpine AS build
WORKDIR /web

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
ARG VITE_API_URL=""
ENV VITE_API_URL=${VITE_API_URL}
RUN npm run build

# ---- Serve: static files only. Runs as the non-root nginx user on 8080. ----
FROM nginxinc/nginx-unprivileged:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080

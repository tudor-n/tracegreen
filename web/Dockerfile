# syntax=docker/dockerfile:1.7
# Stages: dev (Vite dev server with /api proxy; compose bind-mounts ./web and keeps node_modules in a volume),
#         build (static bundle), nginx (serves the bundle and proxies /api to the API — the demo image).

FROM node:24-bookworm-slim AS base
RUN npm install -g pnpm@10.33.0
WORKDIR /app
ENV CI=true

FROM base AS dev
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY . .
EXPOSE 3000
CMD ["pnpm", "dev"]

FROM base AS build
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY . .
RUN pnpm build

FROM nginx:1.27-alpine AS nginx
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80

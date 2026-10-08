FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

FROM nginxinc/nginx-unprivileged:1.27-alpine
USER root
COPY --from=build /app/dist/neurovox-ui/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY neurovox-headers.conf /etc/nginx/neurovox-headers.conf
COPY docker-entrypoint.sh /docker-entrypoint-neurovox.sh
RUN chmod +x /docker-entrypoint-neurovox.sh && chown -R 101:101 /usr/share/nginx/html
USER 101
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["/docker-entrypoint-neurovox.sh"]

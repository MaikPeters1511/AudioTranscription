FROM node:lts-alpine AS build
WORKDIR /app

# Install dependencies
COPY package.json package-lock.json ./
RUN npm ci

# Copy source and build
COPY . .
RUN npm run build --configuration=production

FROM nginx:alpine AS final
# Copy compiled angular app to nginx html dir
COPY --from=build /app/dist/AudioTranscription.Web/browser /usr/share/nginx/html

# Custom nginx config template; the base image's entrypoint runs envsubst on
# every *.template file here and writes the result to /etc/nginx/conf.d (S14)
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
ENV NGINX_CLIENT_MAX_BODY_SIZE=500M

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]

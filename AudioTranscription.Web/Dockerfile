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
COPY --from=build /app/dist/audio-transcription-web/browser /usr/share/nginx/html

# Copy custom nginx config
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]

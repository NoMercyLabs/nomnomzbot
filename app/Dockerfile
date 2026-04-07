FROM node:22-alpine AS build
WORKDIR /app
COPY package.json yarn.lock ./
COPY scripts/ ./scripts/
RUN yarn install --frozen-lockfile
COPY . .
ARG EXPO_PUBLIC_API_URL=http://localhost:5080
ENV EXPO_PUBLIC_API_URL=$EXPO_PUBLIC_API_URL
RUN npx expo export --platform web

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
RUN printf 'server {\n  listen 80;\n  root /usr/share/nginx/html;\n  index index.html;\n  location / {\n    try_files $uri $uri/ /index.html;\n  }\n  gzip on;\n  gzip_types text/plain text/css application/json application/javascript text/xml;\n}\n' > /etc/nginx/conf.d/default.conf
EXPOSE 80

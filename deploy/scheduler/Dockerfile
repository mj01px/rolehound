# The scheduler: busybox crond from Alpine, plus curl to call the API and tzdata
# for TZ=America/Sao_Paulo. Built once on the VM (docker compose build), so
# nothing is downloaded each time the container starts.
FROM alpine:3.24.4@sha256:f59abe656b692d901e184be3445e084786602108599d2a51d229c0f5a48c71bf
RUN apk add --no-cache curl tzdata

#!/bin/sh
# Calls one scheduled task on the API. The key comes from the container's environment.
set -eu
curl --silent --show-error --fail --max-time 900 \
  -X POST "http://api:8080/tasks/$1" \
  -H "X-Task-Key: ${TASKS_KEY}" \
  -o /dev/null -w "$(date -Iseconds) $1 %{http_code}\n"

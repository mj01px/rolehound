#!/bin/sh
# Compressed dump of the database; keeps the last 7 days on the VM disk.
set -eu
file="/backups/rolehound-$(date +%Y-%m-%d).dump"
pg_dump -h postgres -U rolehound -d rolehound --format=custom --compress=9 --file="$file.tmp"
mv "$file.tmp" "$file"
find /backups -name 'rolehound-*.dump' -mtime +7 -delete
echo "$(date -Iseconds) backup $(du -h "$file" | cut -f1) $file"

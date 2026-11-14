#!/usr/bin/env bash
# Back up Salio: the whole database, plus the uploaded supplier invoices.
#
#   deploy/backup.sh               the nightly backup
#   deploy/backup.sh pre-deploy    the same, with a label in the file name
#
# Writes, into BACKUP_DIR:
#   salio_<UTC date>_<UTC time>[_label].dump             the database
#   salio_<UTC date>_<UTC time>[_label].invoices.tar.gz  uploaded invoices
# The .dump is pg_dump's custom format: already compressed, and the format
# deploy/restore.sh reads. Then it deletes backups older than KEEP_DAYS.
#
# CRON (as a user that may run docker; 23:15 UTC is 02:15 in Nairobi):
#   15 23 * * * /opt/salio/salio-net/deploy/backup.sh >> /var/backups/salio/backup.log 2>&1
#
# COPY THEM OFF THE SERVER, from Git Bash on the Windows laptop:
#   mkdir -p ~/salio-backups && scp 'USER@SERVER:/var/backups/salio/salio_*' ~/salio-backups/
#
# Settings (environment variables, all optional):
#   BACKUP_DIR   where backups go.   Default /var/backups/salio
#                (in Git Bash on Windows: ~/salio-backups)
#   KEEP_DAYS    days to keep.       Default 14
#   SALIO_DIR    the folder holding docker-compose.yml and .env.
#                Default: the folder above this script.
#   SALIO_DB / SALIO_DB_USER         Default salio / salio
#
# Safe to run by hand at any time, including while the shop is selling.
# If anything fails it says "BACKUP FAILED", exits non-zero, deletes nothing
# and leaves no half-written file under a real backup's name.
set -euo pipefail

# Git Bash on Windows rewrites arguments that look like Unix paths before
# docker sees them ("/invoices" becomes "C:/Program Files/Git/invoices").
# These two switch that off. They do nothing on Linux.
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SALIO_DIR="${SALIO_DIR:-$(dirname "$script_dir")}"
SALIO_DB="${SALIO_DB:-salio}"
SALIO_DB_USER="${SALIO_DB_USER:-salio}"
KEEP_DAYS="${KEEP_DAYS:-14}"

case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*) default_backup_dir="$HOME/salio-backups" ;;
    *) default_backup_dir="/var/backups/salio" ;;
esac
BACKUP_DIR="${BACKUP_DIR:-$default_backup_dir}"

label="${1:-}"

fail() {
    echo "$(date -u +%FT%TZ) BACKUP FAILED: $*" >&2
    exit 1
}

case "$KEEP_DAYS" in
    '' | *[!0-9]*) fail "KEEP_DAYS must be a whole number of days, got '$KEEP_DAYS'" ;;
esac
[ "$KEEP_DAYS" -ge 1 ] || fail "KEEP_DAYS must be at least 1"

case "$label" in
    *[!a-z0-9-]*) fail "the label may only hold a-z, 0-9 and -, got '$label'" ;;
esac

[ -f "$SALIO_DIR/docker-compose.yml" ] || fail "no docker-compose.yml in $SALIO_DIR (set SALIO_DIR)"

# docker compose finds docker-compose.yml and .env in the current folder.
cd "$SALIO_DIR"

# Dumps hold every sale and every password hash: owner-only files.
umask 077
mkdir -p "$BACKUP_DIR" || fail "cannot create $BACKUP_DIR"
BACKUP_DIR="$(cd "$BACKUP_DIR" && pwd)"

# One backup at a time. (flock is on every Linux server; Git Bash has none,
# and nothing runs backups there on a timer.)
if command -v flock > /dev/null 2>&1; then
    exec 9> "$BACKUP_DIR/.backup.lock"
    flock -n 9 || fail "another backup is already running"
fi

stamp="$(date -u +%Y-%m-%d_%H%M%S)"
name="salio_$stamp"
if [ -n "$label" ]; then
    name="${name}_$label"
fi

final_dump="$BACKUP_DIR/$name.dump"
partial_dump="$final_dump.partial"
final_files="$BACKUP_DIR/$name.invoices.tar.gz"
partial_files="$final_files.partial"

finished=0
cleanup() {
    rm -f "$partial_dump" "$partial_files"
    if [ "$finished" -ne 1 ]; then
        echo "$(date -u +%FT%TZ) BACKUP FAILED: see the lines above. Nothing was deleted." >&2
    fi
}
trap cleanup EXIT

db_running="$(docker compose ps --status running --services 2> /dev/null | tr -d '\r' | grep -x db || true)"
[ "$db_running" = "db" ] || fail "the db container is not running (docker compose ps)"

# ---- 1. The database -------------------------------------------------------
# Written under a .partial name first: a dump that stopped half way never
# carries a real backup's name.
docker compose exec -T db pg_dump -U "$SALIO_DB_USER" -d "$SALIO_DB" --format=custom > "$partial_dump"

[ -s "$partial_dump" ] || fail "pg_dump wrote an empty file"

# A dump pg_restore cannot read is not a backup. Read its table of contents
# back, and make sure the tables a shop cannot live without are in it.
contents="$(docker compose exec -T db pg_restore --list < "$partial_dump")"

for table in JournalLines Sales Organizations; do
    grep -q "TABLE DATA public $table " <<< "$contents" \
        || fail "the dump does not contain the $table table"
done

mv "$partial_dump" "$final_dump"

# ---- 2. The uploaded invoices ----------------------------------------------
# The db container sees the invoices volume read-only at /invoices (see
# docker-compose.yml), so no extra container is needed.
docker compose exec -T db tar -czf - -C /invoices . > "$partial_files"
docker compose exec -T db tar -tzf - < "$partial_files" > /dev/null
mv "$partial_files" "$final_files"

# ---- 3. Retention ----------------------------------------------------------
# Only reached when today's backup is safely written, so a broken backup job
# can never delete its way down to nothing. -mtime +N-1 means "N or more
# days old".
find "$BACKUP_DIR" -maxdepth 1 -type f \
    \( -name 'salio_*.dump' -o -name 'salio_*.invoices.tar.gz' -o -name 'salio_*.partial' \) \
    -mtime +$((KEEP_DAYS - 1)) -print -delete | sed 's/^/deleted old backup: /'

finished=1
kept="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'salio_*.dump' | wc -l | tr -d ' ')"
echo "$(date -u +%FT%TZ) backup written: $final_dump ($(du -h "$final_dump" | cut -f1)), invoices: $final_files ($(du -h "$final_files" | cut -f1)). $kept database backups kept (KEEP_DAYS=$KEEP_DAYS)."

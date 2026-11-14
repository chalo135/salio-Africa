#!/usr/bin/env bash
# Wait until the API is up and can reach its database, then say so.
#
#   deploy/wait-for-api.sh          waits up to 90 seconds
#   deploy/wait-for-api.sh 180      waits up to 180 seconds
#
# Run on the machine the containers run on. It asks the API directly on its
# 127.0.0.1 port (SALIO_API_PORT in .env), not through Caddy, so it works
# before DNS or the certificate exist. Exit code 0 = healthy.
#
# The API image has no shell and no curl inside it (on purpose), so Docker
# itself cannot run a health check in that container. This is that check.
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SALIO_DIR="${SALIO_DIR:-$(dirname "$script_dir")}"
seconds="${1:-90}"

# One value from .env, without running the file.
setting() {
    grep -E "^$1=" "$SALIO_DIR/.env" 2> /dev/null | tail -n 1 | cut -d= -f2- | tr -d '\r'
}

[ -f "$SALIO_DIR/.env" ] || { echo "No .env in $SALIO_DIR" >&2; exit 2; }

domain="$(setting SALIO_DOMAIN)"
port="$(setting SALIO_API_PORT)"
port="${port:-18080}"

[ -n "$domain" ] || { echo "SALIO_DOMAIN is empty in $SALIO_DIR/.env" >&2; exit 2; }

url="http://127.0.0.1:$port/api/health"
deadline=$(($(date +%s) + seconds))
answer="000"

echo "Waiting for the API at $url (up to $seconds seconds) ..."

while :; do
    # The API only answers to its own domain name (AllowedHosts).
    answer="$(curl --silent --output /dev/null --max-time 5 --write-out '%{http_code}' --header "Host: $domain" "$url" 2> /dev/null)"

    if [ "$answer" = "200" ]; then
        echo "HEALTHY: the API is up and can reach the database."
        exit 0
    fi

    if [ "$(date +%s)" -ge "$deadline" ]; then
        break
    fi

    sleep 2
done

echo "NOT HEALTHY after $seconds seconds. Last answer: $answer" >&2

case "$answer" in
    000) echo "  000 = nothing is listening. Is the container running?   docker compose ps api" >&2 ;;
    503) echo "  503 = the API is up but cannot reach the database (wrong password, or db not running)." >&2 ;;
    400) echo "  400 = the API refused the host name. SALIO_DOMAIN in .env must be the real domain." >&2 ;;
    404) echo "  404 = this API build has no /api/health. It is an older build: rebuild the images." >&2 ;;
esac

echo "  Look at:   cd $SALIO_DIR && docker compose logs --tail=60 api" >&2
exit 1

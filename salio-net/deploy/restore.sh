#!/usr/bin/env bash
# Restore a NAMED backup into a NAMED database.
#
#   deploy/restore.sh <dump-file> <database-name>
#
# PROVE A BACKUP WORKS (touches nothing live):
#   deploy/restore.sh /var/backups/salio/salio_2026-11-12_231500.dump salio_restore_test
# makes a fresh database called salio_restore_test inside the same Postgres
# container, restores the dump into it, and prints:
#   - every table's row count in the restored copy next to the live database
#   - debits and credits per shop in the restored copy (they must be equal)
#   - the newest migration in each
#   - the exact command that drops the test database again
# The live database is only read (for the counts). The scratch database is
# dropped and re-made if it already exists.
#
# REPLACE THE LIVE DATABASE (a real restore, after a bad release or a loss):
#   deploy/restore.sh <dump-file> salio --replace-live
# It refuses without --replace-live, and then asks you to type the database
# name. It restores into a side database first and checks it; only then does
# it stop the API, rename the old live database out of the way (kept, not
# dropped) and rename the restored one in. If anything fails before the
# swap, the live database has not been touched.
# Not at a keyboard (a script)? Also set SALIO_CONFIRM_REPLACE_LIVE=<name>.
#
# Settings (environment variables, all optional):
#   SALIO_DIR    the folder holding docker-compose.yml and .env.
#                Default: the folder above this script.
#   SALIO_DB / SALIO_DB_USER   the live database and its user. Default salio / salio
set -euo pipefail

# Git Bash on Windows rewrites arguments that look like Unix paths before
# docker sees them. These two switch that off. They do nothing on Linux.
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SALIO_DIR="${SALIO_DIR:-$(dirname "$script_dir")}"
LIVE_DB="${SALIO_DB:-salio}"
DB_USER="${SALIO_DB_USER:-salio}"

usage() {
    cat >&2 << EOF
Usage: $0 <dump-file> <database-name> [--replace-live]

  Test a backup:      $0 /var/backups/salio/salio_<date>.dump salio_restore_test
  Replace the live:   $0 /var/backups/salio/salio_<date>.dump $LIVE_DB --replace-live
EOF
    exit 2
}

fail() {
    echo "RESTORE FAILED: $*" >&2
    exit 1
}

dump=""
target=""
replace_live=0

for arg in "$@"; do
    case "$arg" in
        --replace-live) replace_live=1 ;;
        -h | --help) usage ;;
        -*) echo "Unknown option: $arg" >&2; usage ;;
        *)
            if [ -z "$dump" ]; then
                dump="$arg"
            elif [ -z "$target" ]; then
                target="$arg"
            else
                usage
            fi
            ;;
    esac
done

[ -n "$dump" ] && [ -n "$target" ] || usage
[ -f "$dump" ] || fail "no such file: $dump"
[ -s "$dump" ] || fail "the file is empty: $dump"

# The dump path may be relative to where the script was called from.
dump="$(cd "$(dirname "$dump")" && pwd)/$(basename "$dump")"

# A plain lower-case name only. It is placed inside SQL below, so nothing
# that needs quoting is accepted.
if ! [[ "$target" =~ ^[a-z][a-z0-9_]{0,50}$ ]]; then
    fail "the database name must be lower-case letters, digits and _ (starting with a letter), got '$target'"
fi

case "$target" in
    postgres | template0 | template1) fail "'$target' belongs to Postgres itself. Choose another name." ;;
esac

[ -f "$SALIO_DIR/docker-compose.yml" ] || fail "no docker-compose.yml in $SALIO_DIR (set SALIO_DIR)"
cd "$SALIO_DIR"

# ---- The live database is protected ----------------------------------------
if [ "$target" = "$LIVE_DB" ]; then
    if [ "$replace_live" -ne 1 ]; then
        cat >&2 << EOF
REFUSED: '$LIVE_DB' is the LIVE database. Nothing was changed.

To test this backup, restore it into a scratch database instead:
  $0 $dump salio_restore_test

To really replace the live database with this backup:
  $0 $dump $LIVE_DB --replace-live
EOF
        exit 1
    fi

    echo "You are about to REPLACE the live database '$LIVE_DB' with:"
    echo "  $dump"
    echo "Everything recorded after that backup was taken will no longer be in '$LIVE_DB'."
    echo "(The current database is kept under another name, not dropped.)"

    if [ -t 0 ]; then
        printf "Type the database name (%s) to go ahead, anything else to stop: " "$LIVE_DB"
        read -r typed
        [ "$typed" = "$LIVE_DB" ] || fail "you typed '$typed', not '$LIVE_DB'. Nothing was changed."
    else
        [ "${SALIO_CONFIRM_REPLACE_LIVE:-}" = "$LIVE_DB" ] \
            || fail "not at a keyboard, and SALIO_CONFIRM_REPLACE_LIVE is not '$LIVE_DB'. Nothing was changed."
    fi
elif [ "$replace_live" -eq 1 ]; then
    fail "--replace-live only makes sense with the live database name ('$LIVE_DB'), not '$target'. Nothing was changed."
fi

# ---- Helpers ---------------------------------------------------------------
# One SQL statement against one database; rows come back as a|b|c.
sql() {
    local database="$1"
    local statement="$2"
    docker compose exec -T db psql -U "$DB_USER" -d "$database" -X -q -v ON_ERROR_STOP=1 -At -F '|' -c "$statement" | tr -d '\r'
}

database_exists() {
    [ "$(sql postgres "SELECT count(*) FROM pg_database WHERE datname = '$1'")" = "1" ]
}

# Exact row count of every table: "table|rows", one per line.
table_counts() {
    sql "$1" "
        SELECT c.relname, (xpath('/row/n/text()', query_to_xml(
                   format('SELECT count(*) AS n FROM %I.%I', n.nspname, c.relname),
                   false, true, '')))[1]::text
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public' AND c.relkind = 'r'
        ORDER BY c.relname"
}

# "shop|debits|credits", one per shop.
books() {
    sql "$1" '
        SELECT o."Name",
               (SELECT coalesce(sum(l."DebitMinor"), 0) FROM "JournalLines" l WHERE l."OrganizationId" = o."Id"),
               (SELECT coalesce(sum(l."CreditMinor"), 0) FROM "JournalLines" l WHERE l."OrganizationId" = o."Id")
        FROM "Organizations" o
        ORDER BY o."Name"'
}

newest_migration() {
    sql "$1" 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1' 2> /dev/null || true
}

# Drop (if there), create, restore.
restore_into() {
    local database="$1"
    docker compose exec -T db dropdb -U "$DB_USER" --if-exists "$database"
    docker compose exec -T db createdb -U "$DB_USER" "$database"
    docker compose exec -T db pg_restore -U "$DB_USER" -d "$database" --no-owner --exit-on-error < "$dump"
}

# Prints the per-shop books of a database. Returns 1 if any shop is out.
print_books() {
    local database="$1"
    local rows shop debits credits bad=0

    rows="$(books "$database")"

    echo
    echo "Debits and credits per shop in '$database' (they must be equal):"

    if [ -z "$rows" ]; then
        echo "  (no shops in this backup)"
        return 0
    fi

    while IFS='|' read -r shop debits credits; do
        if [ "$debits" = "$credits" ]; then
            printf "  %-40s debits %15s   credits %15s   OK\n" "$shop" "$debits" "$credits"
        else
            printf "  %-40s debits %15s   credits %15s   NOT EQUAL\n" "$shop" "$debits" "$credits"
            bad=1
        fi
    done <<< "$rows"

    return "$bad"
}

# Row counts of the restored database beside the live one.
print_counts() {
    local restored="$1"
    local live="$2"
    local right_label="${3:-the database in use now}"
    local restored_counts live_counts=""

    restored_counts="$(table_counts "$restored")"

    if [ -n "$live" ] && database_exists "$live"; then
        live_counts="$(table_counts "$live")"
    fi

    echo
    echo "Rows per table. Left: the restored backup ('$restored'). Right: $right_label ('${live:-none}')."
    echo "The live side may be ahead: it has kept selling since the backup was taken."
    echo

    awk -F'|' -v left="$restored" -v right="${live:-none}" '
        BEGIN { printf "  %-28s %14s %14s\n", "table", "restored", "live now" }
        FNR == 1 { file++ }
        file == 1 && NF == 2 { a[$1] = $2; names[$1] = 1 }
        file == 2 && NF == 2 { b[$1] = $2; names[$1] = 1 }
        END {
            n = 0
            for (name in names) { sorted[++n] = name }
            # A plain sort, so it works with any awk.
            for (i = 1; i <= n; i++) {
                for (j = i + 1; j <= n; j++) {
                    if (sorted[j] < sorted[i]) { t = sorted[i]; sorted[i] = sorted[j]; sorted[j] = t }
                }
            }
            for (i = 1; i <= n; i++) {
                name = sorted[i]
                l = (name in a) ? a[name] : "-"
                r = (name in b) ? b[name] : "-"
                note = (l == r) ? "" : "   differs"
                printf "  %-28s %14s %14s%s\n", name, l, r, note
            }
        }' <(printf '%s\n' "$restored_counts") <(printf '%s\n' "$live_counts")
}

# ---- Before anything is created --------------------------------------------
running="$(docker compose ps --status running --services 2> /dev/null | tr -d '\r')"
grep -qx db <<< "$running" || fail "the db container is not running (docker compose ps)"

# The file must be a dump pg_restore can read, before any database is made.
docker compose exec -T db pg_restore --list < "$dump" > /dev/null \
    || fail "pg_restore cannot read $dump. It is not a usable backup."

# ---- A scratch restore -----------------------------------------------------
if [ "$target" != "$LIVE_DB" ]; then
    if database_exists "$target"; then
        echo "'$target' already exists (an earlier test). Dropping it and starting fresh."
    fi

    echo "Restoring $dump"
    echo "     into the scratch database '$target' ..."
    restore_into "$target"
    echo "Restored."

    print_counts "$target" "$LIVE_DB"

    books_ok=1
    print_books "$target" || books_ok=0

    echo
    echo "Newest migration, restored: $(newest_migration "$target")"
    echo "Newest migration, live now: $(newest_migration "$LIVE_DB")"
    echo
    echo "'$LIVE_DB' was only read, never changed. When you have looked, drop the scratch database:"
    echo "  cd $SALIO_DIR && docker compose exec -T db dropdb -U $DB_USER --if-exists $target"

    if [ "$books_ok" -ne 1 ]; then
        echo
        fail "debits do not equal credits in this backup. Do not rely on it; tell the developer."
    fi

    echo
    echo "RESTORE TEST PASSED: the backup restores, and debits equal credits in every shop."
    exit 0
fi

# ---- Replacing the live database -------------------------------------------
stamp="$(date -u +%Y%m%d_%H%M%S)"
incoming="${LIVE_DB}_incoming"
kept="${LIVE_DB}_before_restore_$stamp"

echo "Step 1 of 3: restoring into the side database '$incoming' (the live one is untouched) ..."
restore_into "$incoming"

if ! print_books "$incoming"; then
    fail "debits do not equal credits in this backup. '$LIVE_DB' was NOT touched. '$incoming' is left for inspection."
fi

api_was_running=0
if grep -qx api <<< "$running"; then
    api_was_running=1
fi

swapped=0
after_swap() {
    if [ "$api_was_running" -eq 1 ]; then
        echo "Starting the API again ..."
        docker compose start api || echo "COULD NOT START THE API. Run: docker compose start api" >&2
    fi
    if [ "$swapped" -ne 1 ]; then
        echo "RESTORE FAILED before the swap finished. Check which databases exist:" >&2
        echo "  docker compose exec -T db psql -U $DB_USER -d postgres -c '\\l'" >&2
    fi
}
trap after_swap EXIT

echo
echo "Step 2 of 3: stopping the API so nothing is written during the swap ..."
if [ "$api_was_running" -eq 1 ]; then
    docker compose stop api
fi

echo "Step 3 of 3: swapping the databases ..."
if database_exists "$LIVE_DB"; then
    sql postgres "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname IN ('$LIVE_DB', '$incoming') AND pid <> pg_backend_pid()" > /dev/null
    sql postgres "ALTER DATABASE \"$LIVE_DB\" RENAME TO \"$kept\""
    kept_note="The database as it was before this restore is kept as '$kept'."
else
    kept_note="There was no '$LIVE_DB' database before this restore."
    kept=""
fi

sql postgres "ALTER DATABASE \"$incoming\" RENAME TO \"$LIVE_DB\""
swapped=1

print_counts "$LIVE_DB" "$kept" "the database it replaced, kept"

echo
echo "Newest migration now in '$LIVE_DB': $(newest_migration "$LIVE_DB")"
echo
echo "RESTORED: '$LIVE_DB' now holds $dump"
echo "$kept_note"
if [ -n "$kept" ]; then
    echo "When you are sure the restore is right (give it a day), drop the old one:"
    echo "  cd $SALIO_DIR && docker compose exec -T db dropdb -U $DB_USER $kept"
fi
echo "If the backup is from an OLDER release than the images now running, set SALIO_VERSION"
echo "in .env to the release that made it (or run: docker compose run --rm migrate) before selling."

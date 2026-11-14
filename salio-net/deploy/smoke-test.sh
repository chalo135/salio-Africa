#!/usr/bin/env bash
# Is a deployed Salio really working? Five checks against a running site:
#   1 health   2 sign-in   3 create a part   4 sell it   5 trial balance
# Each prints PASS or FAIL and why. The exit code is 0 only if all passed.
#
#   export SMOKE_EMAIL='smoke@example.com'
#   read -rs SMOKE_PASSWORD && export SMOKE_PASSWORD     # typed, not shown
#   deploy/smoke-test.sh https://salio.example.com
#
# IT WRITES A REAL PART AND A REAL SALE, and Salio's books are never
# edited or deleted. So it must never run inside the client's shop:
#   - it signs in as its OWN account (SMOKE_EMAIL / SMOKE_PASSWORD), the
#     owner of a shop made only for this, whose name starts with "Smoke"
#     (deploy/DEPLOY.md shows how to make it: a second setup code);
#   - if the shop it lands in is not called "Smoke...", or the account
#     belongs to several shops, or is not an owner, it stops and writes
#     nothing.
#
# The password is never put on a command line and never written to disk.
# Needs only bash and curl. Works over https, and over plain http against
# a local build (it carries the login cookie itself, so the cookie's
# "Secure" mark does not get in the way).
#
# Optional settings:
#   SMOKE_INSECURE=1   accept a self-signed certificate (a laptop test
#                      behind Caddy's local certificate). Never on the server.
#   SMOKE_HOST=name    send this Host name. For testing straight at
#                      http://127.0.0.1:18080 on the server before DNS exists.
set -uo pipefail

base="${1:-}"

if [ -z "$base" ] || [ "$base" = "-h" ] || [ "$base" = "--help" ]; then
    echo "Usage: SMOKE_EMAIL=... SMOKE_PASSWORD=... $0 https://salio.example.com" >&2
    exit 2
fi

base="${base%/}"

case "$base" in
    http://* | https://*) ;;
    *) echo "The address must start with http:// or https://, got '$base'" >&2; exit 2 ;;
esac

if [ -z "${SMOKE_EMAIL:-}" ] || [ -z "${SMOKE_PASSWORD:-}" ]; then
    echo "Set SMOKE_EMAIL and SMOKE_PASSWORD (the smoke-test account) in the environment first." >&2
    echo "  export SMOKE_EMAIL='...'; read -rs SMOKE_PASSWORD && export SMOKE_PASSWORD" >&2
    exit 2
fi

command -v curl > /dev/null 2>&1 || { echo "curl is not installed." >&2; exit 2; }

curl_options=(--silent --show-error --include --max-time 30)

if [ "${SMOKE_INSECURE:-}" = "1" ]; then
    curl_options+=(--insecure)
fi

if [ -n "${SMOKE_HOST:-}" ]; then
    curl_options+=(--header "Host: $SMOKE_HOST")
fi

failures=0
cookie=""      # the login cookie, kept in memory only
status=""      # set by call: the HTTP status
headers=""     # set by call: the response headers
body=""        # set by call: the response body

pass() {
    echo "PASS  $1"
}

fail() {
    echo "FAIL  $1"
    failures=$((failures + 1))
}

# call METHOD PATH [JSON]   -> sets status, headers, body.
# The JSON goes to curl on its standard input, never on its command line.
call() {
    local method="$1"
    local path="$2"
    local json="${3:-}"
    local options=("${curl_options[@]}" --request "$method" --write-out '\n%{http_code}')
    local raw

    # What a browser sends from the till's own page.
    options+=(--header "Origin: $base")

    if [ -n "$cookie" ]; then
        options+=(--header "Cookie: $cookie")
    fi

    if [ -n "$json" ]; then
        raw="$(printf '%s' "$json" | curl "${options[@]}" --header 'Content-Type: application/json' --data-binary @- "$base$path" 2>&1)"
    else
        raw="$(curl "${options[@]}" "$base$path" 2>&1)"
    fi

    raw="$(printf '%s' "$raw" | tr -d '\r')"
    status="$(printf '%s\n' "$raw" | tail -n 1)"
    raw="$(printf '%s\n' "$raw" | sed '$d')"

    # Headers end at the first empty line; the rest is the body.
    headers="$(printf '%s\n' "$raw" | sed '/^$/q')"
    body="$(printf '%s\n' "$raw" | sed '1,/^$/d')"

    case "$status" in
        [0-9][0-9][0-9]) ;;
        *) body="$raw"; status="000" ;;
    esac
}

# The server's own words for a refusal ("detail" in its JSON), or the start
# of whatever came back.
reason() {
    local detail
    detail="$(printf '%s' "$body" | grep -o '"detail":"[^"]*"' | head -n 1 | sed 's/^"detail":"//; s/"$//')"

    if [ -n "$detail" ]; then
        printf '%s' "$detail"
    else
        printf '%s' "$body" | tr '\n' ' ' | cut -c 1-160
    fi
}

# json_escape TEXT -> TEXT safe inside a JSON string.
json_escape() {
    printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

# first_text KEY -> the first "KEY":"value" in the body.
first_text() {
    printf '%s' "$body" | grep -o "\"$1\":\"[^\"]*\"" | head -n 1 | sed "s/^\"$1\":\"//; s/\"\$//"
}

# last_number KEY -> the last "KEY":123 in the body.
last_number() {
    printf '%s' "$body" | grep -o "\"$1\":-\{0,1\}[0-9]*" | tail -n 1 | sed "s/^\"$1\"://"
}

finish() {
    # Sign out, so the smoke login does not stay valid for 12 hours.
    if [ -n "$cookie" ]; then
        call POST /api/auth/logout
        cookie=""
    fi

    echo
    if [ "$failures" -eq 0 ]; then
        echo "SMOKE TEST PASSED: $base"
        exit 0
    fi

    echo "SMOKE TEST FAILED: $failures of 5 checks did not pass ($base)"
    exit 1
}

echo "Smoke test of $base"

# ---- 1. Health -------------------------------------------------------------
call GET /api/health

if [ "$status" = "200" ] && printf '%s' "$body" | grep -q '"status" *: *"ok"'; then
    pass "1 health        the API answers and can reach its database"
elif [ "$status" = "000" ]; then
    fail "1 health        could not connect: $(reason)"
elif [ "$status" = "404" ]; then
    fail "1 health        /api/health answered 404: this API build has no health endpoint, or the request did not reach the API"
else
    fail "1 health        /api/health answered $status: $(reason)"
fi

# ---- 2. Sign in ------------------------------------------------------------
call POST /api/auth/login "{\"email\":\"$(json_escape "$SMOKE_EMAIL")\",\"password\":\"$(json_escape "$SMOKE_PASSWORD")\"}"

signed_in=0
organization_id=""

if [ "$status" = "200" ]; then
    # Every Set-Cookie that is part of the login (it can come in pieces:
    # salio.auth, salio.authC1, ...). Sent back by hand as a Cookie header.
    cookie="$(printf '%s\n' "$headers" | grep -i '^set-cookie: *salio\.auth[^=]*=[^;]' \
        | sed 's/^[^:]*: *//; s/;.*$//' | tr '\n' ';' | sed 's/;$//; s/;/; /g')"

    shop_count="$(printf '%s' "$body" | grep -o '"organizationId"' | wc -l | tr -d ' ')"
    shop_name="$(first_text name)"
    organization_id="$(first_text organizationId)"
    role="$(last_number role)"

    if [ -z "$cookie" ]; then
        fail "2 sign-in       the server accepted the password but sent no login cookie"
    elif printf '%s' "$body" | grep -q '"mustChangePassword" *: *true'; then
        fail "2 sign-in       this account still has a temporary password. Sign in once in a browser and choose a password, then run this again. Nothing was written."
    elif [ "$shop_count" != "1" ]; then
        fail "2 sign-in       this account belongs to $shop_count shops. The smoke-test account must belong to exactly one, the Smoke Test shop. Nothing was written."
    elif [ "${shop_name#Smoke}" = "$shop_name" ]; then
        fail "2 sign-in       REFUSED: this account's shop is called '$shop_name'. The smoke test only runs in a shop whose name starts with 'Smoke', never in a real shop. Nothing was written."
    elif [ "$role" != "0" ]; then
        fail "2 sign-in       this account is not the owner of '$shop_name'. Creating a part and reading the trial balance need the owner. Nothing was written."
    else
        pass "2 sign-in       signed in to '$shop_name'"
        signed_in=1
    fi
elif [ "$status" = "401" ]; then
    fail "2 sign-in       refused: wrong SMOKE_EMAIL or SMOKE_PASSWORD"
elif [ "$status" = "429" ]; then
    fail "2 sign-in       too many sign-in attempts from this address. Wait a minute and run it again."
else
    fail "2 sign-in       /api/auth/login answered $status: $(reason)"
fi

if [ "$signed_in" -ne 1 ]; then
    fail "3 create part   not tried: sign-in did not pass"
    fail "4 sale          not tried: sign-in did not pass"
    fail "5 trial balance not tried: sign-in did not pass"
    finish
fi

# ---- 3. Create a part ------------------------------------------------------
# A new name and SKU every run, so running it again never collides.
run_id="$(date -u +%Y%m%d-%H%M%S)"
price_minor=10000    # KES 100.00

call POST /api/products "{\"organizationId\":\"$organization_id\",\"sku\":\"SMOKE-$run_id\",\"name\":\"Smoke test part $run_id\",\"sellPriceMinor\":$price_minor,\"binLocation\":null}"

product_id=""

if [ "$status" = "201" ] || [ "$status" = "200" ]; then
    product_id="$(first_text id)"
fi

if [ -n "$product_id" ]; then
    pass "3 create part   SMOKE-$run_id"
else
    fail "3 create part   /api/products answered $status: $(reason)"
    fail "4 sale          not tried: there is no part to sell"
fi

# ---- 4. Sell it ------------------------------------------------------------
# The shop's day is Nairobi's (UTC+3), the same date the till sends.
today="$(date -u -d '+3 hours' +%Y-%m-%d 2> /dev/null || date -u +%Y-%m-%d)"

if [ -n "$product_id" ]; then
    call POST /api/sales "{\"organizationId\":\"$organization_id\",\"saleDate\":\"$today\",\"lines\":[{\"productId\":\"$product_id\",\"quantity\":1,\"unitPriceMinor\":$price_minor,\"taxBand\":\"A\"}],\"payments\":[{\"method\":0,\"amountMinor\":$price_minor,\"reference\":null}],\"customerId\":null}"

    sale_number="$(first_text saleNumber)"
    sale_total="$(last_number totalMinor)"

    if { [ "$status" = "200" ] || [ "$status" = "201" ]; } && [ -n "$sale_number" ] && [ "$sale_total" = "$price_minor" ]; then
        pass "4 sale          receipt $sale_number, cash, KES 100.00"
    elif [ "$status" = "200" ] || [ "$status" = "201" ]; then
        fail "4 sale          the sale was saved but its total is '$sale_total', expected $price_minor"
    else
        fail "4 sale          /api/sales answered $status: $(reason)"
    fi
fi

# ---- 5. Trial balance ------------------------------------------------------
call GET "/api/reports/trial-balance?organizationId=$organization_id&asOf=$today"

# The grand totals come after the rows, so they are the last of each name.
debits="$(last_number totalDebitMinor)"
credits="$(last_number totalCreditMinor)"

if [ "$status" != "200" ]; then
    fail "5 trial balance /api/reports/trial-balance answered $status: $(reason)"
elif [ -z "$debits" ] || [ -z "$credits" ]; then
    fail "5 trial balance the report came back without its totals"
elif [ "$debits" != "$credits" ]; then
    fail "5 trial balance DEBITS $debits DO NOT EQUAL CREDITS $credits. Stop and tell the developer."
elif [ "$debits" = "0" ]; then
    fail "5 trial balance debits and credits are both 0: the sale did not reach the books"
else
    pass "5 trial balance debits = credits = $debits (minor units)"
fi

finish

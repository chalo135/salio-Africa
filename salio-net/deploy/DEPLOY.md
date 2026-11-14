# Putting Salio on the VPS — the runbook

Follow it top to bottom. Every step says what you should SEE. If you do not
see it, stop and go to "If something goes wrong" at the bottom. Do not skip
ahead and do not improvise.

**The one rule above all others:** this server also runs Jade Wears, a live
client site. Nothing here may take it down. Every Salio container has a
memory ceiling and is first in line to be killed if the machine runs short,
Salio joins no network but its own, and no step below stops, restarts or
edits anything belonging to the other site, except one clearly marked line
in Path B.

Where things run:

| Marked | Means |
|---|---|
| **LAPTOP** | Git Bash on the Windows laptop (not PowerShell, not cmd) |
| **SERVER** | an SSH session on the VPS |

Two words you type yourself, once, in step 0: the server's SSH login and
Salio's domain. Everything after that is copy and paste.

Contents: 0 Before you start · 1 Look at the server · 2 Path A or Path B ·
3 Build on the laptop · 4 Copy to the server · 5 Settings and the database
password · 6 Start the database · 7 Migrate · 8 Start Salio · 9 HTTPS ·
10 First setup codes · 11 Check it works · 12 The tablet · 13 Backups and a
proven restore · 14 Logs · 15 Rolling back · 16 Every later release ·
17 Changing the database password · 18 If something goes wrong

---

## 0. Before you start

**LAPTOP.** Open Git Bash. Set these two for this window (replace the right-hand sides):

```bash
VPS=root@203.0.113.10          # how you SSH in: user@ip-address
DOMAIN=till.yourshop.co.ke     # Salio's domain. No https://, no slash
```

Check each of these. All four must pass before going on.

```bash
ssh $VPS 'echo connected; uname -m'
```
SEE: `connected` and `x86_64`. (If it says `aarch64`, stop: the images built
in step 3 would not run. Use the fallback in step 3b.)

```bash
nslookup $DOMAIN
ssh $VPS 'curl -4 -s https://ifconfig.me; echo'
```
SEE: the address under `Name: ... Address:` in the first equals the address
printed by the second. If not, the domain's **A record** does not point at
the server yet: fix it at the DNS provider, wait, check again. Caddy cannot
get a certificate until it does. (You can carry on to step 8 meanwhile; you
cannot do step 9.)

```bash
ssh $VPS 'docker --version && docker compose version && docker ps > /dev/null && echo docker-ok'
```
SEE: two version lines and `docker-ok`.
- `docker: command not found` → install it. **Only if Docker is truly missing**
  (never reinstall over a running Docker, the other site may be using it):
  ```bash
  ssh $VPS 'curl -fsSL https://get.docker.com -o /tmp/get-docker.sh && sudo sh /tmp/get-docker.sh'
  ```
  then run the check again.
- `permission denied ... docker.sock` → you are not root:
  `ssh $VPS 'sudo usermod -aG docker $USER'`, then run the check again (a new
  SSH session picks the group up).
- `docker compose` unknown → `ssh $VPS 'sudo apt-get update && sudo apt-get install -y docker-compose-plugin'`.

```bash
docker --version && docker ps > /dev/null && echo laptop-docker-ok
```
SEE: `laptop-docker-ok`. If not, start Docker Desktop and wait for it.

---

## 1. Look at the server before touching anything

**SERVER** (`ssh $VPS`). Nothing here changes anything.

```bash
free -m
```
Read the **available** column of the `Mem:` line. What Salio can use at its ceilings:

| Container | Ceiling | Notes |
|---|---|---|
| db (Postgres) | 1024 MB | `SALIO_DB_MEM` |
| api | 512 MB | `SALIO_API_MEM` |
| web (the till's files) | 64 MB | |
| caddy | 128 MB | Path A only |
| **Running total** | **1728 MB** Path A, **1600 MB** Path B | |
| migrate | 512 MB | only for the minute a migration runs |

- available is **2300 or more** → carry on.
- available is **1700 to 2300** → carry on, but in step 5 set `SALIO_DB_MEM=512m`
  (running total becomes 1216 MB / 1088 MB).
- available is **under 1700** → **stop. Do not deploy tonight.** Salio would be
  squeezing the other site.

```bash
df -h / | tail -1
```
SEE: at least 5G under `Avail`. Less → see "disk full" in section 18 first.

```bash
sudo ss -ltnp | grep -E ':(18080|18081)\s'
```
SEE: nothing. If a line prints, something already uses that port: in step 5
change `SALIO_API_PORT` / `SALIO_WEB_PORT` to free numbers (for example 18090
and 18091), and on Path B use the same numbers in step 9B.

```bash
docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
docker network ls
docker volume ls | grep salio
```
SEE: whatever the other site runs (note it, touch none of it). No network
or volume whose name starts with `salio_` on a first deploy.

## 2. Path A or Path B — one command decides

**SERVER.**

```bash
sudo ss -ltnp | grep -E ':(80|443)\s'
```

| You see | You are on |
|---|---|
| **Nothing at all** | **Path A.** Nothing owns the web ports. Salio brings its own Caddy container. |
| Lines ending `users:(("caddy",...` | **Path B.** A Caddy already runs on the server (for Jade Wears). It will serve Salio too. Salio's own Caddy container stays off. |
| Lines with `nginx`, `apache2`, `docker-proxy` or anything else | **Neither. Stop at step 8** and read "Something else owns 80/443" in section 18. Do NOT stop that program: it is the other site. |

Write down which path. Steps 5 and 9 differ; everything else is the same.

---

## 3. Build the images — on the laptop

**LAPTOP (Git Bash).** The images are built here, from the folder as it is
on this laptop, and copied over. Two reasons: building .NET on the server can
use over a gigabyte outside every memory ceiling, next to the live site; and
this folder is exactly what was tested.

```bash
cd /c/Users/charl/Desktop/salio/salio-net
TAG=$(date -u +%Y%m%d-%H%M)
echo "This release is: $TAG"
```
SEE: something like `This release is: 20261112-2015`. **Write the tag down.**
It names this release for ever (rollback uses it).

```bash
docker build -f Salio.Api/Dockerfile -t salio-api:$TAG . \
 && docker build -f Salio.Api/Dockerfile --target migrate -t salio-migrate:$TAG . \
 && docker build --build-arg VITE_API_URL= -t salio-web:$TAG web \
 && echo BUILD-OK
```
SEE: `BUILD-OK` as the last line (5 to 10 minutes the first time). An error
mentioning `error CS....` is a code error, not a deploy error: fix the code,
build again.

```bash
docker save salio-api:$TAG salio-migrate:$TAG salio-web:$TAG | gzip > ~/salio-images-$TAG.tar.gz
ls -lh ~/salio-images-$TAG.tar.gz
```
SEE: one file of roughly 150 to 200 MB.

### 3b. FALLBACK ONLY — building on the server

Use only if the laptop cannot build, or the server is not `x86_64`.
**Warning: this can use 1.5 GB of the server's memory with no ceiling.** Do it
only when `free -m` shows 3000 or more available, and one image at a time.

```bash
# LAPTOP: copy the source (never .env, never node_modules or build output)
cd /c/Users/charl/Desktop/salio
tar --exclude=node_modules --exclude=bin --exclude=obj --exclude=dist --exclude=.git \
    --exclude=.env --exclude=App_Data --exclude=claude -czf ~/salio-src.tar.gz salio-net
ssh $VPS 'mkdir -p /opt/salio'
scp ~/salio-src.tar.gz $VPS:/opt/salio/
# SERVER:
cd /opt/salio && tar -xzf salio-src.tar.gz && cd salio-net
# do step 5 (.env) first, with SALIO_VERSION set to a tag you choose, then:
docker compose build api && docker compose --profile tools build migrate && docker compose build web
```
Then continue from step 6 (skip step 4 and the `docker load` line).

## 4. Copy everything to the server

**LAPTOP (Git Bash)**, same window (it still knows `$TAG` and `$VPS`).

```bash
cd /c/Users/charl/Desktop/salio/salio-net
ssh $VPS 'sudo mkdir -p /opt/salio/salio-net && sudo chown -R $(id -un) /opt/salio'
scp ~/salio-images-$TAG.tar.gz docker-compose.yml .env.example $VPS:/opt/salio/salio-net/
scp -r deploy $VPS:/opt/salio/salio-net/
ssh $VPS "cd /opt/salio/salio-net && chmod +x deploy/*.sh && ls deploy deploy/caddy && echo TAG=$TAG"
```
SEE: `DEPLOY.md backup.sh caddy restore.sh smoke-test.sh wait-for-api.sh`,
then `Caddyfile salio-host.caddy salio-rules.caddy`, then `TAG=` and your tag.
(If root is your login, `sudo` is simply not needed; the line still works.)

**SERVER**, from here on unless marked.

```bash
cd /opt/salio/salio-net
gunzip -c salio-images-*.tar.gz | docker load
docker images | grep -E '^salio-'
```
SEE: three `Loaded image:` lines, then `salio-api`, `salio-migrate` and
`salio-web`, each with your tag.

## 5. Settings, and a NEW database password

The development database password was pasted into a chat AND is in pushed git
history (an old `appsettings.json`), and the laptop's dev database still uses
it. Treat it as public. **It is never reused here.** The production password
is generated fresh on the server by the line below, and seen by nobody.

**SERVER**, first deploy only (later releases keep the same `.env`):

```bash
cd /opt/salio/salio-net
[ -f .env ] && echo ".env ALREADY EXISTS - do not run the next lines" || cp .env.example .env
chmod 600 .env
sed -i "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 24)/" .env
nano .env
```

In nano set exactly these, then save (Ctrl+O, Enter, Ctrl+X):

| Line | Value |
|---|---|
| `POSTGRES_PASSWORD=` | already filled in by the line above. Leave it. |
| `SALIO_DOMAIN=` | the domain, e.g. `till.yourshop.co.ke`. No `https://`. |
| `SALIO_VERSION=` | the tag from step 3 |
| `COMPOSE_PROFILES=` | **Path A:** `caddy` · **Path B:** leave empty |
| `SALIO_DB_MEM=` | `1g`, or `512m` if step 1 said so |

Leave every other line as it is.

```bash
grep -c '^POSTGRES_PASSWORD=[0-9a-f]\{48\}$' .env
grep -E '^(SALIO_DOMAIN|SALIO_VERSION|COMPOSE_PROFILES)=' .env
docker compose config --quiet && echo CONFIG-OK
```
SEE: `1`; your three values; `CONFIG-OK`. A message like `Set SALIO_DOMAIN in
.env` means that line is still empty.

`.env` now holds the only copy of the database password. It never leaves the
server, and is never put in a chat or in git.

## 6. Start the database

```bash
docker compose pull db
docker compose up -d db
sleep 15; docker compose ps db
```
SEE: the STATUS column says `Up ... (healthy)`. If it says `(health:
starting)`, wait ten seconds and run the last command again.

```bash
docker compose ps db --format '{{.Ports}}'
```
SEE: exactly `5432/tcp`. No `0.0.0.0`, no `->`: Postgres is not published and
must never be reachable from outside.

## 7. Migrate the database

A migration changes the live database's tables. **Take a backup before every
migration.** Tonight, on a brand-new empty database, there is nothing to
back up, so on the FIRST deploy skip the next line. On every later release
it is not optional:

```bash
deploy/backup.sh pre-deploy        # LATER RELEASES ONLY. See section 13 for what you should see.
```

Then:

```bash
docker compose run --rm migrate
```
SEE: a lot of SQL scrolling past (first deploy: every migration, about a
minute; later: only new ones, or `No migrations were applied. The database is
already up to date.`), and the LAST line is `Done.` Ignore a line saying
`Cannot load library libgssapi_krb5.so.2`: it is harmless.
- `password authentication failed` → section 18, "database password mismatch".
- Any other error: **do not start the API.** The old release is still running
  untouched on a later release; go to section 15.

## 8. Start Salio

```bash
docker compose up -d --no-build
deploy/wait-for-api.sh
```
SEE: `HEALTHY: the API is up and can reach the database.` (On Path A the
first command also pulls and starts Caddy.) If it says NOT HEALTHY, it also
says why and what to look at.

The API answers only to its own domain name. So any check made straight at
`127.0.0.1:18080` must send it, as `wait-for-api.sh` does:
`curl -s -H "Host: $(grep '^SALIO_DOMAIN=' .env | cut -d= -f2)" http://127.0.0.1:18080/api/health`
→ `{"status":"ok"}`. Without the `Host` header the answer is 400; that is the
API being careful, not broken. The very first answer after a start can take
about three seconds. In the API log, one `Health check: the database did not
answer` line right at start-up and a `Cannot load library
libgssapi_krb5.so.2` line are both harmless.

Run **one** API container only (never `--scale api=2`): sign-in limits are
counted inside the API process, and a restart clears them.

```bash
docker compose ps
```
SEE: `db` (healthy), `api` Up, `web` Up (healthy), and on Path A `caddy` Up.
Nothing `Restarting`.

```bash
docker compose logs api | grep -E 'PublicBaseUrl|AllowedHosts|InvoiceStorage|Cors' ; echo "(end of warnings)"
```
SEE: only `(end of warnings)`. A line about `PublicBaseUrl`, `AllowedHosts` or
`InvoiceStorage` means `SALIO_DOMAIN` is empty or wrong in `.env`: fix it, then
`docker compose up -d --no-build`.

```bash
docker stats --no-stream --format 'table {{.Name}}\t{{.MemUsage}}\t{{.MemPerc}}' | grep -E 'NAME|salio'
```
SEE: each Salio container far below its limit (api around 100 to 150 MiB of
512, db under 100 MiB).

**Now open the other site (Jade Wears) in a browser.** It must load as usual.
If it does not: section 18, "the other site is down", immediately.

## 9. HTTPS

### 9A. Path A — Salio's own Caddy

Caddy started in step 8 and asks Let's Encrypt for the certificate by
itself. Nothing to configure. Give it up to a minute, then:

```bash
docker compose logs caddy | grep -i -E 'certificate obtained|error' | tail -5
```
SEE: a line containing `certificate obtained successfully`. Lines with
`error` → section 18, "certificate not issued".

### 9B. Path B — add Salio to the server's Caddy

This is the one place a file belonging to the other site's setup is edited:
one `import` line is added to the main Caddyfile. A copy is kept first, and
Caddy refuses a broken config instead of loading it.

```bash
cd /opt/salio/salio-net
DOMAIN=$(grep '^SALIO_DOMAIN=' .env | cut -d= -f2)
API_PORT=$(grep '^SALIO_API_PORT=' .env | cut -d= -f2)
WEB_PORT=$(grep '^SALIO_WEB_PORT=' .env | cut -d= -f2)
echo "$DOMAIN $API_PORT $WEB_PORT"
```
SEE: your domain, then `18080 18081` (or the ports you chose in step 1).

```bash
sudo mkdir -p /etc/caddy/sites
sudo cp deploy/caddy/salio-rules.caddy /etc/caddy/sites/salio-rules.caddy
sed -e "s/SALIO_DOMAIN_GOES_HERE/$DOMAIN/" -e "s/127.0.0.1:18080/127.0.0.1:$API_PORT/" -e "s/127.0.0.1:18081/127.0.0.1:$WEB_PORT/" \
    deploy/caddy/salio-host.caddy | sudo tee /etc/caddy/sites/salio-host.caddy | tail -4
```
SEE: the last lines show your real domain followed by `{` and the `import
salio_site 127.0.0.1:...` line.

```bash
sudo cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.before-salio
grep -q 'salio-host.caddy' /etc/caddy/Caddyfile || echo 'import /etc/caddy/sites/salio-host.caddy' | sudo tee -a /etc/caddy/Caddyfile
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile 2>&1 | tail -1
```
SEE: `Valid configuration`. **If it says anything else, do not reload.** Put
the old file back and stop:
`sudo cp /etc/caddy/Caddyfile.before-salio /etc/caddy/Caddyfile`

```bash
sudo systemctl reload caddy && systemctl is-active caddy
```
SEE: `active`. **Open Jade Wears in a browser straight away.** If anything is
wrong with it:
```bash
sudo cp /etc/caddy/Caddyfile.before-salio /etc/caddy/Caddyfile && sudo systemctl reload caddy
```

### Both paths — check from outside

**LAPTOP (Git Bash):**

```bash
curl -s https://$DOMAIN/api/health; echo
curl -s -o /dev/null -w '%{http_code}\n' https://$DOMAIN/
curl -sI https://$DOMAIN/ | grep -i -E 'strict-transport|x-content-type|permissions-policy'
```
SEE: `{"status":"ok"}`, then `200`, then three header lines. Anything else →
section 18.

## 10. The first setup codes

A shop is created by its owner, from a link holding a one-time setup code.
You make the code; they choose their own password; you never know it.

You need **two** codes tonight: one for a shop called **Smoke Test** (yours,
for the automatic check in step 11; the check refuses to run in any shop
whose name does not start with "Smoke"), and one for the client.

**SERVER.** The Smoke Test shop first. Use an email address of your own:

```bash
cd /opt/salio/salio-net
docker compose run --rm --no-deps api new-setup-code "Smoke Test" you+smoke@example.com
```
SEE exactly three lines, like:
```
Code:    K7M-P29
Link:    https://till.yourshop.co.ke/setup?code=K7MP29
Expires: 2026-11-19 20:15 UTC
```
- The link must start with `https://` and YOUR domain. If it says `Link:
  (none: PublicBaseUrl is not set)` or shows another address, `SALIO_DOMAIN`
  in `.env` is wrong: fix it, run `docker compose up -d --no-build`, and make
  a new code.
- A `Cannot load library libgssapi_krb5.so.2` line above the three is harmless.
- `Usage: ...` means the shop name was not in quotes or the email has no `@`.

Open that link on the laptop. Go through the screens: shop name **Smoke
Test**, your name, the same email, a password of at least 8 characters that
you keep (step 11 needs it). You end up in an empty till. Sign out.

Then the client's code (their shop's name, their email):

```bash
docker compose run --rm --no-deps api new-setup-code "Kamau Motor Spares" owner@example.com
```
**Send the client the `Link:` line**, nothing else. It works once and expires
on the date shown. Do not create their shop for them: the password must be
theirs.

## 11. Check it works

### By machine — the smoke test

It signs in to the Smoke Test shop, creates a part, sells it for KES 100
cash, and checks the trial balance. It writes real entries, which is why it
has its own shop.

**LAPTOP (Git Bash)**:

```bash
cd /c/Users/charl/Desktop/salio/salio-net
export SMOKE_EMAIL='you+smoke@example.com'
read -rs -p 'Smoke Test password: ' SMOKE_PASSWORD && export SMOKE_PASSWORD; echo
deploy/smoke-test.sh https://$DOMAIN
```
SEE:
```
PASS  1 health        the API answers and can reach its database
PASS  2 sign-in       signed in to 'Smoke Test'
PASS  3 create part   SMOKE-20261112-201533
PASS  4 sale          receipt ..., cash, KES 100.00
PASS  5 trial balance debits = credits = ... (minor units)

SMOKE TEST PASSED: https://till.yourshop.co.ke
```
Any `FAIL` line says why in plain words. `REFUSED: this account's shop is
called ...` means you used a real shop's login: it wrote nothing; use the
Smoke Test account. (It can equally be run on the server: same three lines,
from `/opt/salio/salio-net`. Before DNS or the certificate exist it can go
straight at the API: `SMOKE_HOST=<the domain> deploy/smoke-test.sh http://127.0.0.1:18080`.)

### By hand — on the tablet

1. Open `https://<the domain>` in Chrome. SEE: the Salio sign-in screen, with
   a padlock in the address bar.
2. Sign in as the Smoke Test owner. SEE: the till.
3. Add a part (name, price), put it in the cart, take cash. SEE: a receipt
   with a receipt number.
4. Open the reports and look at the trial balance. SEE: total debits equal
   total credits.

## 12. Install on the Android tablet

1. In Chrome on the tablet open `https://<the domain>` and sign in.
2. Chrome menu (⋮) → **Install app** (or **Add to Home screen**) → Install.
   Salio now opens from its own icon, full screen.

---

## 13. Backups, and proving one restores

A backup that has never been restored is not a backup. Do all of this
tonight, after the smoke test (so there is something in the database).

**SERVER:**

```bash
sudo mkdir -p /var/backups/salio && sudo chown $(id -un) /var/backups/salio && chmod 700 /var/backups/salio
cd /opt/salio/salio-net
deploy/backup.sh
ls -lh /var/backups/salio
```
SEE: a line `... backup written: /var/backups/salio/salio_<date>_<time>.dump
(...), invoices: ... 1 database backups kept (KEEP_DAYS=14).` and the two
files. `BACKUP FAILED` says why; nothing is deleted when it fails.

**Restore it into a scratch database** (the live one is only read):

```bash
deploy/restore.sh "$(ls -t /var/backups/salio/salio_*.dump | head -1)" salio_restore_test
```
SEE: a table of every table's rows, `restored` beside `live now` (equal
tonight; later the live side may be ahead, marked `differs`, because the shop
kept selling); `debits ... credits ... OK` for each shop; the same newest
migration on both lines; and `RESTORE TEST PASSED`.

Drop the scratch database (the script prints this line too):

```bash
docker compose exec -T db dropdb -U salio --if-exists salio_restore_test
```

`restore.sh` refuses to touch the live database (`salio`) unless you add
`--replace-live` and then type the database name. That is for section 15 only.

**Every night, automatically.** The server's clock is UTC; 23:15 UTC is 02:15
in Nairobi.

```bash
( crontab -l 2>/dev/null | grep -v 'salio-net/deploy/backup.sh'; echo '15 23 * * * /opt/salio/salio-net/deploy/backup.sh >> /var/backups/salio/backup.log 2>&1' ) | crontab -
crontab -l | grep backup.sh
```
SEE: the cron line. It keeps 14 days (to change: put `KEEP_DAYS=30 ` before
the path in that line). Tomorrow morning check it ran:
`tail -3 /var/backups/salio/backup.log` → a `backup written` line.

**Copy them off the server.** A backup on the same disk dies with the disk.
**LAPTOP (Git Bash)**, at least weekly:

```bash
mkdir -p ~/salio-backups && scp "$VPS:/var/backups/salio/salio_*" ~/salio-backups/ && ls -lh ~/salio-backups | tail -4
```
SEE: the same files, now on the laptop. They hold every sale and every
password hash: keep that folder private.

Uploaded invoice files come back from the `.invoices.tar.gz` beside each
dump, only if ever needed:
`docker run --rm -i -v salio_invoices:/to alpine tar -xzf - -C /to < /var/backups/salio/salio_<date>_<time>.invoices.tar.gz`
then `docker run --rm -v salio_invoices:/to alpine chown -R 1654:1654 /to`.

## 14. Seeing the logs

**SERVER**, in `/opt/salio/salio-net`:

```bash
docker compose logs --tail=100 api          # the last 100 lines of the API
docker compose logs -f api                  # follow live (Ctrl+C stops watching, not the API)
docker compose logs --since 30m api         # the last 30 minutes
docker compose logs --since 2026-11-12T18:00:00 --until 2026-11-12T19:00:00 api   # between two times (UTC)
docker compose logs --tail=100 db           # Postgres
docker compose logs --tail=100 caddy        # Path A's Caddy
sudo journalctl -u caddy --since '30 min ago' --no-pager | tail -50     # Path B's Caddy
```

**One error by its code.** When something unexpected breaks, the till says
"tell support this code: 3FA9C2D1". The same code is in the API log beside
the full error:

```bash
docker compose logs --no-color api | grep -A 30 '3FA9C2D1'
```
SEE: `Unhandled error 3FA9C2D1: POST /api/... failed. User: ... Shop: ...`
followed by the error itself.

Logs are capped at 30 MB per container (3 files of 10 MB), so they cannot
fill the disk; older lines are dropped.

## 15. Rolling back a bad release

First decide which case you are in.

**Case 1 — the new release misbehaves, and step 7 said "already up to date"
(no migration ran).** Go back to the previous images. They are still on the
server (`docker images | grep -E '^salio-'` lists every tag).

```bash
cd /opt/salio/salio-net
sed -i 's/^SALIO_VERSION=.*/SALIO_VERSION=THE-PREVIOUS-TAG/' .env
docker compose up -d --no-build api web
deploy/wait-for-api.sh
```
SEE: `HEALTHY`. The database was not touched; nothing is lost.

**Case 2 — a migration ran in this release.** The database's tables are now
the new shape; the old release may not work with them. Try Case 1 first: if
`wait-for-api.sh` says HEALTHY and the smoke test passes, stop there. If
not, the database must go back to the backup taken just before the migration
(`deploy/backup.sh pre-deploy` in step 7 — this is why that step is not
optional):

1. Everything sold since that backup will not be in the restored database.
   Before anything else, on the tablet or with the shop, **write down every
   sale made since the release** (the day's sales list); they are re-entered
   afterwards.
2. ```bash
   cd /opt/salio/salio-net
   ls -t /var/backups/salio/salio_*_pre-deploy.dump | head -1      # the backup to use
   docker compose stop api                                          # the bad release stops writing
   sed -i 's/^SALIO_VERSION=.*/SALIO_VERSION=THE-PREVIOUS-TAG/' .env
   deploy/restore.sh "$(ls -t /var/backups/salio/salio_*_pre-deploy.dump | head -1)" salio --replace-live
   ```
   It asks you to type `salio`. It restores into a side database and checks
   it first; only then swaps. The database as it was is kept (the script
   prints its name), not dropped.
   SEE: `RESTORED: 'salio' now holds ...`
3. ```bash
   docker compose up -d --no-build api web
   deploy/wait-for-api.sh
   ```
   SEE: `HEALTHY`. Run the smoke test (step 11).

**Taking Salio down completely, without touching the other site:**

```bash
cd /opt/salio/salio-net
docker compose down
```
SEE: Salio's containers and its network removed. Its data (database,
invoices, certificates) stays in the volumes; `docker compose up -d
--no-build` brings it all back. **Never add `-v`**: that deletes the database.
On Path B the domain then answers 502 until Salio is back; to remove it from
the server's Caddy as well:
`sudo cp /etc/caddy/Caddyfile.before-salio /etc/caddy/Caddyfile && sudo systemctl reload caddy`

## 16. Every later release — short

1. **LAPTOP:** step 3 (new `TAG`, build, save), then the `scp` lines of step 4
   (`.env` on the server is never overwritten: it is not copied).
2. **SERVER:**
   ```bash
   cd /opt/salio/salio-net && chmod +x deploy/*.sh
   gunzip -c salio-images-NEW-TAG.tar.gz | docker load
   grep '^SALIO_VERSION=' .env                         # write down the CURRENT tag: it is the way back
   deploy/backup.sh pre-deploy                         # must print "backup written"
   sed -i 's/^SALIO_VERSION=.*/SALIO_VERSION=NEW-TAG/' .env
   docker compose run --rm migrate                     # ends with "Done."
   docker compose up -d --no-build
   deploy/wait-for-api.sh                              # HEALTHY
   ```
3. **LAPTOP:** the smoke test (step 11). Open Jade Wears once.
4. Went wrong? Section 15.
5. Tidy, once the release has run a day: `rm /opt/salio/salio-net/salio-images-OLD-TAG.tar.gz`.
   Keep the previous release's images in Docker (they are the rollback);
   remove older ones with `docker rmi salio-api:OLD salio-migrate:OLD salio-web:OLD`.

If a release changes `deploy/caddy/` on Path A: `docker compose restart caddy`.
On Path B: repeat the two copy lines of step 9B, validate, reload.

## 17. Changing the database password later

`POSTGRES_PASSWORD` in `.env` is read by Postgres only when its volume is
first created. Editing `.env` alone changes what the API *sends* but not what
Postgres *expects*, and the API then cannot sign in to the database. The order
that works (about ten seconds of failed requests in the middle; do it when
the shop is closed):

```bash
cd /opt/salio/salio-net
NEW=$(openssl rand -hex 24)
docker compose exec -T db psql -U salio -d salio -c "ALTER ROLE salio PASSWORD '$NEW'"   # 1. Postgres first
sed -i "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$NEW/" .env                          # 2. then .env
unset NEW
docker compose up -d --no-build api                                                    # 3. then the API, re-made with the new one
deploy/wait-for-api.sh
```
SEE: `ALTER ROLE`, then the db and api containers `Recreated`/`Started`
(Docker re-makes db too, because its settings line changed; the data is in
the volume and is not touched), then `HEALTHY`.
Step 1 needs no password: inside its own container Postgres trusts the local
connection.

## 18. If something goes wrong

| What you see | What it is | What to do |
|---|---|---|
| **The other site (Jade Wears) is down or slow** | Possibly Salio taking memory, CPU or (Path A) the web ports | **First, at once:** `cd /opt/salio/salio-net && docker compose stop` (frees everything Salio holds; its data is safe). Then `free -m`; `sudo dmesg -T \| grep -i -E 'out of memory\|killed process' \| tail -5`; `systemctl status caddy --no-pager \| head -5`. Path B and it began right after step 9B: `sudo cp /etc/caddy/Caddyfile.before-salio /etc/caddy/Caddyfile && sudo systemctl reload caddy`. Bring Salio back only when the other site is well and you know why. |
| **Certificate not issued** (browser warns; Path A `docker compose logs caddy` shows `error` with `challenge` or `obtain`) | Let's Encrypt could not reach the server on the domain | 1. `nslookup` of the domain must give this server's address (step 0). 2. Ports 80 and 443 must be open: `sudo ufw status` (if active: `sudo ufw allow 80,443/tcp`), and the provider's firewall if one is set. 3. Then `docker compose restart caddy` (Path A) or `sudo systemctl reload caddy` (Path B) and wait a minute. 4. `too many certificates` / `rateLimited` in the log: stop retrying; wait the time the message says. Never delete the `salio_caddy_data` volume: that is what causes this. |
| **502 from Caddy** on `/api/...` | Caddy is up, the API is not answering | `docker compose ps api` then `deploy/wait-for-api.sh`. Not running → `docker compose up -d --no-build`. Restarting → next row. Path B: the ports in `/etc/caddy/sites/salio-host.caddy` must equal `SALIO_API_PORT`/`SALIO_WEB_PORT` in `.env`. |
| **502 on the till itself** (not `/api`) | the `web` container is down | `docker compose ps web`; `docker compose up -d --no-build web`; `docker compose logs --tail=30 web`. |
| **API keeps restarting** (`docker compose ps` shows `Restarting`) | Out of memory, or it crashes at start | `docker inspect --format 'oom={{.State.OOMKilled}} exit={{.State.ExitCode}} restarts={{.RestartCount}}' $(docker compose ps -aq api)`. `oom=true` or `exit=137` → memory: raise `SALIO_API_MEM` in `.env` to `768m` (only if `free -m` has room), `docker compose up -d --no-build api`. `oom=false` → it is failing at start: `docker compose logs --tail=60 api` and read the last error (a missing image tag, a bad setting). |
| **`/api/health` says 503**, API is Up, not restarting | The API cannot reach the database. A wrong password does NOT make the API restart; it shows here. | `docker compose logs --tail=40 api \| grep -i -E 'password\|28P01\|refused\|timeout'`. `password authentication failed` → next row. `refused`/`timeout` → `docker compose ps db`; `docker compose up -d db`. |
| **Database password mismatch** (`password authentication failed for user "salio"`, or migrate fails with it) | `.env` and Postgres disagree, usually because `.env` was edited after the database was first created | Make Postgres agree with `.env` (no data is touched): `PW=$(grep '^POSTGRES_PASSWORD=' .env \| cut -d= -f2); docker compose exec -T db psql -U salio -d salio -c "ALTER ROLE salio PASSWORD '$PW'"; unset PW; docker compose up -d --no-build api; deploy/wait-for-api.sh` |
| **`/api/health` says 400** from the server itself | The API refuses the host name | `SALIO_DOMAIN` in `.env` is not the domain being used. Fix, `docker compose up -d --no-build`. |
| **Setup link opens nothing / has the wrong address** | `PublicBaseUrl` is built from `SALIO_DOMAIN` | Fix `SALIO_DOMAIN`, `docker compose up -d --no-build`, make a new code (step 10). |
| **Invoice upload says it cannot be stored (503)** | The invoices volume is not writable | `docker compose logs --tail=30 api`; `docker run --rm -v salio_invoices:/v alpine chown -R 1654:1654 /v`; `docker compose restart api`. |
| **Disk full** (`No space left on device`; Postgres stops accepting writes) | Old image files, old images, or backups | `df -h /`; `docker system df`; `du -sh /var/backups/salio /opt/salio/salio-net/*.tar.gz`. Free space, safest first: `rm /opt/salio/salio-net/salio-images-*.tar.gz` (already loaded into Docker); then `docker rmi` Salio tags older than the previous release (section 16); then `docker builder prune -f`. **Never** `docker system prune -a` or `docker volume prune` on this server: they can remove the other site's images and unused volumes. Then `docker compose up -d --no-build`. |
| **Something else owns 80/443** (step 2: `nginx`, `apache2`, `docker-proxy`) | The other site's own web server or a proxy in a container | Path A cannot work (the ports are taken) and Path B's file is for Caddy only. Salio itself is fine and private on `127.0.0.1:18080` (API) and `127.0.0.1:18081` (till). That existing server must forward Salio's domain: everything under `/api/` to `127.0.0.1:18080`, everything else to `127.0.0.1:18081`, keeping the `Host` header and sending `X-Forwarded-For` and `X-Forwarded-Proto`, with a certificate for the domain, an 11 MB body limit for `/api/imports` and `/api/supplier-invoices` and 1 MB for the rest of `/api/`. This runbook has no tested commands for that: do not guess at eleven at night. Leave Salio running privately and do it in daylight. If it is `docker-proxy`, that proxy lives in a container and cannot reach `127.0.0.1` on the server at all without a change to its own setup. |
| **`docker compose` says `Set POSTGRES_PASSWORD in .env`** (or SALIO_DOMAIN / SALIO_VERSION) | You are not in `/opt/salio/salio-net`, or that line of `.env` is empty | `cd /opt/salio/salio-net`; `grep -E '^(SALIO_DOMAIN\|SALIO_VERSION)=' .env`. |
| **`bad interpreter` or `$'\r': command not found`** running a script | The script got Windows line endings on the way | `sed -i 's/\r$//' deploy/*.sh deploy/caddy/*` then run it again. |
| **Sign-in says "Too many sign-in attempts"** | The limit is 5 a minute and 20 an hour per address | Wait a minute. If everyone in the shop gets it at once while one person mistyped, tell the developer: the API is not seeing real addresses through the proxy. |

Things that are never the fix: `docker compose down -v`, `docker volume rm
salio_pgdata`, `docker system prune`, deleting `.env`, restarting the other
site's containers.

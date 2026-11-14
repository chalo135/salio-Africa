# Salio till (web)

The till: React 19 + TypeScript + Vite + Tailwind. It is a set of static
files; everything it knows comes from the API under `/api`.

## Commands

| Command | What it does |
|---|---|
| `npm run dev` | The till on http://localhost:5173, talking to the API on http://localhost:5077 |
| `npm run build` | Type-checks, then writes the real build to `dist/` |
| `npm run preview` | Serves `dist/` on http://localhost:4173, to try the real build |
| `npm run lint` | ESLint |

## Where the API is: `VITE_API_URL`

One setting, read when the till is **built** and baked into the JavaScript.
Changing it means building again. The code is the `API` constant in
`src/auth.ts`.

| How the till is run | `VITE_API_URL` | The till calls |
|---|---|---|
| `npm run build` / Docker | not set, or empty | `/api/...` on the address the till was loaded from |
| `npm run build` / Docker | `https://api.example.com` | `https://api.example.com/api/...` |
| `npm run dev` | not set | `http://localhost:5077/api/...` |
| `npm run dev` | set | whatever it says |

A real build never falls back to `localhost`. On the server leave it empty:
Caddy serves the till and `/api` under one hostname.

No slash at the end of the value.

Three ways to set it:

1. **Docker (how the server is built).** A build-arg. `docker-compose.yml`
   already passes it empty:

   ```
   docker build --build-arg VITE_API_URL= -t salio-web .
   ```

   Docker does not read `.env` files from this folder (`.dockerignore`
   leaves them out), so the build-arg is the only way in.

2. **A file, for builds on the laptop.** Copy `.env.example` to
   `.env.production` (used by `npm run build`) or `.env.development` (used
   by `npm run dev`). Both are gitignored.

3. **An environment variable, for one command.**

   ```
   # PowerShell
   $env:VITE_API_URL = "https://api.example.com"; npm run build
   # bash
   VITE_API_URL=https://api.example.com npm run build
   ```

To check a build: `grep -r "localhost:5077" dist/` must find nothing.

## Installing on the tablet, and bad connections

- `public/manifest.webmanifest` and `public/icons/` make Salio installable
  from Chrome ("Install Salio" in the menu, or Chrome's own menu).
- `public/sw.js` is the service worker. It keeps the till's own files on the
  tablet, so a bad connection still shows the till. It never touches `/api`
  and never saves anything from it. Opening the page always asks the network
  first, so a release reaches the tablet the next time the page loads with a
  working connection. The rules are written at the top of that file.
- The worker is registered only in a real build (`src/install.ts`). With
  `npm run dev` it is not used, and one left over from earlier is removed.
- To throw away everything the worker has saved on every tablet, change
  `VERSION` in `public/sw.js` and release.

## Serving

`Dockerfile` builds the till and serves `dist/` with nginx (`nginx.conf`).
HTTPS and `/api` belong to Caddy, in front.

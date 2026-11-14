// Salio's service worker. It keeps a copy of the till's own files (the page,
// its script and its styles) on the tablet, so a bad connection shows the
// till and not a blank screen.
//
// What it must never do, and how each is kept:
//
// - Never touch the API. Anything under /api goes straight to the network,
//   exactly as if this file did not exist. Nothing from it is ever saved.
//   Sales, stock and money are never answered from the tablet's memory.
// - Never leave the tablet on last week's till. Opening the page always asks
//   the network first. The saved page is used only when the network fails or
//   takes longer than NAVIGATION_TIMEOUT_MS, and every answer the network
//   does give replaces the saved one.
// - Never serve a half-saved till. A new page is saved only after every
//   script and style it names has been saved, so the saved page and its
//   files always belong together.
// - Never pretend. If a file is neither saved nor reachable, the request
//   fails just as it would in a plain browser tab.
//
// Change VERSION to throw away everything saved and start again.

const VERSION = "1";
const CACHE = "salio-shell-" + VERSION;

// The page. Every address of the till (/, /admin, ...) is this one file.
const SHELL = "/index.html";

// How long opening the page waits for the network before using the saved
// copy. A mobile connection often hangs rather than fails.
const NAVIGATION_TIMEOUT_MS = 4000;

// Small files outside /assets/ that are also kept. Icons under /icons/ are
// kept the same way.
const SMALL_FILES = ["/manifest.webmanifest", "/icons/icon-48.png", "/icons/icon-192.png"];

// Only a full, ordinary answer from our own server is ever saved.
function canSave(response) {
  return response.status === 200 && response.type === "basic";
}

function isHtml(response) {
  return (response.headers.get("Content-Type") || "").includes("text/html");
}

// The /assets/... addresses a page names. Vite puts a hash of the contents
// in each name, so a name never changes what it means.
function assetsNamedIn(html) {
  return Array.from(new Set(html.match(/\/assets\/[^"'\s>]+/g) || []));
}

// Fetches one /assets/ file and saves it. Throws if it cannot be had.
async function saveAsset(cache, url) {
  const response = await fetch(url);
  // A web page where a script should be is "not found" in disguise.
  if (!canSave(response) || isHtml(response)) {
    throw new Error("Could not fetch " + url);
  }
  await cache.put(url, response);
}

// Deletes saved /assets/ files that the given list does not name.
async function deleteAssetsNotIn(cache, wanted) {
  const saved = await cache.keys();
  for (const request of saved) {
    const path = new URL(request.url).pathname;
    if (path.startsWith("/assets/") && !wanted.includes(path)) {
      await cache.delete(request);
    }
  }
}

// Saves a page the network just gave us, as the new saved page.
//
// Order matters: first every file the page names, then the page itself. If
// the connection drops halfway, the old saved page is still there with all
// of its own files.
//
// deleteOld: also delete every saved script and style this page does not
// name, which is how an older till's files leave the tablet. Not done when
// the saved page has just been shown instead (the network was slow),
// because that page is still loading those files; the next load that the
// network answers in time clears them.
async function saveShell(response, deleteOld) {
  if (!canSave(response) || !isHtml(response)) {
    return;
  }

  const html = await response.text();
  const cache = await caches.open(CACHE);
  const wanted = assetsNamedIn(html);

  // A different till from the one saved: save its files, then the page.
  const saved = await cache.match(SHELL);
  if (!saved || (await saved.text()) !== html) {
    for (const url of wanted) {
      if (!(await cache.match(url))) {
        await saveAsset(cache, url);
      }
    }
    await cache.put(SHELL, new Response(html, { headers: response.headers }));
  }

  if (deleteOld) {
    await deleteAssetsNotIn(cache, wanted);
  }
}

// Install: save the page and the files it names before this worker is
// allowed to start. If any of it fails, the install fails and the browser
// tries again on the next visit, so a running worker always has a whole
// till saved. This is also what covers the very first visit, when the page
// loaded before any worker existed.
self.addEventListener("install", (event) => {
  event.waitUntil(
    (async () => {
      const response = await fetch(SHELL, { cache: "no-cache" });
      if (!canSave(response) || !isHtml(response)) {
        throw new Error("Could not fetch the page");
      }
      await saveShell(response, false);

      // Nice to have, not needed to show the till: a failure here is fine.
      const cache = await caches.open(CACHE);
      await cache.addAll(SMALL_FILES).catch(() => {});

      // Take over now rather than waiting for every Salio tab to close.
      await self.skipWaiting();
    })(),
  );
});

// Activate: clear out what older versions of this file saved, and any
// script or style the saved page no longer names, so the tablet's storage
// does not fill up release after release.
self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys();
      for (const name of names) {
        if (name.startsWith("salio-shell-") && name !== CACHE) {
          await caches.delete(name);
        }
      }

      const cache = await caches.open(CACHE);
      const saved = await cache.match(SHELL);
      if (saved) {
        await deleteAssetsNotIn(cache, assetsNamedIn(await saved.text()));
      }

      await self.clients.claim();
    })(),
  );
});

// Opening the page: network first, the saved page if that fails or is slow.
async function answerNavigation(event) {
  const cache = await caches.open(CACHE);
  const saved = await cache.match(SHELL);
  const network = fetch(event.request);

  let usedSaved = false;

  // Whatever happens below, a page the network gives us is saved for next
  // time, even if it arrives after the saved one has already been shown.
  event.waitUntil(
    network.then((response) => saveShell(response.clone(), !usedSaved)).catch(() => {}),
  );

  if (!saved) {
    return network; // Nothing saved: behave like a plain browser tab.
  }

  return new Promise((resolve) => {
    const timer = setTimeout(() => {
      usedSaved = true;
      resolve(saved);
    }, NAVIGATION_TIMEOUT_MS);

    network.then(
      (response) => {
        clearTimeout(timer);
        // The server is there but broken (being restarted, say): the saved
        // till is more use than an error page.
        if (response.status >= 500) {
          usedSaved = true;
          resolve(saved);
        } else {
          resolve(response);
        }
      },
      () => {
        clearTimeout(timer);
        usedSaved = true;
        resolve(saved);
      },
    );
  });
}

// Scripts and styles: the saved copy if there is one, otherwise the network,
// saving what comes back. Safe because the name changes when the file does.
async function answerAsset(request) {
  const cache = await caches.open(CACHE);
  const saved = await cache.match(request);
  if (saved) {
    return saved;
  }

  const response = await fetch(request);
  if (canSave(response) && !isHtml(response)) {
    await cache.put(request, response.clone());
  }
  return response;
}

// Manifest and icons: the saved copy at once, refreshed from the
// network behind it for next time. Nothing saved yet: the network.
async function answerSmallFile(event) {
  const cache = await caches.open(CACHE);
  const saved = await cache.match(event.request);

  const refresh = fetch(event.request).then(async (response) => {
    if (canSave(response) && !isHtml(response)) {
      await cache.put(event.request, response.clone());
    }
    return response;
  });

  if (!saved) {
    return refresh;
  }

  event.waitUntil(refresh.catch(() => {}));
  return saved;
}

// A request this handler returns from without calling respondWith goes to
// the network untouched, as if there were no service worker.
self.addEventListener("fetch", (event) => {
  const request = event.request;
  const url = new URL(request.url);

  // The API: never answered here, never saved. Checked first, on purpose.
  if (url.pathname === "/api" || url.pathname.startsWith("/api/")) {
    return;
  }

  // Only plain reads of our own files are handled.
  if (request.method !== "GET" || url.origin !== self.location.origin) {
    return;
  }

  if (request.mode === "navigate") {
    event.respondWith(answerNavigation(event));
  } else if (url.pathname.startsWith("/assets/")) {
    event.respondWith(answerAsset(request));
  } else if (SMALL_FILES.includes(url.pathname) || url.pathname.startsWith("/icons/")) {
    event.respondWith(answerSmallFile(event));
  }
});

// The page tells the worker which /assets/ files it actually loaded (see
// src/install.ts), and any not yet saved are saved. This catches files the
// page itself does not name: a screen loaded later, an image named by a
// style sheet.
self.addEventListener("message", (event) => {
  const data = event.data;
  if (!data || data.type !== "save-assets" || !Array.isArray(data.paths)) {
    return;
  }

  event.waitUntil(
    (async () => {
      const cache = await caches.open(CACHE);
      for (const path of data.paths) {
        const isAsset = typeof path === "string" && path.startsWith("/assets/");
        if (isAsset && !(await cache.match(path))) {
          await saveAsset(cache, path).catch(() => {});
        }
      }
    })(),
  );
});

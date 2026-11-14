// Installing Salio on the tablet, and its service worker. Imported once,
// from main.tsx.
//
// Chrome's "Salio can be installed" announcement is caught by a small script
// in index.html, before React exists, and parked on window.salioInstallPrompt.
// This file reads it from there, so one that arrived early is not lost, and
// is told when a later one arrives.

// Chrome's event. Not in TypeScript's own list, so described here.
type InstallPromptEvent = Event & {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
};

// Set by the script in index.html.
declare global {
  interface Window {
    salioInstallPrompt?: InstallPromptEvent | null;
    salioInstalled?: boolean;
  }
}

// Screens showing an Install control, told whenever that changes.
const listeners = new Set<() => void>();

function changed() {
  listeners.forEach((listener) => listener());
}

// The service worker (public/sw.js) keeps the till's own files on the
// tablet, so a bad connection still shows the till. It never touches /api.
//
// Only in a real build. With "npm run dev" a worker would save Vite's
// development files and keep serving old code, so there any worker left
// over from earlier is removed, along with whatever it saved.
if ("serviceWorker" in navigator) {
  if (import.meta.env.PROD) {
    window.addEventListener("load", () => {
      navigator.serviceWorker
        .register("/sw.js")
        .then(() => navigator.serviceWorker.ready)
        .then(tellWorkerWhatLoaded)
        .catch(() => {
          // No worker: the till works as an ordinary web page.
        });
    });
  } else {
    removeWorker().catch(() => {});
  }
}

// Tells the worker which /assets/ files this page loaded, so it can save
// any it does not have yet. On the very first visit the page loads before
// the worker exists; this is how those files still end up saved.
function tellWorkerWhatLoaded(registration: ServiceWorkerRegistration) {
  const paths = performance
    .getEntriesByType("resource")
    .map((entry) => new URL(entry.name))
    .filter((url) => url.origin === location.origin && url.pathname.startsWith("/assets/"))
    .map((url) => url.pathname);

  registration.active?.postMessage({ type: "save-assets", paths });
}

async function removeWorker() {
  const registrations = await navigator.serviceWorker.getRegistrations();
  for (const registration of registrations) {
    await registration.unregister();
  }

  const names = await caches.keys();
  for (const name of names) {
    if (name.startsWith("salio-shell-")) {
      await caches.delete(name);
    }
  }
}

// index.html's listeners were added first, so by the time these run the
// prompt is already parked (or cleared). These only tell the screens.
window.addEventListener("beforeinstallprompt", changed);
window.addEventListener("appinstalled", changed);

// True when Salio was opened from its icon, not from a browser tab. The
// second check is the iPhone's and iPad's own way of saying the same.
export function runningInstalled() {
  return (
    window.matchMedia("(display-mode: standalone)").matches ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  );
}

// Installed, as far as this page can tell: opened from the icon, or
// installed from this tab just now. Every Install control hides then.
export function isInstalled() {
  return runningInstalled() || window.salioInstalled === true;
}

// Chrome's own install question is ready to show. False on a browser that
// never offers installing, so no Install control appears there.
export function canInstall() {
  return Boolean(window.salioInstallPrompt) && !isInstalled();
}

export function onInstallChange(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

// The floating Install pill is a nudge, so "no" is remembered on this
// tablet: its X, or a no to Chrome's install question. The Install item in
// the menu is not affected.
const PILL_DISMISSED_KEY = "salio.installPillDismissed";

// A failed read (private window, storage blocked) counts as not dismissed.
export function pillDismissed() {
  try {
    return localStorage.getItem(PILL_DISMISSED_KEY) === "yes";
  } catch {
    return false;
  }
}

export function dismissPill() {
  try {
    localStorage.setItem(PILL_DISMISSED_KEY, "yes");
  } catch {
    // Storage blocked: it hides for now and may come back on the next load.
  }
}

// Shows Chrome's install question. A stored prompt works once: it is thrown
// away before it is shown, so nothing can use it twice, and every Install
// control hides whatever the person chooses. After a no, Chrome announces
// again on a later visit: the menu's Install item comes back then, the pill
// does not.
export async function install() {
  const offer = window.salioInstallPrompt;
  if (!offer) {
    return;
  }

  window.salioInstallPrompt = null;
  changed();

  await offer.prompt();
  const choice = await offer.userChoice;
  if (choice.outcome === "dismissed") {
    dismissPill();
  }
  changed();
}

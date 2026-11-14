import { lazy, Suspense, useEffect, useState } from "react";
import App from "./App";
import NotFound from "./NotFound";
import ConnectionBanner from "./ConnectionBanner";
import { API } from "./auth";
import { apiFetch } from "./api";

// Which part of Salio this address opens. Read once: the app changes the
// address itself only between paths it knows ("/" and "/setup").
//
// - "/" and "/setup": the till and shop setup (App).
// - "/admin": the admin area, but only once the server says this is the
//   admin. Anyone else, signed in or not, gets exactly the not-found page.
// - Anything else: the not-found page.
const KNOWN_PATHS = ["/", "/setup"];

// Loaded only after the server has confirmed the admin, so the admin
// screen's code is not in the file every till downloads. The API is still
// the real gate: this only keeps the words out of the main file.
// If its file cannot be fetched (no internet), say so instead of crashing.
const AdminScreen = lazy(() => import("./AdminScreen").catch(() => ({ default: CouldNotOpen })));

function CouldNotOpen() {
  return (
    <div className="flex min-h-full flex-col items-center justify-center bg-background px-4 text-center text-foreground">
      <p className="text-2xl font-bold">Page didn't open</p>
      <p className="mt-2 text-lg">Check the internet, then reload</p>
      <button
        onClick={() => window.location.reload()}
        className="mt-6 h-14 w-full max-w-sm rounded-xl bg-primary text-xl font-bold text-primary-foreground"
      >
        Reload
      </button>
    </div>
  );
}

function AdminGate() {
  // null while asking the server.
  const [isAdmin, setIsAdmin] = useState<boolean | null>(null);

  useEffect(() => {
    apiFetch(`${API}/api/admin/session`, { credentials: "include" })
      .then((response) => setIsAdmin(response.status === 204))
      .catch(() => setIsAdmin(false));
  }, []);

  if (isAdmin === null) {
    // A plain page, the not-found page's colour, while the server answers.
    return <div className="min-h-full bg-background" />;
  }

  if (!isAdmin) {
    return <NotFound />;
  }

  return (
    <Suspense fallback={<div className="min-h-full bg-background" />}>
      <AdminScreen />
    </Suspense>
  );
}

// The part of Salio this address opens.
function Page() {
  const path = window.location.pathname;

  if (path === "/admin") {
    return <AdminGate />;
  }

  if (KNOWN_PATHS.includes(path)) {
    return <App />;
  }

  return <NotFound />;
}

// The whole window: the can't-reach banner when it is needed, then the page
// in what is left. Every screen fills this space (h-full), not the window
// (h-dvh), so the banner pushes nothing off the bottom.
export default function Root() {
  return (
    <div className="flex h-dvh flex-col">
      <ConnectionBanner />
      <div className="min-h-0 flex-1 overflow-y-auto">
        <Page />
      </div>
    </div>
  );
}

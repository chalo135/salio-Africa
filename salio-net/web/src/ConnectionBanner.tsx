import { useEffect, useSyncExternalStore } from "react";
import { API } from "./auth";
import { isReachable, onReachableChange, setReachable } from "./api";

// Asks the cheapest question there is: is Salio there? Only a clear 200
// counts as yes. Anything else (no answer, the front door's 502, or Salio's
// own "unavailable" while its database is down) counts as no.
async function checkNow() {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), 8000);

  try {
    const response = await fetch(`${API}/api/health`, { signal: controller.signal, cache: "no-store" });
    setReachable(response.status === 200);
  } catch {
    setReachable(false);
  } finally {
    window.clearTimeout(timer);
  }
}

// The strip at the top of the app while Salio cannot be reached. It shows
// when any call fails for network reasons (see api.ts) and goes by itself
// when a call next gets through. While it shows, it asks again every 12
// seconds. Nothing on screen is touched: this only says what is going on.
export default function ConnectionBanner() {
  const reachable = useSyncExternalStore(onReachableChange, isReachable);

  // The browser's own "offline" and "online". Offline is believed at once.
  // Online is not: wifi with no internet behind it also says "online", so
  // that only prompts a real check.
  useEffect(() => {
    function wentOffline() {
      setReachable(false);
    }

    window.addEventListener("offline", wentOffline);
    window.addEventListener("online", checkNow);

    return () => {
      window.removeEventListener("offline", wentOffline);
      window.removeEventListener("online", checkNow);
    };
  }, []);

  useEffect(() => {
    if (reachable) {
      return;
    }

    const timer = window.setInterval(checkNow, 12000);
    return () => window.clearInterval(timer);
  }, [reachable]);

  if (reachable) {
    return null;
  }

  return (
    <div role="status" className="shrink-0 bg-warning px-3 py-2 text-center text-base font-semibold text-warning-foreground">
      Can't reach Salio. Your work is kept.
    </div>
  );
}

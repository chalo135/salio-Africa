import { useState, useEffect, useSyncExternalStore } from "react";
import { canInstall, dismissPill, install, onInstallChange, pillDismissed } from "./install";

// The floating nudge: "Install Salio", an Install button and an X. It uses
// the same stored install prompt as the sidebar's Install item (install.ts).
//
// When it shows:
// - Chrome's install question is ready and Salio is not installed;
// - it has not been dismissed on this tablet, by its X or by a no to
//   Chrome's install question (remembered in install.ts);
// - the app has been open about three seconds, so it arrives after the
//   screen has settled, not on top of it;
// - no cart has anything in it. Mid-sale is the worst moment.
//
// App.tsx never puts it on Sell, so it cannot cover the search, the cart or
// the Pay buttons.
//
// Movement: it slides up 12px and fades in (200ms), and leaves the same way
// (150ms). Nothing repeats. With "reduce motion" switched on in the tablet's
// settings it simply appears and disappears.

// How long after the page loads before it may appear.
const SETTLE_MS = 3000;

// How long the exit takes, matching duration-150 below.
const EXIT_MS = 150;

export default function InstallPill({ cartHasItems }: { cartHasItems: boolean }) {
  const ready = useSyncExternalStore(onInstallChange, canInstall);
  const [dismissed, setDismissed] = useState(pillDismissed);

  // True once the app has been open SETTLE_MS. Counted from the page load,
  // not from this screen, so opening Today later does not wait again.
  const [settled, setSettled] = useState(() => performance.now() >= SETTLE_MS);

  useEffect(() => {
    if (settled) {
      return;
    }
    const timer = window.setTimeout(() => setSettled(true), SETTLE_MS - performance.now());
    return () => window.clearTimeout(timer);
  }, [settled]);

  const wanted = ready && !dismissed && settled && !cartHasItems;

  // Two steps, so the movement can play: "inPage" puts it on the page,
  // "showing" slides it into view. Leaving reverses them, EXIT_MS apart.
  const [inPage, setInPage] = useState(false);
  const [showing, setShowing] = useState(false);

  useEffect(() => {
    let frame = 0;
    let timer = 0;

    if (wanted) {
      // On the page first, still hidden; one frame later, into view. Without
      // the gap the browser would skip straight to the end of the movement.
      frame = window.requestAnimationFrame(() => {
        setInPage(true);
        frame = window.requestAnimationFrame(() => setShowing(true));
      });
    } else {
      frame = window.requestAnimationFrame(() => setShowing(false));
      timer = window.setTimeout(() => setInPage(false), EXIT_MS);
    }

    return () => {
      window.cancelAnimationFrame(frame);
      window.clearTimeout(timer);
    };
  }, [wanted]);

  if (!inPage) {
    return null;
  }

  return (
    // Phone: centred, above the bottom tabs. Tablet: bottom right.
    <div
      aria-hidden={showing ? undefined : true}
      className={
        "fixed bottom-28 left-1/2 z-10 flex -translate-x-1/2 items-center gap-2 rounded-full " +
        "bg-primary py-1 pl-4 pr-1 text-primary-foreground shadow-lg " +
        "md:bottom-6 md:left-auto md:right-6 md:translate-x-0 " +
        "transition-[opacity,translate] motion-reduce:transition-none " +
        (showing
          ? "translate-y-0 opacity-100 duration-200 ease-out"
          : "pointer-events-none translate-y-3 opacity-0 duration-150 ease-in motion-reduce:translate-y-0")
      }
    >
      <span className="whitespace-nowrap text-base font-semibold">Install Salio</span>
      <button
        onClick={install}
        className="h-11 rounded-full bg-primary-foreground px-4 text-base font-bold text-primary"
      >
        Install
      </button>
      <button
        onClick={() => {
          dismissPill();
          setDismissed(true);
        }}
        aria-label="Dismiss"
        className="h-11 w-11 rounded-full text-2xl font-bold"
      >
        ×
      </button>
    </div>
  );
}

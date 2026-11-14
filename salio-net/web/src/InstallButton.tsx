import { useSyncExternalStore } from "react";
import { canInstall, install, onInstallChange } from "./install";

// The Install item, in the tablet's sidebar and on the phone's More screen.
// The floating pill is only a nudge and can be dismissed for good, so this
// is the real route.
//
// It is there only while Chrome has its install question ready, so tapping
// it always does something. It is not there when Salio is already installed
// or was opened from its icon, on a browser that never offers installing,
// or straight after the question was answered (Chrome offers again on a
// later visit, and the item comes back with it).
//
// "sidebar" is white on the green sidebar; "page" is green on a light page.
export default function InstallButton({ tone }: { tone: "sidebar" | "page" }) {
  // Read when the item first appears, and again whenever install.ts says
  // something changed: the same parked prompt the pill uses.
  const ready = useSyncExternalStore(onInstallChange, canInstall);

  if (!ready) {
    return null;
  }

  // In the sidebar it is one compact row with no note under it: the sidebar
  // must never scroll, even on a short screen (see Sidebar.tsx).
  if (tone === "sidebar") {
    return (
      <button
        onClick={install}
        className="mb-1.5 h-10 w-full rounded-xl bg-primary-fill text-base font-bold text-primary-foreground
          active:bg-primary-foreground/25"
      >
        Install Salio
      </button>
    );
  }

  const buttonClass = "h-14 w-full rounded-xl border-2 border-primary bg-surface text-xl font-bold text-primary";

  const noteClass = "mt-1 text-sm text-muted-foreground";

  return (
    <div className="mb-3">
      <button onClick={install} className={buttonClass}>
        Install Salio
      </button>
      <p className={noteClass}>Adds to home screen</p>
    </div>
  );
}

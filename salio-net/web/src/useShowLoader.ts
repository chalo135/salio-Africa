import { useEffect, useRef, useState } from "react";

// Decides when a loader is drawn. It never touches the work itself.
//
// - A wait shorter than 300 ms shows nothing: a flash of loader for a fast
//   answer is worse than no loader at all.
// - Once shown, the loader stays at least 600 ms, so it never blinks on and
//   straight off again.
// - Every timer is cleared when the screen using this goes away.
export default function useShowLoader(isLoading: boolean) {
  const [show, setShow] = useState(false);

  // When the loader appeared, to work out how much of the 600 ms is left.
  const shownAt = useRef(0);

  useEffect(() => {
    if (isLoading) {
      if (show) {
        // Already on screen (a new wait started inside the 600 ms): keep it.
        return;
      }

      // Show only if the wait is still going after 300 ms.
      const timer = window.setTimeout(() => {
        shownAt.current = Date.now();
        setShow(true);
      }, 300);
      return () => window.clearTimeout(timer);
    }

    if (!show) {
      // Finished before the loader ever appeared: nothing to do.
      return;
    }

    // Finished while showing: hide once 600 ms have passed since it appeared.
    const left = Math.max(0, 600 - (Date.now() - shownAt.current));
    const timer = window.setTimeout(() => setShow(false), left);
    return () => window.clearTimeout(timer);
  }, [isLoading, show]);

  return show;
}

// When the till locks itself. Locking is only what this browser shows: the
// login stays alive behind the lock screen, and the carts and the open shop
// stay exactly as they were. It stops someone picking up the tablet; it is
// not a wall against someone with developer tools.
//
// The rules, all in one place:
//   - No touch, click or key for 20 minutes, and every cart is empty: lock.
//   - No touch, click or key for 60 minutes, and any cart has something in
//     it: lock. A half-served customer gets longer before the screen locks.
//   - The tablet was asleep, or Salio was in the background, for a minute
//     or more: lock when it comes back. A short look away (checking an
//     M-Pesa SMS) does not lock.
//   - The idle time ran out while Salio was in the background: lock when it
//     comes back.

import { useEffect, useRef } from "react";

const MINUTE = 60 * 1000;

export const IDLE_EMPTY_CART_MS = 20 * MINUTE;
export const IDLE_WITH_ITEMS_MS = 60 * MINUTE;
export const HIDDEN_LOCK_MS = 1 * MINUTE;

// How long the till may sit untouched before it locks.
export function idleLimitMs(anyCartHasItems: boolean) {
  return anyCartHasItems ? IDLE_WITH_ITEMS_MS : IDLE_EMPTY_CART_MS;
}

// True once the till has sat untouched for longer than it may.
export function idleTimeUp(now: number, lastTouch: number, anyCartHasItems: boolean) {
  return now - lastTouch >= idleLimitMs(anyCartHasItems);
}

// True when Salio was out of sight long enough to lock on return.
export function hiddenTooLong(now: number, hiddenAt: number) {
  return now - hiddenAt >= HIDDEN_LOCK_MS;
}

// What counts as someone using the till. Scrolling a long list counts too.
const ACTIVITY_EVENTS = ["pointerdown", "keydown", "touchstart", "wheel", "scroll"] as const;

// Watches for idleness and for coming back from sleep, and calls onLock
// when one of the rules above says so. Does nothing while `enabled` is
// false: already locked, nobody signed in, or a screen that must not be
// locked over (choosing a password, the PIN offer).
export function useAutoLock({
  enabled,
  anyCartHasItems,
  onLock,
}: {
  enabled: boolean;
  anyCartHasItems: boolean;
  onLock: () => void;
}) {
  // Refs, so the timer always reads the latest values without being
  // restarted on every change to the cart.
  const itemsRef = useRef(anyCartHasItems);
  const onLockRef = useRef(onLock);
  useEffect(() => {
    itemsRef.current = anyCartHasItems;
    onLockRef.current = onLock;
  });

  useEffect(() => {
    if (!enabled) {
      return;
    }

    // Starting (or unlocking) counts as a touch.
    let lastTouch = Date.now();
    // When the page was last hidden, or null while it is showing.
    let hiddenAt: number | null = document.hidden ? Date.now() : null;
    let done = false;

    function lock() {
      if (!done) {
        done = true;
        onLockRef.current();
      }
    }

    function touched() {
      lastTouch = Date.now();
    }

    // Checked every 15 seconds. A timer set for the full 20 minutes would
    // have to be restarted on every tap.
    const timer = window.setInterval(() => {
      if (idleTimeUp(Date.now(), lastTouch, itemsRef.current)) {
        lock();
      }
    }, 15 * 1000);

    function onVisibilityChange() {
      const now = Date.now();
      if (document.hidden) {
        hiddenAt = now;
        return;
      }
      const wasHiddenAt = hiddenAt;
      hiddenAt = null;
      // Timers barely run while the tablet sleeps, so check both rules the
      // moment it wakes.
      if ((wasHiddenAt !== null && hiddenTooLong(now, wasHiddenAt)) || idleTimeUp(now, lastTouch, itemsRef.current)) {
        lock();
      }
    }

    for (const name of ACTIVITY_EVENTS) {
      // capture: scroll does not bubble, so listen on the way down.
      window.addEventListener(name, touched, { capture: true, passive: true });
    }
    document.addEventListener("visibilitychange", onVisibilityChange);

    return () => {
      window.clearInterval(timer);
      for (const name of ACTIVITY_EVENTS) {
        window.removeEventListener(name, touched, { capture: true });
      }
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [enabled]);
}

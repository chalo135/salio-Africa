// Every call to the Salio server goes through apiFetch, and every refusal is
// turned into words by problemText. Two jobs, both here so no screen does
// them its own way:
//
// 1. Knowing whether Salio can be reached. A call that fails for network
//    reasons (no answer, a timeout, or the 502/503/504 the server's front
//    door gives while the API restarts) marks Salio as not reachable, and
//    the banner at the top of the app shows (ConnectionBanner.tsx). The next
//    call that gets an answer clears it.
// 2. Plain words. The shopkeeper never sees a status code, raw JSON or a
//    browser error such as "Failed to fetch".

let reachable = true;
const listeners = new Set<() => void>();

export function isReachable() {
  return reachable;
}

export function setReachable(now: boolean) {
  if (now === reachable) {
    return;
  }
  reachable = now;
  for (const listener of listeners) {
    listener();
  }
}

// For useSyncExternalStore: returns the function that stops listening.
export function onReachableChange(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

// What the server's front door (Caddy) answers when the API itself is not
// there: 502, 503 or 504 as an HTML page. That means "not reachable right
// now". The same numbers WITH a JSON body are Salio itself speaking (for
// example "Reading invoices is not switched on yet"), and are not this.
export function isGatewayDown(response: Response) {
  const status = response.status;
  if (status !== 502 && status !== 503 && status !== 504) {
    return false;
  }
  const type = response.headers.get("content-type") ?? "";
  return !type.includes("json");
}

// fetch, plus: it tells the banner whether Salio answered, and it gives up
// after timeoutMs if one is given (no timeout otherwise: reading an invoice
// or a big import can rightly take a long time).
//
// It throws exactly when fetch would: the server could not be reached, or
// the time ran out.
export async function apiFetch(url: string, init?: RequestInit, timeoutMs?: number): Promise<Response> {
  const controller = new AbortController();
  const timer = timeoutMs === undefined ? undefined : window.setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(url, { ...init, signal: controller.signal });
    setReachable(!isGatewayDown(response));
    return response;
  } catch (error) {
    setReachable(false);
    throw error;
  } finally {
    if (timer !== undefined) {
      window.clearTimeout(timer);
    }
  }
}

export const CANT_REACH = "Can't reach Salio";

// True when the server's words are fit to show: a short sentence, not a
// page of HTML, raw JSON or a programmer's error.
function isPlain(text: unknown): text is string {
  if (typeof text !== "string") {
    return false;
  }
  const trimmed = text.trim();
  if (trimmed === "" || trimmed.length > 300) {
    return false;
  }
  if (trimmed.startsWith("<") || trimmed.startsWith("{") || trimmed.startsWith("[")) {
    return false;
  }
  return !/Exception|System\.|\$\.|undefined|\bnull\b|\n\s+at /.test(trimmed);
}

// Why the server said no, as one plain sentence.
//
// The server's own "detail" is written for the shopkeeper and is shown as it
// came. Without one, the sentence comes from the kind of failure; `fallback`
// is what the screen says when nothing more is known.
export async function problemText(
  response: Response,
  fallback = "Not saved. Try again",
): Promise<string> {
  try {
    const problem: unknown = await response.json();

    if (typeof problem === "object" && problem !== null) {
      const detail = (problem as { detail?: unknown }).detail;
      if (isPlain(detail)) {
        return detail;
      }

      // A form the server would not take: its reasons are in "errors", as
      // field name -> messages. Show the first one that reads as a sentence.
      const errors = (problem as { errors?: unknown }).errors;
      if (typeof errors === "object" && errors !== null && !Array.isArray(errors)) {
        for (const messages of Object.values(errors)) {
          const first = Array.isArray(messages) ? messages[0] : messages;
          // "The input was not valid." says nothing: use the screen's words.
          if (isPlain(first) && first !== "The input was not valid.") {
            return first;
          }
        }
      }
    }
  } catch {
    // Not JSON (the front door's HTML page, or nothing): by kind, below.
  }

  if (response.status === 403) {
    return "Owner only";
  }
  if (response.status === 413) {
    return "File too large";
  }
  if (response.status === 429) {
    return "Wait a minute, then retry";
  }
  if (response.status >= 500) {
    return "Salio had a problem. Nothing saved.";
  }

  return fallback;
}

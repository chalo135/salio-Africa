// The prices a cashier can tap on a cart line, from the asking price down to
// the stop price. Every amount is in minor units (KES cents), whole numbers
// only: no division result is ever used as a price without being whole.

// Nice gaps between buttons, in KES: 5, 10, 20, 25, 50, 100, 200, 250, 500.
const STEPS_MINOR = [500, 1000, 2000, 2500, 5000, 10000, 20000, 25000, 50000];

// The largest nice gap that divides `amount` exactly, or null.
function niceStepDividing(amount: number): number | null {
  for (let i = STEPS_MINOR.length - 1; i >= 0; i--) {
    if (amount % STEPS_MINOR[i] === 0) {
      return STEPS_MINOR[i];
    }
  }
  return null;
}

// The largest nice gap no bigger than `amount`, or null.
function niceStepAtMost(amount: number): number | null {
  for (let i = STEPS_MINOR.length - 1; i >= 0; i--) {
    if (STEPS_MINOR[i] <= amount) {
      return STEPS_MINOR[i];
    }
  }
  return null;
}

// Asking price first, stop last, 2 to 4 buttons, never one below the stop.
//
//   no stop                     -> [asking]
//   stop at or above asking     -> [asking]  (the asking price is already
//                                  under the stop; the screen says so)
//   otherwise, 3 equal jumps if the gap splits into nice ones (4 buttons),
//   else 2 equal jumps (3 buttons), else roughly equal jumps rounded down to
//   a nice gap, else just [asking, stop].
export function priceSteps(askingMinor: number, stopMinor: number | null): number[] {
  if (stopMinor === null || stopMinor >= askingMinor) {
    return [askingMinor];
  }

  const range = askingMinor - stopMinor;

  // Equal jumps, each a multiple of a nice gap. Prefer 4 buttons.
  for (const jumps of [3, 2]) {
    if (range % jumps !== 0) {
      continue;
    }

    const interval = range / jumps;

    if (niceStepDividing(interval) !== null) {
      const prices = [];
      for (let k = 0; k <= jumps; k++) {
        prices.push(askingMinor - k * interval);
      }
      return prices;
    }
  }

  // Roughly equal: each middle button rounded down to a nice gap. Rounding
  // down by less than one jump keeps every button above the stop.
  for (const jumps of [3, 2]) {
    const rough = (range - (range % jumps)) / jumps;
    const step = niceStepAtMost(rough);

    if (step === null) {
      continue;
    }

    const middles: number[] = [];

    for (let k = 1; k < jumps; k++) {
      const exact = askingMinor - k * rough;
      const rounded = exact - (exact % step);

      if (rounded > stopMinor && rounded < askingMinor && !middles.includes(rounded)) {
        middles.push(rounded);
      }
    }

    if (middles.length === jumps - 1) {
      return [askingMinor, ...middles, stopMinor];
    }
  }

  return [askingMinor, stopMinor];
}

// How far one tap of LOWER goes below the lowest button: the gap between the
// last two buttons, or, with only the asking price showing, a nice gap of
// about a tenth of it (never less than KES 5).
export function lowerInterval(steps: number[]): number {
  if (steps.length >= 2) {
    return steps[steps.length - 2] - steps[steps.length - 1];
  }

  const tenth = (steps[0] - (steps[0] % 10)) / 10;
  return niceStepAtMost(tenth) ?? STEPS_MINOR[0];
}

// True when the agreed price has gone under the lowest button: under the
// stop, or under the asking price when there is no stop. The line turns red
// and says so. It never stops the sale.
export function belowLowest(askingMinor: number, stopMinor: number | null, agreedMinor: number): boolean {
  const steps = priceSteps(askingMinor, stopMinor);
  return agreedMinor < steps[steps.length - 1];
}

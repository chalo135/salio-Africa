// Run with: npm test   (Node's own test runner; no packages.)
// Amounts are in minor units, so KES 4,500 is 450000.
import { test } from "node:test";
import assert from "node:assert/strict";
import { ladderSteps, lowerInterval, priceSteps } from "../src/priceSteps.ts";

const KES = (amounts: number[]) => amounts.map((a) => a * 100);

test("4500 down to 3900: four buttons, 200 apart", () => {
  assert.deepEqual(priceSteps(450000, 390000), KES([4500, 4300, 4100, 3900]));
});

test("500 down to 420: three buttons, 40 apart", () => {
  assert.deepEqual(priceSteps(50000, 42000), KES([500, 460, 420]));
});

test("500 down to 460: three buttons, 20 apart", () => {
  assert.deepEqual(priceSteps(50000, 46000), KES([500, 480, 460]));
});

test("no stop: the asking price only", () => {
  assert.deepEqual(priceSteps(50000, null), KES([500]));
});

test("stop above the asking price (the oil case): the asking price only", () => {
  assert.deepEqual(priceSteps(50000, 52000), KES([500]));
});

test("never a button below the stop, and asking first, stop last", () => {
  for (let asking = 1000; asking <= 600000; asking += 1370) {
    for (const stop of [asking - 100, asking - 730, asking - 9990, Math.floor(asking / 2)]) {
      if (stop <= 0 || stop >= asking) continue;
      const steps = priceSteps(asking, stop);
      assert.ok(steps.length >= 2 && steps.length <= 4, `${asking}/${stop}: ${steps}`);
      assert.equal(steps[0], asking);
      assert.equal(steps[steps.length - 1], stop);
      for (let i = 1; i < steps.length; i++) {
        assert.ok(steps[i] < steps[i - 1], `${asking}/${stop}: ${steps} not falling`);
        assert.ok(steps[i] >= stop, `${asking}/${stop}: ${steps} below stop`);
        assert.ok(Number.isInteger(steps[i]), `${asking}/${stop}: ${steps} not whole`);
      }
    }
  }
});

test("LOWER steps by the last gap", () => {
  assert.equal(lowerInterval(KES([4500, 4300, 4100, 3900])), 20000);
  assert.equal(lowerInterval(KES([500, 460, 420])), 4000);
});

test("LOWER with only the asking price: a nice gap near a tenth", () => {
  assert.equal(lowerInterval(KES([500])), 5000);
  assert.equal(lowerInterval(KES([30])), 500);
});

test("ladder, no stop: asking 1500 and three steps of about 5%", () => {
  assert.deepEqual(ladderSteps(150000, null), KES([1500, 1400, 1300, 1200]));
});

test("ladder, stop 3900 under asking 4500: down to the stop and two past it", () => {
  assert.deepEqual(ladderSteps(450000, 390000), KES([4500, 4300, 4100, 3900, 3700, 3500]));
});

test("ladder: at most 6, falling, whole, never at or below zero", () => {
  for (let asking = 500; asking <= 900000; asking += 1730) {
    for (const stop of [null, asking - 500, Math.floor(asking / 2), 1000]) {
      const steps = ladderSteps(asking, stop);
      assert.ok(steps.length >= 1 && steps.length <= 6, `${asking}/${stop}: ${steps}`);
      assert.equal(steps[0], asking);
      for (let i = 1; i < steps.length; i++) {
        assert.ok(steps[i] < steps[i - 1] && steps[i] > 0, `${asking}/${stop}: ${steps}`);
        assert.ok(Number.isInteger(steps[i]), `${asking}/${stop}: ${steps} not whole`);
      }
    }
  }
});

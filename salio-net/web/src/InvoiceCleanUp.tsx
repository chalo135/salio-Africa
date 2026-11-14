import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import {
  FULL_CROP,
  drawThresholded,
  greyPage,
  previewSize,
  toPng,
  turnedPreview,
  type Crop,
  type Greyed,
} from "./invoiceImage";

// Which part of the crop box a finger is moving.
type Grip = "topLeft" | "topRight" | "bottomLeft" | "bottomRight" | "box";

// The smallest crop, as a fraction of each edge: a box cannot be dragged
// inside out.
const MIN_SIZE = 0.08;

// The crop screen works on a copy this size: quick to redraw while turning.
const PREVIEW_EDGE = 1000;

// Half a corner handle's 44px, kept free round the picture.
const GRIP_ROOM = 22;

function clamp(value: number, low: number, high: number) {
  return Math.min(high, Math.max(low, value));
}

// The photo, cleaned up on this phone before anything is sent.
// First crop and straighten; then see it in black and white, adjust how
// dark counts as ink, and send the PNG.
export default function InvoiceCleanUp({
  image,
  sending,
  onSend,
  onCancel,
}: {
  file: File;
  image: HTMLImageElement;
  sending: boolean;
  onSend: (png: Blob) => void;
  onCancel: () => void;
}) {
  const [stage, setStage] = useState<"crop" | "clean">("crop");
  const [quarterTurns, setQuarterTurns] = useState(0);
  const [fineAngle, setFineAngle] = useState(0);
  const [crop, setCrop] = useState<Crop>(FULL_CROP);
  const degrees = quarterTurns * 90 + fineAngle;

  // The crop screen: a small turned copy, fitted to the space there is.
  const frame = useRef<HTMLDivElement>(null);
  const previewHolder = useRef<HTMLDivElement>(null);
  const [frameSize, setFrameSize] = useState({ width: 0, height: 0 });
  const preview = previewSize(image, degrees, PREVIEW_EDGE);

  useEffect(() => {
    const canvas = turnedPreview(image, degrees, PREVIEW_EDGE);
    canvas.className = "block h-full w-full";
    const holder = previewHolder.current;
    if (holder) {
      holder.replaceChildren(canvas);
    }
  }, [image, degrees, stage]);

  useEffect(() => {
    const element = frame.current;
    if (!element) return;
    const observer = new ResizeObserver(() => {
      setFrameSize({ width: element.clientWidth, height: element.clientHeight });
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, [stage]);

  // Room is left round the picture so a corner handle on its very edge is
  // still whole, and still easy to catch.
  const room = { width: frameSize.width - 2 * GRIP_ROOM, height: frameSize.height - 2 * GRIP_ROOM };
  const fit = Math.max(0, Math.min(room.width / preview.width, room.height / preview.height)) || 0;
  const shownWidth = Math.floor(preview.width * fit);
  const shownHeight = Math.floor(preview.height * fit);

  // Dragging a corner or the whole box.
  const dragging = useRef<{ grip: Grip; startX: number; startY: number; startCrop: Crop } | null>(null);

  function startDrag(grip: Grip, event: ReactPointerEvent) {
    event.preventDefault();
    event.stopPropagation();
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    dragging.current = { grip, startX: event.clientX, startY: event.clientY, startCrop: crop };
  }

  function moveDrag(event: ReactPointerEvent) {
    const drag = dragging.current;
    if (!drag || shownWidth === 0) return;
    const dx = (event.clientX - drag.startX) / shownWidth;
    const dy = (event.clientY - drag.startY) / shownHeight;
    const c = drag.startCrop;
    const next = { ...c };

    if (drag.grip === "box") {
      const w = c.right - c.left;
      const h = c.bottom - c.top;
      next.left = clamp(c.left + dx, 0, 1 - w);
      next.top = clamp(c.top + dy, 0, 1 - h);
      next.right = next.left + w;
      next.bottom = next.top + h;
    } else {
      if (drag.grip === "topLeft" || drag.grip === "bottomLeft") {
        next.left = clamp(c.left + dx, 0, c.right - MIN_SIZE);
      } else {
        next.right = clamp(c.right + dx, c.left + MIN_SIZE, 1);
      }
      if (drag.grip === "topLeft" || drag.grip === "topRight") {
        next.top = clamp(c.top + dy, 0, c.bottom - MIN_SIZE);
      } else {
        next.bottom = clamp(c.bottom + dy, c.top + MIN_SIZE, 1);
      }
    }
    setCrop(next);
  }

  function endDrag() {
    dragging.current = null;
  }

  function turn(by: number) {
    setQuarterTurns((quarterTurns + by + 4) % 4);
    // The page's edges are somewhere else now: start the crop again.
    setCrop(FULL_CROP);
  }

  // The clean-up screen: the black-and-white page and its PNG size.
  const [page, setPage] = useState<Greyed | null>(null);
  const [threshold, setThreshold] = useState(128);
  const [png, setPng] = useState<Blob | null>(null);
  const [working, setWorking] = useState(false);
  const result = useRef<HTMLCanvasElement>(null);

  function clean() {
    setWorking(true);
    setPng(null);
    // Let "Cleaning…" paint before the work holds the page for a moment.
    window.setTimeout(() => {
      const greyed = greyPage(image, degrees, crop);
      setPage(greyed);
      setThreshold(greyed.suggested);
      setStage("clean");
      setWorking(false);
    }, 30);
  }

  // Redraw and re-measure whenever the threshold moves. The PNG is made a
  // moment after the slider stops, so dragging it stays smooth.
  useEffect(() => {
    if (stage !== "clean" || page === null || !result.current) return;
    const canvas = result.current;
    drawThresholded(page, threshold, canvas);
    setPng(null);
    let cancelled = false;
    const timer = window.setTimeout(async () => {
      const made = await toPng(canvas);
      if (!cancelled) setPng(made);
    }, 250);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [stage, page, threshold]);

  const grips: { grip: Grip; x: number; y: number }[] = [
    { grip: "topLeft", x: crop.left, y: crop.top },
    { grip: "topRight", x: crop.right, y: crop.top },
    { grip: "bottomLeft", x: crop.left, y: crop.bottom },
    { grip: "bottomRight", x: crop.right, y: crop.bottom },
  ];

  if (stage === "crop") {
    return (
      <div className="flex min-h-0 flex-1 flex-col">
        <p className="shrink-0 px-3 pt-2 text-base font-bold">1. Crop and straighten</p>

        <div ref={frame} className="relative m-2 min-h-40 flex-1 overflow-hidden">
          <div
            className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2"
            style={{ width: shownWidth, height: shownHeight }}
          >
            {/* The picture and its shading are clipped to the picture; the
                corner handles are not. */}
            <div className="absolute inset-0 overflow-hidden">
              <div ref={previewHolder} className="h-full w-full" />
              <div
                className="pointer-events-none absolute"
                style={{
                  left: crop.left * shownWidth,
                  top: crop.top * shownHeight,
                  width: (crop.right - crop.left) * shownWidth,
                  height: (crop.bottom - crop.top) * shownHeight,
                  boxShadow: "0 0 0 9999px rgba(0,0,0,0.45)",
                }}
              />
            </div>
            {/* The crop box. Everything outside it is shaded. */}
            <div
              onPointerDown={(e) => startDrag("box", e)}
              onPointerMove={moveDrag}
              onPointerUp={endDrag}
              onPointerCancel={endDrag}
              className="absolute cursor-move touch-none border-2 border-primary-foreground outline-2 outline-primary"
              style={{
                left: crop.left * shownWidth,
                top: crop.top * shownHeight,
                width: (crop.right - crop.left) * shownWidth,
                height: (crop.bottom - crop.top) * shownHeight,
              }}
            />
            {grips.map((g) => (
              <div
                key={g.grip}
                onPointerDown={(e) => startDrag(g.grip, e)}
                onPointerMove={moveDrag}
                onPointerUp={endDrag}
                onPointerCancel={endDrag}
                data-grip={g.grip}
                className="absolute flex h-11 w-11 -translate-x-1/2 -translate-y-1/2 touch-none items-center justify-center"
                style={{ left: g.x * shownWidth, top: g.y * shownHeight }}
              >
                <span className="block h-5 w-5 rounded-full border-2 border-primary-foreground bg-primary shadow" />
              </div>
            ))}
          </div>
        </div>

        <div className="shrink-0 space-y-2 border-t border-border bg-surface p-2">
          {/* On a phone the slider gets a row of its own, to be long enough. */}
          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={() => turn(-1)}
              className="min-h-11 flex-1 rounded-lg md:flex-none border-2 border-border-strong px-3 text-base font-bold"
            >
              ↺ 90°
            </button>
            <label className="order-first flex min-w-0 basis-full items-center gap-2 text-sm font-semibold md:order-none md:basis-0 md:flex-1">
              <span className="shrink-0">Straighten</span>
              <input
                type="range"
                min={-15}
                max={15}
                step={0.5}
                value={fineAngle}
                onChange={(e) => setFineAngle(Number(e.target.value))}
                className="min-w-0 flex-1 accent-primary"
                aria-label="Straighten"
              />
              <span className="w-12 shrink-0 text-right font-money">{fineAngle.toFixed(1)}°</span>
            </label>
            <button
              onClick={() => turn(1)}
              className="min-h-11 flex-1 rounded-lg md:flex-none border-2 border-border-strong px-3 text-base font-bold"
            >
              90° ↻
            </button>
          </div>
          <div className="flex gap-2">
            <button
              onClick={onCancel}
              className="min-h-14 flex-1 rounded-xl border-2 border-border-strong bg-surface px-2 text-base font-bold"
            >
              Choose another
            </button>
            <button
              onClick={clean}
              disabled={working}
              className="min-h-14 flex-1 rounded-xl bg-primary px-2 text-xl font-bold text-primary-foreground disabled:opacity-40"
            >
              {working ? "Working…" : "Next"}
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <p className="shrink-0 px-3 pt-2 text-base font-bold">2. Check it reads clearly</p>
      <p className="shrink-0 px-3 text-sm text-muted-foreground">
        Slide until text is clear
      </p>

      <div className="m-2 min-h-40 flex-1 overflow-auto rounded border border-border bg-surface">
        <canvas ref={result} className="mx-auto block h-full max-w-full object-contain" />
      </div>

      <div className="shrink-0 space-y-2 border-t border-border bg-surface p-2">
        <label className="flex items-center gap-2 text-sm font-semibold">
          <span className="shrink-0">Ink</span>
          <input
            type="range"
            min={20}
            max={235}
            value={threshold}
            onChange={(e) => setThreshold(Number(e.target.value))}
            className="min-w-0 flex-1 accent-primary"
            aria-label="Ink"
          />
          <span className="w-10 shrink-0 text-right font-money">{threshold}</span>
          {page !== null && threshold !== page.suggested && (
            <button
              onClick={() => setThreshold(page.suggested)}
              className="shrink-0 px-1 text-sm font-bold text-primary underline"
            >
              Reset
            </button>
          )}
        </label>
        <div className="flex gap-2">
          <button
            onClick={() => setStage("crop")}
            disabled={sending}
            className="min-h-14 flex-1 rounded-xl border-2 border-border-strong bg-surface px-2 text-base font-bold"
          >
            ‹ Crop
          </button>
          <button
            onClick={() => png && onSend(png)}
            disabled={png === null || sending}
            className="min-h-14 flex-1 rounded-xl bg-primary px-2 text-xl font-bold text-primary-foreground disabled:opacity-40"
          >
            {sending ? "Reading…" : "Read invoice"}
          </button>
        </div>
      </div>
    </div>
  );
}

// Cleaning up a photo of an invoice, in the browser, before it is sent.
// A phone photo is a few MB of colour JPEG; what a reader needs is sharp
// black text on white at about 300 DPI. So: crop, straighten, grey, flatten
// the lighting, stretch the contrast, then black or white per pixel, and
// save as PNG. Never JPEG: JPEG smears the edges of letters.

// The crop, as fractions of the straightened picture: 0 is the left/top
// edge, 1 the right/bottom.
export type Crop = { left: number; top: number; right: number; bottom: number };

export const FULL_CROP: Crop = { left: 0, top: 0, right: 1, bottom: 1 };

// The longest edge sent. An A4 page at 300 DPI is 3508 px; 2500 keeps
// small print readable and the file small. Never more than double the
// photo's own pixels: that would only invent blur.
const TARGET_LONGEST_EDGE = 2500;
const MAX_UPSCALE = 2;

export function loadImage(file: File): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file);
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error("unreadable"));
    };
    image.src = url;
  });
}

// The size of the picture once turned by this many degrees: the corners
// stick out, so the box grows.
function turnedSize(width: number, height: number, degrees: number) {
  const radians = (degrees * Math.PI) / 180;
  const cos = Math.abs(Math.cos(radians));
  const sin = Math.abs(Math.sin(radians));
  return { width: width * cos + height * sin, height: width * sin + height * cos };
}

// Draws the image turned by `degrees`, the whole turned picture scaled by
// `scale`, onto a context whose origin is the turned picture's top-left.
function drawTurned(
  context: CanvasRenderingContext2D,
  image: HTMLImageElement,
  degrees: number,
  scale: number,
) {
  const turned = turnedSize(image.naturalWidth, image.naturalHeight, degrees);
  context.save();
  context.scale(scale, scale);
  context.translate(turned.width / 2, turned.height / 2);
  context.rotate((degrees * Math.PI) / 180);
  context.drawImage(image, -image.naturalWidth / 2, -image.naturalHeight / 2);
  context.restore();
}

// The size of the small turned copy for the crop screen.
export function previewSize(image: HTMLImageElement, degrees: number, maxEdge: number) {
  const turned = turnedSize(image.naturalWidth, image.naturalHeight, degrees);
  const scale = Math.min(1, maxEdge / Math.max(turned.width, turned.height));
  return {
    width: Math.max(1, Math.round(turned.width * scale)),
    height: Math.max(1, Math.round(turned.height * scale)),
    scale,
  };
}

// A small turned copy for the crop screen, longest edge `maxEdge`.
export function turnedPreview(image: HTMLImageElement, degrees: number, maxEdge: number) {
  const size = previewSize(image, degrees, maxEdge);
  const scale = size.scale;
  const canvas = document.createElement("canvas");
  canvas.width = size.width;
  canvas.height = size.height;
  const context = canvas.getContext("2d");
  if (context) {
    // Corners that the turn brings in are paper-white, not black.
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, canvas.width, canvas.height);
    drawTurned(context, image, degrees, scale);
  }
  return canvas;
}

// The cropped, straightened page in grey, lighting flattened and contrast
// stretched, ready for a threshold. `suggested` is Otsu's threshold for it.
export type Greyed = { width: number; height: number; grey: Uint8ClampedArray; suggested: number };

export function greyPage(image: HTMLImageElement, degrees: number, crop: Crop): Greyed {
  const turned = turnedSize(image.naturalWidth, image.naturalHeight, degrees);
  const cropWidth = (crop.right - crop.left) * turned.width;
  const cropHeight = (crop.bottom - crop.top) * turned.height;
  const scale = Math.min(MAX_UPSCALE, TARGET_LONGEST_EDGE / Math.max(cropWidth, cropHeight));

  const width = Math.max(1, Math.round(cropWidth * scale));
  const height = Math.max(1, Math.round(cropHeight * scale));
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d", { willReadFrequently: true });
  if (!context) {
    throw new Error("no canvas");
  }

  context.fillStyle = "#ffffff";
  context.fillRect(0, 0, width, height);
  context.imageSmoothingQuality = "high";
  context.translate(-crop.left * turned.width * scale, -crop.top * turned.height * scale);
  drawTurned(context, image, degrees, scale);

  const rgba = context.getImageData(0, 0, width, height).data;
  const count = width * height;

  // Grey: how bright each pixel looks to an eye.
  const grey = new Uint8ClampedArray(count);
  for (let i = 0; i < count; i++) {
    grey[i] = 0.299 * rgba[i * 4] + 0.587 * rgba[i * 4 + 1] + 0.114 * rgba[i * 4 + 2];
  }

  // Flatten the lighting: divide each pixel by the average brightness
  // around it, so a shadow across the page does not turn into a black
  // patch. The window is wider than a line of text.
  const radius = Math.max(8, Math.round(Math.min(width, height) / 30));
  const sums = new Uint32Array((width + 1) * (height + 1));
  for (let y = 0; y < height; y++) {
    let rowSum = 0;
    for (let x = 0; x < width; x++) {
      rowSum += grey[y * width + x];
      sums[(y + 1) * (width + 1) + x + 1] = sums[y * (width + 1) + x + 1] + rowSum;
    }
  }

  const flat = new Uint8ClampedArray(count);
  for (let y = 0; y < height; y++) {
    const y0 = Math.max(0, y - radius);
    const y1 = Math.min(height, y + radius + 1);
    for (let x = 0; x < width; x++) {
      const x0 = Math.max(0, x - radius);
      const x1 = Math.min(width, x + radius + 1);
      const total =
        sums[y1 * (width + 1) + x1] - sums[y0 * (width + 1) + x1] - sums[y1 * (width + 1) + x0] + sums[y0 * (width + 1) + x0];
      const mean = total / ((x1 - x0) * (y1 - y0));
      flat[y * width + x] = mean > 0 ? (grey[y * width + x] / mean) * 230 : 255;
    }
  }

  // Raise the contrast: the darkest 1% become black, the lightest 1% white.
  const histogram = new Uint32Array(256);
  for (let i = 0; i < count; i++) histogram[flat[i]]++;
  let low = 0;
  let high = 255;
  for (let seen = 0, v = 0; v < 256; v++) {
    seen += histogram[v];
    if (seen > count * 0.01) {
      low = v;
      break;
    }
  }
  for (let seen = 0, v = 255; v >= 0; v--) {
    seen += histogram[v];
    if (seen > count * 0.01) {
      high = v;
      break;
    }
  }
  const range = Math.max(1, high - low);
  const stretched = histogram.fill(0);
  for (let i = 0; i < count; i++) {
    flat[i] = ((flat[i] - low) * 255) / range;
    stretched[flat[i]]++;
  }

  return { width, height, grey: flat, suggested: otsu(stretched, count) };
}

// Otsu's method: the threshold that best splits the pixels into two groups
// (ink and paper).
function otsu(histogram: Uint32Array, count: number) {
  let sumAll = 0;
  for (let v = 0; v < 256; v++) sumAll += v * histogram[v];

  let sumBelow = 0;
  let countBelow = 0;
  let best = 0;
  let bestThreshold = 128;
  for (let v = 0; v < 256; v++) {
    countBelow += histogram[v];
    if (countBelow === 0) continue;
    const countAbove = count - countBelow;
    if (countAbove === 0) break;
    sumBelow += v * histogram[v];
    const meanBelow = sumBelow / countBelow;
    const meanAbove = (sumAll - sumBelow) / countAbove;
    const between = countBelow * countAbove * (meanBelow - meanAbove) * (meanBelow - meanAbove);
    if (between > best) {
      best = between;
      bestThreshold = v;
    }
  }
  return bestThreshold;
}

// Black or white per pixel, drawn onto `canvas`. Darker than the threshold
// is ink.
export function drawThresholded(page: Greyed, threshold: number, canvas: HTMLCanvasElement) {
  canvas.width = page.width;
  canvas.height = page.height;
  const context = canvas.getContext("2d");
  if (!context) return;
  const out = context.createImageData(page.width, page.height);
  const data = out.data;
  for (let i = 0; i < page.grey.length; i++) {
    const v = page.grey[i] < threshold ? 0 : 255;
    data[i * 4] = v;
    data[i * 4 + 1] = v;
    data[i * 4 + 2] = v;
    data[i * 4 + 3] = 255;
  }
  context.putImageData(out, 0, 0);
}

// PNG only. Never JPEG for a document.
export function toPng(canvas: HTMLCanvasElement): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error("no png"))), "image/png");
  });
}

// "3.2 MB", "214 KB".
export function fileSize(bytes: number) {
  if (bytes >= 1024 * 1024) {
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

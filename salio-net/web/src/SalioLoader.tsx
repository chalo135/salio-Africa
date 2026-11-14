import SalioWordmark from "./SalioWordmark";

// The animated wordmark with a line saying what is happening. It has no
// timers: the screen asks useShowLoader whether to draw it at all.
//
// "full" covers the whole screen (the form behind it stays where it was, so
// nothing typed is lost). "inline" sits inside one section of a screen.
export default function SalioLoader({
  message,
  size,
}: {
  message: string;
  size: "full" | "inline";
}) {
  if (size === "full") {
    return (
      <div
        role="status"
        aria-live="polite"
        className="fixed inset-0 z-50 flex flex-col items-center justify-center gap-6 bg-background px-4"
      >
        <SalioWordmark animated className="w-72 max-w-[80vw] text-primary" />
        <p className="text-center text-lg text-muted-foreground">{message}</p>
      </div>
    );
  }

  return (
    <div role="status" aria-live="polite" className="flex flex-col items-center gap-2 p-4">
      <SalioWordmark animated className="w-24 text-primary" />
      <p className="text-center text-base text-muted-foreground">{message}</p>
    </div>
  );
}

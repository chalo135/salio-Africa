import { useEffect } from "react";
import SalioWordmark from "./SalioWordmark";

// Any address Salio does not know. Also what /admin shows to anyone the
// server has not confirmed is the admin: the two must be identical, word for
// word, so /admin gives nothing away (decision D7). Do not add anything here
// that depends on the address.
export default function NotFound() {
  useEffect(() => {
    document.title = "Page not found";
  }, []);

  return (
    <div className="flex min-h-full flex-col items-center justify-center bg-background px-4 text-center text-foreground">
      <SalioWordmark className="h-12 w-auto text-primary" />
      <h1 className="mt-6 text-2xl font-bold">Page not found</h1>
      <a
        href="/"
        className="mt-6 flex min-h-12 items-center rounded-xl bg-primary px-6 text-lg font-bold text-primary-foreground"
      >
        Go to Salio
      </a>
    </div>
  );
}

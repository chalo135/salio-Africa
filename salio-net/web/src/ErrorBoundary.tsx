import { Component, type ReactNode } from "react";

// If a screen crashes while drawing, React would leave a white page. This
// catches it and says so in words, with a Reload button. The carts are saved
// on the tablet (cartStorage.ts), so a reload keeps them.
//
// A class, because React only offers this to class components.
export default class ErrorBoundary extends Component<{ children: ReactNode }, { crashed: boolean }> {
  state = { crashed: false };

  static getDerivedStateFromError() {
    return { crashed: true };
  }

  render() {
    if (!this.state.crashed) {
      return this.props.children;
    }

    return (
      <div className="flex min-h-dvh flex-col items-center justify-center bg-background px-4 text-center text-foreground">
        <h1 className="text-2xl font-bold">Something broke</h1>
        <p className="mt-2 max-w-md text-lg">Your carts are kept</p>
        <button
          onClick={() => window.location.reload()}
          className="mt-6 h-14 w-full max-w-sm rounded-xl bg-primary text-xl font-bold text-primary-foreground"
        >
          Reload
        </button>
      </div>
    );
  }
}

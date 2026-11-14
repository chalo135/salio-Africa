/// <reference types="vite/client" />

// The settings this app reads at build time. See web/README.md.
interface ImportMetaEnv {
  // Where the API is. Empty or unset in a real build: same address as the till.
  readonly VITE_API_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

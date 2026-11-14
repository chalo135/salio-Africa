import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './tailwind.css'
import './index.css'
// Root decides by the address: the till, the admin area, or not found.
import Root from './Root.tsx'
import ErrorBoundary from './ErrorBoundary.tsx'
// Registers the service worker and listens for "installable" from the start.
import './install.ts'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ErrorBoundary>
      <Root />
    </ErrorBoundary>
  </StrictMode>,
)

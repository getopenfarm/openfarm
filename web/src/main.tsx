import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './app/App.tsx';

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Could not find #root element to mount the app into.');
}
const root = createRoot(rootElement);
const render = () =>
  root.render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

// `vite --mode demo` builds the marketing-site demo: an in-browser API must be in place before the
// first request. Vite folds MODE at build time, so production bundles never contain the demo.
if (import.meta.env.MODE === 'demo') void import('./demo/install').then(render);
else render();

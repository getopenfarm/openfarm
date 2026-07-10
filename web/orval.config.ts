import { defineConfig } from 'orval';

// Stub config: no real OpenAPI surface exists yet beyond the backend's sample
// ping endpoint. Generation is deferred until M0-proper ships a real endpoint
// set — do not run `bunx orval` against this config until then.
export default defineConfig({
  openfarm: {
    input: '../api/openapi.json',
    output: {
      target: './src/lib/api/generated/endpoints.ts',
      client: 'react-query',
      mode: 'tags-split',
    },
  },
});

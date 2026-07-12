import { defineConfig } from 'orval';

export default defineConfig({
  openfarm: {
    input: '../api/openapi.json',
    output: {
      target: './src/lib/api/generated/endpoints.ts',
      client: 'react-query',
      mode: 'tags-split',
      httpClient: 'fetch',
      override: {
        mutator: {
          path: './src/lib/api/httpClient.ts',
          name: 'httpClient',
        },
      },
    },
    hooks: {
      afterAllFilesWrite: 'biome check --write ./src/lib/api/generated',
    },
  },
});

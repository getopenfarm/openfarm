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
      afterAllFilesWrite: [
        'python3 ../tools/generate-query-permissions.py',
        'biome check --write ./src/lib/api/generated ./src/lib/auth/apiPermissions.generated.ts',
      ],
    },
  },
});

export const DOCS_URL = 'https://getopenfarm.github.io/docs/';

export type Snippet = {
  /** One line of guidance above the code, or the whole instruction when there is no code. */
  text: string;
  code?: string;
  /** Where the code goes (a config file path). */
  file?: string;
  after?: string;
};

export type AssistantClient = {
  id: string;
  label: string;
  signIn: Snippet[];
  /** Null when the assistant cannot send a token and needs sign-in. */
  token: Snippet[] | null;
};

const json = (value: unknown) => JSON.stringify(value, null, 2);

// The assistant expands these variables, not JavaScript.
// biome-ignore lint/suspicious/noTemplateCurlyInString: literal client variable.
const CURSOR_TOKEN = 'Bearer ${env:OPENFARM_TOKEN}';
// biome-ignore lint/suspicious/noTemplateCurlyInString: literal client variable.
const VSCODE_TOKEN = 'Bearer ${input:openfarm-token}';

/** Setup per assistant. `url` is the MCP server URL. Never contains a real credential. */
export function assistantClients(url: string): AssistantClient[] {
  return [
    {
      id: 'claude',
      label: 'Claude',
      signIn: [
        { text: 'In Claude, open Settings › Connectors.' },
        {
          text: 'Choose Add custom connector. Name: OpenFarm. URL:',
          code: url,
        },
        {
          text: 'Sign in to OpenFarm, choose what Claude may do, then choose Allow selected access.',
          after:
            'Claude connects from the internet: the URL must be public HTTPS.',
        },
      ],
      token: null,
    },
    {
      id: 'chatgpt',
      label: 'ChatGPT',
      signIn: [
        {
          text: 'In ChatGPT, open Settings › Apps & Connectors › Advanced settings and turn on Developer mode.',
        },
        {
          text: 'Create a connector with authentication OAuth and this URL:',
          code: url,
        },
        { text: 'Sign in to OpenFarm and choose Allow selected access.' },
      ],
      token: null,
    },
    {
      id: 'claude-code',
      label: 'Claude Code',
      signIn: [
        {
          text: 'Add the server:',
          code: `claude mcp add --transport http openfarm ${url}`,
          after: 'Then run /mcp in Claude Code and choose Authenticate.',
        },
      ],
      token: [
        {
          text: 'Add the server with your token in OPENFARM_TOKEN:',
          code: `claude mcp add --transport http openfarm ${url} \\\n  --header "Authorization: Bearer $OPENFARM_TOKEN"`,
        },
      ],
    },
    {
      id: 'cursor',
      label: 'Cursor',
      signIn: [
        {
          text: 'Add the server:',
          file: '~/.cursor/mcp.json',
          code: json({ mcpServers: { openfarm: { url } } }),
        },
      ],
      token: [
        {
          text: 'Add the server with your token in OPENFARM_TOKEN:',
          file: '~/.cursor/mcp.json',
          code: json({
            mcpServers: {
              openfarm: {
                url,
                headers: { Authorization: CURSOR_TOKEN },
              },
            },
          }),
        },
      ],
    },
    {
      id: 'vscode',
      label: 'VS Code',
      signIn: [
        {
          text: 'Add the server:',
          file: '.vscode/mcp.json',
          code: json({ servers: { openfarm: { type: 'http', url } } }),
        },
      ],
      token: [
        {
          text: 'Add the server. VS Code asks for the token once and stores it securely.',
          file: '.vscode/mcp.json',
          code: json({
            inputs: [
              {
                type: 'promptString',
                id: 'openfarm-token',
                description: 'OpenFarm access token',
                password: true,
              },
            ],
            servers: {
              openfarm: {
                type: 'http',
                url,
                headers: { Authorization: VSCODE_TOKEN },
              },
            },
          }),
        },
      ],
    },
    {
      id: 'other',
      label: 'Other',
      signIn: [
        {
          text: 'Transport: Streamable HTTP. Authentication: Sign in (OAuth). Server URL:',
          code: url,
        },
      ],
      token: [
        {
          text: 'Transport: Streamable HTTP. Send the token in a header:',
          code: 'Authorization: Bearer ofp_…',
        },
        {
          text: 'Codex, in ~/.codex/config.toml, with your token in OPENFARM_TOKEN:',
          code: `[mcp_servers.openfarm]\nurl = "${url}"\nbearer_token_env_var = "OPENFARM_TOKEN"`,
        },
      ],
    },
  ];
}

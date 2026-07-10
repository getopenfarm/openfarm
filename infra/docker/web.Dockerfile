FROM oven/bun:1.3
WORKDIR /app
EXPOSE 5173
ENTRYPOINT ["sh", "-c", "bun install && bun run dev -- --host 0.0.0.0"]

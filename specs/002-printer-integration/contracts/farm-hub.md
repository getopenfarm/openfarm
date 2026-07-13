# Contract: `/hubs/farm` SignalR hub

Tenant-scoped hub groups (one group per `OrganizationId`, joined on connect from the authenticated
JWT's claim, same pattern as the REST endpoints). No polling fallback for any of this data once a
client is subscribed (constitution Principle III/V) — `web/src/features/printers/hooks/useFarmHub.ts`
is the sole real-time entry point, writing into TanStack Query's cache via `setQueryData`.

## Client → Hub

No client-invoked methods this phase — the hub is push-only. Clients simply join their organization's
group on connect (server-side, from the JWT, not a client-invoked "join" call).

## Hub → Client events

### `PrinterStatusChanged`

Fired whenever a `Printer.Status` or `IsReachable` value changes (manual `TransitionTo`, connector
`ObserveStatus`, or a reachability-sweep flip).

```jsonc
{ "printerId": "guid", "status": "AwaitingClear", "isReachable": true }
```

### `PrinterTelemetryUpdated`

Fired on every parsed MQTT telemetry delta that changes a displayed field (temperatures, progress,
current stage, or AMS contents) — SC-002 requires this to reach the client within 5 seconds of the
underlying MQTT message.

```jsonc
{
  "printerId": "guid",
  "telemetry": { "nozzleTempC": 219.4, "bedTempC": 59.8, "printProgressPercent": 42, "currentStageLabel": "printing" },
  "amsUnits": [ { "unitIndex": 0, "trays": [ { "slotIndex": 0, "empty": false, "materialType": "PLA", "colorHex": "1A1A1AFF", "remainPercent": 62, "active": true } ] } ]
}
```

### `CameraFrameReceived`

Fired per decoded camera frame, regardless of whether it originated from the `CAMERA_IMAGE` binary
push (P1/A1) or the FFmpeg-relayed RTSP stream (X1) — one contract for both transports (research.md
§4). SC-005 requires the first frame within 3 seconds of opening the detail page.

```jsonc
{ "printerId": "guid", "frameJpegBase64": "..." }
```

Camera-unavailable state (feed temporarily unreachable, or the printer's camera transport hasn't
produced a frame yet) is represented by the client-side detail page simply not having received a
recent `CameraFrameReceived` event for that printer within a short freshness window — no separate
"camera unavailable" event is needed; the frontend degrades gracefully by timeout (FR-009).

## Group membership

Clients join the group for their organization automatically on hub connection (server reads the
`organization_id` claim, same Finbuckle-scoped tenant resolution as REST). All three events above are
sent only to the connecting organization's group — never broadcast across tenants.

# Snapmaker U1 connectivity reliability

Improve U1 printer API handling while retaining OpenFarm production safeguards. Existing features only; no database migration or new controls.

## Implementation checklist

- [x] Introduce a shared uncertain-start exception and recognize it in dispatch for Bambu and U1.
- [x] Add regression tests for lost U1 start acknowledgement and restart reconciliation.
- [x] Introduce a per-printer session owner with readiness, cancellation, reconnect generations, command watchers and serialized dispatch. Separate filament, alerts and metadata/history state from pure mapping.
- [x] Match query-token WebSocket auth, object-list-filtered subscriptions, 30-second RPC deadlines, 120-second controls and exponential 5–300 second reconnects. Retain identity validation and U1 mDNS.
- [x] Implement RFID/feed sensor merging and print_task.json conditional polling every 3 seconds, file-change refresh and 5.5-second re-emission. Preserve four-head topology; unknown quantity remains null.
- [x] Implement active and one-shot exceptions, canonical codes, severity >=2 Error, default 3, deduplication and ten-notification retention.
- [x] Replace start_local_print with routing macros and printer.print.start; preserve raw tool numbers. Use content-derived filenames and metadata existence checks with multipart upload.
- [x] Separate pre-start failures from uncertain starts. Never automatically resend an uncertain start. Serialize uploads/dispatch per printer; allow independent printers to progress.
- [x] Replace fixed total upload deadline with connection, inactivity and final-response deadlines; test slow progressing transfers.
- [x] Consume correlated history outcomes and metadata progress/ETA. Preserve Mark ready, reconnect deduplication, attribution and exactly-once deductions.
- [x] Remove synthetic freshness: cached publication, camera wake and transfer activity cannot refresh printer or nozzle observation timestamps. Preserve quiet healthy transports and existing reachability policy.
- [x] Poll camera on port 80 at 1 Hz; session-owned camera wake immediately and every 2 seconds. Isolate camera errors.
- [x] Update documentation and run U1, production/inventory and Bambu regression tests plus backend build.
- [ ] Record physical U1 acceptance separately: mapped print, authentication, controls, sensors, alerts, camera and network loss around start.

Keep public connector/REST/UI interfaces. Preserve object skipping, per-head nozzles and temperatures. Do not add previous-start-API/camera-port fallbacks, cloud/plugin integration or a generic vendor framework.

## Automated verification

- Printer regressions: 273 passed, including Bambu compatibility and the slow U1 upload deadline test.
- Final U1-focused suite, including reconnect/isolation and nested status updates: 72 passed.
- Application suite: 551 passed.
- Docker-backed dispatch and restart reconciliation: 20 passed, including shared and Bambu uncertain-start exceptions.
- API build: zero warnings and zero errors.
- Full Infrastructure suite: 517 passed, one real-slicer golden mismatch. The isolated OrcaSlicer embedded-project case reproduced 11.36 g against the expected 11.09 g. Its test and slicing implementation were not changed by this work.
- Physical U1 acceptance remains pending. No printer was contacted and no visual verification was performed.

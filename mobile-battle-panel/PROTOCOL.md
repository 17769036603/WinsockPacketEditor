# Mobile battle panel frame protocol

This is a local, read-only contract between the Java overlay service and the Rust worker. It carries snapshots only; it has no memory-write or game-command message.

## Transport

- Java starts the worker through the platform su command after the user grants this app root access. The worker writes frames to stdout; stderr is diagnostics only.
- Each frame is a big-endian u32 payload length followed by exactly that many bytes.
- Maximum payload is 1024 bytes. Version 2 is exactly 680 bytes. Reject every other length, version, slot count, position family, nonzero reserved byte, or malformed frame and clear displayed values. EBP1 is not accepted.
- Stderr remains a diagnostics channel. The UI accepts only bounded ENEMY_READER_STATUS records: progress is ENEMY_READER_STATUS|tables|<done_bytes>|<total_bytes> or ENEMY_READER_STATUS|role_nodes|<done_bytes>|<total_bytes>; retry is ENEMY_READER_STATUS|retry|0|0|<code>, where code is manager_not_found, manager_ambiguous, battle_data_invalid, read_error, or process_changed. Do not include addresses, resource values, or arbitrary error text in these records. The UI ignores all other stderr output.
- The service may terminate the worker process when stopped. No network or desktop process participates at runtime.

## Version 2 payload

All integer fields are big-endian.

| Offset | Size | Meaning |
| ---: | ---: | --- |
| 0 | 4 | ASCII EBP2 |
| 4 | 1 | Overall status: 0 no game, 1 locating, 2 not in battle, 3 live, 4 root denied, 5 read error, 6 expired |
| 5 | 1 | Slot count; exactly 20 |
| 6 | 2 | Reserved, must be zero |
| 8 | 4 | Game PID, zero when no process is bound |
| 12 | 8 | Process start ticks |
| 20 | 8 | Battle ID |
| 28 | 4 | Round number |
| 32 | 8 | Snapshot Unix time in milliseconds |
| 40 | 640 | Twenty fixed 32-byte slots |

Each slot is position:u32, state:u8, reserved:[u8;3], hp:i64, mp:i64, sp:i64. State values are 0 unused/unknown, 1 empty, 2 alive with all resources, 3 dead, and 4 alive with validated HP/MP but unknown SP. State 4 requires the encoded sp sentinel to be zero; it is displayed as a dash, never as numeric zero. HP/MP are meaningful for states 2 and 4; SP is meaningful only for state 2. SP uses `m_Speed` when present; if absent, only a validated own-side player, pet, or partner attribute may supply the client attribute value. Battle speed modifiers are not represented by that fallback. Enemy and other-player speeds remain unknown. Only an absent `m_Speed` permits fallback: a present malformed speed or invalid HP/MP still rejects the sample. During normal sampling, a mapped non-local slot whose parsed identity disagrees with the mapped role ID, raw position, local-player marker, or current battlefield is encoded as state 1 (empty/unknown) for that slot; the rest of the frame can be emitted only after all frame-level checks pass. A local identity mismatch, missing or unreadable identity fields, or any other read error still rejects the sample. Cold manager discovery remains strict for all mapped roles. Dead roles do not read SP; their identity and HP/MP checks remain in place. Older decoders reject state 4; the Java decoder and bundled worker must be updated together.

Slots are ordered as the local player's side first, then the opposing side. Within each side, the first five slots are main units in position order and the next five are pets in position order. When the local main position is 1..5, the local side uses 1..5 and 101..105; the opposing side uses 10001..10005 and 10101..10105. When the local main position is 10001..10005, those two sides are reversed. The worker rejects any other local main-position family rather than guessing. It validates the local role and every readable mapped role against role ID, raw position, local-player flag, and current battlefield. Process identity, BattleId, round, manager fields, and the role-position map are checked for the whole sample.

The worker does not read `m_AnalyzeFinish` or `m_CanOrder`; only BattleId zero identifies `NotInBattle`. For an already identity-bound manager candidate, the worker reads `m_BattleId` independently before validating role tables. A zero BattleId is accepted as `NotInBattle` only after process identity and the current memory maps are rechecked; it is rechecked before the sample completes and emits no slots. A nonzero BattleId must pass the complete manager, round, role identity, and position-map validation before `Live` is emitted. Cold discovery continues to require complete validated manager fields. Round zero retains `Locating` semantics.

The Java decoder accepts only live frames whose process identity, nonzero start ticks and battle ID, positive round, slot count, both position families, reserved bytes, and timestamp validate. Unsigned 64-bit start ticks and battle IDs may occupy the full nonzero range. A frame timestamp may be no more than two seconds in the future. A same-process `Locating`, `ReadError`, or worker `Expired` frame leaves the last live values visible without refreshing their timestamp; the existing five-second age check still clears them based on the frame timestamp. Confirmed `NotInBattle`, `NoGame`, root denial, a different process identity, a malformed stream, or a live frame older than five seconds clears values.

The worker may persist a versioned manager-address candidate in the app-private cache directory. Every cache entry is keyed by package, PID, start ticks, and executable; it is discarded on identity or format mismatch and the manager is revalidated before use. The address is only a candidate, not a stable pointer chain or a cross-process game address.

## Operational boundary

The APK and worker are separate from the existing desktop WPE MobileSync client. The app does not send packets, modify process memory, inject code, or call game methods. Root access is a runtime requirement for reading another process and must be checked on the target emulator; a successful desktop ADB read does not establish APK access.

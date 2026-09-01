# Multiplayer rooms and runs

Aviscribe multiplayer can share Talkatoo run facts through a compatible SMOO+ server without interacting with the game connection. Select **Multiplayer** on the Run screen to create a room, join one with an `XXXX-XXXX` code, or explicitly rejoin a previous room.

The SMOO+ Server build must include Aviscribe integration.

A SMOO+ server port hosts one multiplayer room, matching the group playing on that port. The room has one current run, and its Aviscribe player limit is the server's normal maximum-player setting.

The shared state is limited to moon hint/collection facts, manual counted/wrong overrides, category, postgame mode, players, room ownership, and the activity feed. The room owner can update category or postgame mode in place without clearing moon state, or start a new run when a clean slate is intended. Current kingdom, route ordering, language, capture and crop settings, OCR runtime, hotkeys, and overlay settings remain local to each Aviscribe instance.

Automatic detections are shared only while capture is running. Deliberate corrections remain shareable while connected even if capture is paused. Network failures keep local corrections responsive and queue idempotent events for retry; sharing pauses with a warning if the persisted queue reaches 500 events or 1 MiB.

The text-file output can optionally include only Pending hints owned by the local player while connected to a room. That preference does not filter single-player output.

Closing Aviscribe retains a separate rejoin record but never reconnects automatically on the next launch. Choosing **Leave Room** removes that record. Expired or closed rooms keep the last synchronized moon state locally and leave multiplayer mode.

Moon identities use compact integer kingdom and moon IDs on the wire. The IDs are derived deterministically from the normalized local gameplay catalog. A SHA-256 catalog hash covers owning kingdom, moon ID, collection kingdom, story status, and multi-moon status; translated names and images are deliberately excluded.

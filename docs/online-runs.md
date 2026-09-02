# Multiplayer with SMOO+

Aviscribe can synchronize Talkatoo hints, collected moons, and manual corrections between players in a shared run. Multiplayer uses a compatible SMOO+ server; it does not connect to or modify the game connection itself.

## Requirements

- The SMOO+ server must be running a build with Aviscribe integration.
- Each player needs the server address and port from the server operator.
- Players should use compatible Aviscribe builds. If Aviscribe reports a catalog mismatch, update everyone to the same current build.

Each SMOO+ server port can host one Aviscribe room. The server's normal player limit also applies to the room.

## Create or join a room

1. On the **Run** screen, select **Multiplayer**.
2. Enter the SMOO+ server address and port, plus the name other players should see.
3. Choose one of the following:
   - Select **Create Room** to start a blank shared run, then send the displayed join code to the other players.
   - Enter a join code and select **Join Room** to connect to an existing run.
4. Start capture when you are ready to share automatic detections.

Creating or joining a room replaces the current local moon state with the room's run. Aviscribe asks for confirmation first if the local run is not empty. Capture settings, route order, language, hotkeys, and overlay settings are kept.

## During a run

Aviscribe shares:

- Talkatoo hints and collected moons
- manual **Pending**, **Counted**, **Wrong**, and removal corrections
- the run category and postgame setting
- the player list and recent activity

Capture, crop, language, route, hotkey, and overlay settings remain local to each player. Automatic detections are shared only while capture is running, but manual corrections are still shared while capture is paused.

For an OBS text source, enable **Only include my hints in multiplayer** under **Settings > Overlay output** to exclude hints found by other players. This setting does not affect singleplayer output.

## Room owner controls

The room owner manages the shared run:

- **Apply Settings** changes the category or postgame setting without clearing moon state.
- **Start New Run** clears the shared moon state for every player.
- **Close Room** permanently closes the room for everyone.

## Leaving and reconnecting

Aviscribe attempts to reconnect while it remains open. If the app is closed, it remembers the room but does not reconnect automatically at the next launch; open **Multiplayer** and select **Rejoin previous room**.

Select **Leave Room** to disconnect and delete the saved rejoin information. The last synchronized moon state remains available locally after leaving, or after a room expires or is closed.

## Troubleshooting

- **Cannot connect:** Confirm that the server is running, the address and port are correct, and the server accepts connections from the player's network.
- **Server does not support Aviscribe:** The server needs a SMOO+ build with Aviscribe integration and must have Aviscribe support enabled in the server config.
- **Cannot join:** Check the join code and make sure the room is still open and has space.
- **Catalog mismatch:** Update all players to the same current Aviscribe build before trying again.
- **Sharing paused:** Read the message on the Multiplayer screen. Keep Aviscribe open while it reconnects. If it cannot recover, restart Aviscribe and select **Rejoin previous room**, or leave and join again with the room code.

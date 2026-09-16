# Button-free volley prototypes

Two scene copies (the original `Demo.unity` is unchanged):

- `Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity`: angle an open receiving face toward a target.
- `Assets/GloveBallDemo/Scenes/VolleySpike-codex.unity`: strike a dropping ball toward the lower targets.

Use **GloveBall Demo > Volley > Open Receive / Open Spike**, then Play in Unity over Meta Quest Link / Air Link. These are Editor prototypes, not new APKs or switcher slots. The existing v2 build still uses Demo.unity.

## Inputs

XR Hands 1.7.3 and OpenXR Hand Tracking Subsystem are enabled for Windows and Android. Quest 3S hands are preferred automatically; tracked Touch controller grip poses are the fallback (including Quest 2). No grip, trigger, thumbstick or menu button is used in these scenes. Input Mode on each `Volley Left/Right Palm` can force HandsOnly or ControllersOnly. Hands are intentionally simple open-hand contact-plane proxies, not animated finger meshes.

Enable hand tracking in the headset. For PC hand tracking, ensure the Meta Quest Link runtime exposes hands / developer runtime features; Meta must be the active OpenXR runtime. If hand data is absent the board reports `lost`; holding controllers lets you test the same physics immediately. Support is implemented but Quest 3S hand latency and Quest 2 controller feel still require physical validation.

## Game loop and tuning

- Head and at least one hand must be tracked. A short READY period is followed by one ball at a time. All three targets hit -> new random placement. No lives, miss penalty, grab, charged shot, aiming ray or button movement.
- `Volley Drill - Receive/Spike` owns feed distance, contact height relative to the tracked head, flight duration, lateral spread, ready delay, serve interval, restitution, swing contribution and return-speed cap.
- Receiving surface orientation determines reflection; hand velocity contributes to the shot. No target snapping/automatic aim. A flat or inward-facing surface can return the ball into the torso; body contact is feedback, not required to score or forced by artificial homing.
- Each palm exposes contact radius, controller pose offset, maximum tracked speed and reacquisition delay. Local Y is the surface normal. Swept relative contact checks reduce missed fast crossings. One ball-wide cooldown prevents L/R double hits.
- Torso is a solid capsule following the head, accepts both incoming and returned balls, and uses the existing body impact feedback. Left/right returns use distinct existing volleyball arm impact events. There is no controller rumble call.
- Tracking loss hides that hand and disables its contact. Loss of both hands or headset clears the current ball and pauses feeds. Source changes, large pose jumps and reacquisition reset velocity and impose a settling delay.
- Only volleyball is selected in the copied scene pools. Feed damping is zero for predictable timing. Existing shared physics/audio/haptic assets are not retuned.

## Verification

New EditMode tests cover ball/hand sweeps, miss rejection, receive/spike directions, no target assistance, speed clamp, feed timing, no-grab state transition and both scene configurations. Existing manual haptic settings intentionally differ from the legacy factory-catalog test: the kit now has 31 rather than 16 events, and arm clips were remapped to body clips. Do not run the old asset generator to “fix” that test.

Manual checks: hands in front of HMD -> READY -> receive; switch to Spike while out of Play; try a downward/forward swing; rotate the receiving face inward and observe torso bounce; briefly hide both hands and verify feeds pause; pick up controllers and verify pose-only play. Use a clear physical play area; jumping or deliberately taking facial hits is not required.

The one-time Create Copies menu refuses to overwrite existing drill scenes. Edit the copies directly. Do not regenerate the original scene.

# Original ball-feeder skin

Blender-authored low-poly twin-roller ball feeder, with open outlet, feed basket, stored balls and wheeled stand. No third-party model or texture inputs.

- `ball_feeder.blend`: editable source, Git LFS.
- `create_launcher.py`: protected initial generator; refuses to overwrite authored outputs.
- Runtime FBX: `unity/gloveball/Assets/GloveBallDemo/Art/BallFeeder/`.
- Mesh forward is Blender -Y / Unity +Z before placement. Skin attaches at the existing launcher origin (1.2m above floor) and rotates to the existing muzzle direction (local -Z 0.6m in Receive). The muzzle is not moved.
- Skin has no logic or colliders. Original Body/Barrel renderers are hidden only in Receive; their colliders, launcher transforms and muzzle remain unchanged.
- ReceiveCourtBlue is a scene-local unlit material assignment replacing the floor atlas and excluding baked obstacle silhouettes. This removes the octagonal floor markings; the tradeoff is no dynamic shadows on the plain floor. The separate white court-line mesh and original shared atlas remain untouched.

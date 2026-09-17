# Original ball-feeder skin

Blender-authored low-poly twin-roller ball feeder, with open outlet, feed basket, stored balls and wheeled stand. No third-party model or texture inputs.

- `ball_feeder.blend`: editable source, Git LFS.
- `create_launcher.py`: protected initial generator; refuses to overwrite authored outputs.
- Runtime FBX: `unity/gloveball/Assets/GloveBallDemo/Art/BallFeeder/`.
- `ball_feeder_articulated.blend`: two-part variant generated with `--articulated`; the original source remains protected. Base/stand/wheels are stationary; housing, basket and outlet rotate together.
- Mesh forward is Blender -Y / Unity +Z before placement. `VolleyFeederAim` uses a unit-scale pivot at the launcher origin (1.2m above floor), and a new muzzle 0.6m along pivot +Z. The inherited muzzle child is retained; only BallLauncher's reference is overridden.
- The upper part tracks the current head-height destination at 120 degrees/second. At launch it aligns with the final randomized ballistic velocity, iteratively accounting for the moved muzzle. The drill still owns all firing. Original Body/Barrel renderers remain hidden; no new skin colliders.
- ReceiveCourtBlue is a scene-local unlit material assignment replacing the floor atlas and excluding baked obstacle silhouettes. This removes the octagonal floor markings; the tradeoff is no dynamic shadows on the plain floor. The separate white court-line mesh and original shared atlas remain untouched.

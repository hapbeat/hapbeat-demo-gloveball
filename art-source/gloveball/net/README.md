# Volleyball net

Original low-poly model created in Blender 5.2.1 LTS for this demo. No third-party mesh, texture or model was copied. A search for ready-to-use CC0 nets did not establish a suitable complete asset; this model uses regulation dimensions instead.

- Editable source: `volleyball_net.blend` (Git LFS).
- Protected generator: `create_net.py`; refuses to overwrite existing authored outputs.
- Unity runtime: `Assets/GloveBallDemo/Art/VolleyballNet/VolleyballNet.fbx` and three URP materials.
- Net top 2.43 m, mesh band 1 m high and 9.5 m long, 10 cm mesh spacing. Posts 2.55 m. Top/bottom tapes 7/5 cm. This is the men's height; women's regulation height is 2.24 m.
- Reference: [FIVB Official Volleyball Rules 2025–2028, section 2](https://www.fivb.com/wp-content/uploads/2025/01/FIVB-Volleyball_Rules2025_2028-EN-v05.pdf).
- One mesh / three materials. Simplified solid box collider for the net (not individual cord physics). No cloth simulation or downloaded asset license dependency.

The Receive scene hides the old BallSpawner marker and places the net at its centre. The three actual volley feed launchers remain visible and active as emission references.

## Cosmetic impact response

`volleyball_net_deformable.blend` / `VolleyballNetDeformable.fbx` are a separate, subdivided variant (`create_net.py --deformable`), preserving the original asset. Receive uses this mesh with Read/Write enabled. `VolleyNetResponse` clones the mesh per instance; only the net rectangle dents along its normal. Posts, cables, collision box and the shared source mesh remain fixed. Up to four impact ripples decay and restore the original vertices; no cloth solver is used.

Inspector on `Volley Net`: Normal Retention 0.08, Tangential Retention 0.25, Maximum Dent 0.18 m, Dent Radius 0.7 m, Decay 5, Oscillation 16 rad/s. The fixed collider uses NetLowBounce; the collision callback reduces outgoing ball velocity independently of each ball's floor-bounce material. No new haptic events or audio are emitted by the net. Retained gravity makes the slowed ball fall naturally. ReceiveCourtBlue is now light cyan; original stadium materials remain unchanged.

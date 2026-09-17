# Volleyball net

Original low-poly model created in Blender 5.2.1 LTS for this demo. No third-party mesh, texture or model was copied. A search for ready-to-use CC0 nets did not establish a suitable complete asset; this model uses regulation dimensions instead.

- Editable source: `volleyball_net.blend` (Git LFS).
- Protected generator: `create_net.py`; refuses to overwrite existing authored outputs.
- Unity runtime: `Assets/GloveBallDemo/Art/VolleyballNet/VolleyballNet.fbx` and three URP materials.
- Net top 2.43 m, mesh band 1 m high and 9.5 m long, 10 cm mesh spacing. Posts 2.55 m. Top/bottom tapes 7/5 cm. This is the men's height; women's regulation height is 2.24 m.
- Reference: [FIVB Official Volleyball Rules 2025–2028, section 2](https://www.fivb.com/wp-content/uploads/2025/01/FIVB-Volleyball_Rules2025_2028-EN-v05.pdf).
- One mesh / three materials. Simplified solid box collider for the net (not individual cord physics). No cloth simulation or downloaded asset license dependency.

The Receive scene hides the old BallSpawner marker and places the net at its centre. The three actual volley feed launchers remain visible and active as emission references.

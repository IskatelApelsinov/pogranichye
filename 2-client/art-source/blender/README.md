# Original low-poly tavern prop pack

This folder contains the editable Blender source for a small, server-independent prop pack intended for the Unity 6 / URP village and tavern scene.

## Included props

The generator builds four original, metre-scale assets:

| Asset | Intended use | Construction notes |
|---|---|---|
| Oak tankard | Tavern tables and held beer item | Tapered stave body, iron hoops, curved handle, amber ale and foam |
| Oak barrel | Tavern yard, store rooms and market | Bulged stave body, three forged hoops, top and bottom |
| Wicker market basket | Market stalls and farm yards | Tapered wicker body, woven courses, bread and apples |
| Rustic bench | Tavern and village streets | Separate seat planks, legs and cross-braces |

The wooden handcart is deliberately not duplicated: it already has a dedicated Unity mesh pass in the main project.

## Generate

Requires Blender 4.2 or newer. No third-party models or add-ons are used. The generator attempts FBX and glTF independently: one exporter may fail while the other still produces a usable asset. Each manifest entry records successful formats and any exporter warnings; every prop must produce at least one format for the run to succeed. If neither format is available, enable the corresponding Blender exporter before running.

From this folder:

```bash
blender --background --python generate_tavern_props.py
```

The script creates an ignored `generated/` folder containing:

- `Pogranichye_Tavern_Props.blend` — editable master scene;
- one `.fbx` per prop for direct Unity import when the built-in FBX exporter is available;
- one `.glb` per prop for interchange/review when the built-in glTF exporter is available (at least one exporter must be available);
- `textures/*.png` — original 128×128 procedural colour textures;
- `tavern_props_preview.png` — a simple asset-sheet render;
- `asset_manifest.json` — generated triangle counts and export metadata.

The script targets roughly 300–2,000 triangles per prop and records the actual count plus an in-budget flag in the manifest. It does not download or embed any third-party assets. All geometry and texture pixels are generated locally and deterministically.

After generation, run the Blender-side structural check:

```bash
blender --background --python validate_tavern_props.py
```

The validator opens the generated scene, checks the four root objects and ground-centred pivots, finite mesh geometry, ground contact, triangle budgets, UVs for image-textured meshes, packed 128×128 textures, preview image, and the actual files for every export recorded in the manifest. It exits non-zero on a failed check. This is a separate Blender runtime check; Python syntax CI does not execute Blender.

## Scale, pivots and Unity import

- Scene units are metric; one Blender unit represents one metre.
- Each prop has a named root empty at the centre of its ground footprint. Child meshes are authored in local metres above that origin.
- Tankard and barrel stand on Z=0; the basket and bench also use ground-centred origins.
- FBX export uses Unity-friendly Y-up / -Z-forward axis settings and applies unit scale.
- Import the FBX into Unity with Model Scale 1.0. Check the tankard against the existing held-item mug once before replacing any current procedural view.
- Extract or create URP/Lit materials in Unity. Use the exported PNGs as Base Map textures; do not rely on Blender Principled BSDF shader conversion.
- Keep colliders separate from the render mesh. The tankard must remain non-static while attached to a character hand; placed tavern props may be marked static.
- These are presentation assets only. They do not define server collision, POI coordinates, interaction range or authoritative item state.

## Validation status

The GitHub Actions workflow runs `python3 -m py_compile` on the source. This validates Python syntax only; the repository's current execution environment did not contain Blender, so Blender operators, generated `.blend`/FBX/GLB files, triangle counts, texture packing, preview render and Unity import scale remain unverified. The first Blender run must validate those before the assets are used in the playable scene.

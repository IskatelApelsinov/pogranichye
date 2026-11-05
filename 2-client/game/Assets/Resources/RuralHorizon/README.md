# Rural horizon JPEGs

The Unity client optionally loads eight directional background images from this Resources folder:

- North.jpg
- NorthEast.jpg
- East.jpg
- SouthEast.jpg
- South.jpg
- SouthWest.jpg
- West.jpg
- NorthWest.jpg

Recommended source image: 2048×512 (or 1024×256), landscape panorama strip, with the skyline/horizon around the lower third. Use consistent season, time of day, exposure and art style across all eight directions. JPEG is appropriate for photographic/painterly distant landscapes; use quality 85–92. Keep each image under roughly 1 MB where practical.

The backdrop is a presentation-only ring placed outside the 300×300 m playable Layout v2 world. It follows the presentation camera horizontally, has no colliders, does not alter server movement, and must never be treated as navigable terrain. Missing directions are simply skipped; if no images are present, the existing sky and distant hills remain unchanged.

Use original generated images or assets with explicit commercial-use permission/CC0. Record any third-party source and license alongside the asset before committing it. Do not download random "free" JPEGs without checking their license.

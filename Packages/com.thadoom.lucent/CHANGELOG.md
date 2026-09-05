# Changelog

## [1.0.0]

Initial release.

- **Import automation** — new textures import at 4096 / BC7 / mips on instead of Unity's 2048 / DXT
  defaults. Only fires on assets with no `.meta`, so hand-tuned settings are never overwritten.
- **Project settings fixer** — forces Global Mipmap Limit to Full Resolution on every quality level,
  disables mipmap streaming, sets anisotropic filtering to Per Texture, flags a non-Linear colour space.
- **Pixel-level texture analysis** — GPU readback detects unused alpha channels, single-channel data
  maps wearing an RGBA costume, flat-colour textures, and mislabelled normal maps.
- **Deep Scan** — measures half-resolution PSNR per texture so resolution advice is measured rather
  than guessed. Tells you when to *keep* a texture large, not only when to shrink it.
- **Format decisions** — BC7 for colour and packed masks, BC5 for normals, BC4 for single-channel,
  BC6H for HDR, DXT1 for opaque low-frequency colour in Balanced mode.
- **Mesh analysis** — Read/Write, mesh compression, blend shape normal memory, UV channel count,
  vertex/triangle split ratio, Skinned Motion Vectors, Update When Offscreen.
- **VRChat PC ranking** — live texture memory bar and every rank metric with before/after projection.
- **Bulk retarget** — apply the rules to an existing folder of already-imported textures.

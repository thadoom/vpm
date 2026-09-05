# Lucent Optimizer

**A quality-first texture and mesh optimizer for VRChat PC.**

Most optimizers start by asking how much they can throw away. Lucent starts by asking what each
asset needs in order to look right, fixes everything Unity got wrong by default, and only then goes
looking for memory nobody will miss. It never trades a visible pixel for a megabyte.

Unity 2022.3 · VRChat PC (Windows standalone) · Editor-only, nothing ships in your build.

---

## Install

### → [Add Lucent to VCC](https://thadoom.github.io/vpm/)

Open that page and click **Add to VCC**. Then in the Creator Companion open your project,
go to **Manage Project**, and install **Lucent Optimizer**.

In Unity: **Tools → Lucent → Optimizer** (`Ctrl+Shift+L`).

If the button does not open VCC, add the listing manually under
**VCC → Settings → Packages → Add Repository**:

```
https://thadoom.github.io/vpm/index.json
```

---

## Why Unity has been rendering your 4K textures at 2K

There are three separate places Unity throws away resolution, and fixing only one of them looks
like nothing happened.

**1. Texture Importer → Max Size, which defaults to 2048.** It is written into the `.meta` the
first moment an asset is imported. Your 4096×4096 source becomes a 2048×2048 GPU texture and Unity
never mentions it. Lucent intercepts the first import of every new texture and writes 4096 instead.
It checks `importSettingsMissing`, so it only ever touches assets with no `.meta` yet — settings
you tuned by hand are never overwritten.

**2. Quality Settings → Global Mipmap Limit** (called *Texture Quality* before Unity 2022.2). This
drops the top mip of **every texture in the project** at runtime, and Unity ships several quality
levels with it set to Half Resolution. Your importer says 4096, your GPU gets 2048, and nothing in
the inspector tells you. Lucent forces it to Full Resolution on every quality level, not just the
active one.

**3. Mipmap Streaming.** On without a budget, textures load at a low mip and sometimes never climb
back. Lucent turns it off — avatars are not streamed.

---

## What it changes, and why none of it is a trade-off

| Change | Why it costs you nothing |
|---|---|
| **BC7 instead of DXT5** on colour maps | Identical 8 bits per pixel, identical VRAM. BC7 has far more block modes, so gradients, skin tones and hair stop banding. The biggest free quality win in Unity, and almost nobody flips it. |
| **BC5 instead of DXT5nm** on normal maps | Same 8bpp. Two dedicated channels instead of an alpha-swizzle hack — no more shimmering on curved surfaces. |
| **BC4 for single-channel maps** | 4bpp instead of 8. If a texture is grayscale and opaque, three of its channels were storing a copy of the first. |
| **Stripping a fully opaque alpha** | 8bpp → 4bpp with a mathematically identical result. Lucent reads the actual pixels to confirm the alpha is unused first. |
| **Crunch off** | Crunch shrinks the *download*, not one byte of VRAM, and it is lossy on top of BC. It also stops d4rkAvatarOptimizer from merging the texture into an array. |
| **Mip maps on, Kaiser filter, aniso 4** | Mips are not a quality cost, they are a quality gain at any distance, and they stop the texture cache thrashing. |
| **Correct sRGB flags** | A mask map imported as sRGB feeds gamma-curved numbers to a shader expecting linear ones. Free correctness. |
| **Read/Write off on meshes** | Unity keeps a second full copy in system RAM. VRChat never reads it. |
| **Mesh Compression off** | It quantizes vertex positions and UVs. Shrinks the file on disk, not runtime memory. Pure loss. |
| **Skinned Motion Vectors off** | Roughly doubles a mesh's skinning cost. VRChat does not use them. |
| **Update When Offscreen off** | Forces a full skinning pass every frame even when the mesh is behind you. |

---

## Deep Scan

Every tool that tells you to halve your textures is guessing. Lucent measures.

For each texture it renders the full-resolution version, renders a half-resolution version scaled
back up, and computes PSNR between the two. Above the threshold — 48 dB by default, invisible even
in a side-by-side at 100% zoom — the extra resolution is carrying no information and Lucent offers
to halve it. Below it, Lucent explicitly tells you to **leave the texture alone**.

This is why one scan will often raise the Max Size of one texture and lower another's in the same
avatar. That is the point.

---

## Lucent and d4rkAvatarOptimizer

They do not overlap, and the order matters.

Lucent changes how assets are **stored**: import settings, formats, resolutions, renderer flags.
Non-destructive, reversible, and it improves your source project permanently.

[d4rkAvatarOptimizer](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer) changes how the avatar is
**structured** at build time: merging skinned meshes, merging materials into constant buffers,
building `Texture2DArray`s, baking blend shapes, collapsing FX layers. Destructive transforms
applied to a copy at upload.

Run Lucent first. d4rk's texture-array merging needs textures that share dimensions and compression
format and are **not crunched** — exactly the state Lucent leaves them in. Feeding it a project of
mixed DXT1/DXT5/crunched textures is why "merge same dimension textures" quietly does nothing for
most people.

Lucent deliberately does not merge meshes, merge materials, bake blend shapes, or touch your
animator. d4rk already does all of that properly.

---

## Also in the window

- Live texture memory against the VRChat PC ranking (40 / 75 / 110 / 150 MB), before and after the
  proposed fixes.
- Every rank metric: triangles, skinned meshes, material slots, bones, PhysBones, particle systems.
- Per-texture pixel facts — is the alpha doing anything, is this really one channel of data, is it
  a flat colour wearing a 2048² costume, is it a mislabelled normal map.
- Mesh report: blend shape memory, UV channel count, vertex/triangle split ratio, Read/Write state.
- Bulk retarget for folders of textures imported before you installed Lucent.

Every fix is a `.meta` change covered by Undo. Nothing touches your meshes, materials, animator or
prefab structure.

---

## Credits

Thanks to **[d4rkc0d3r](https://github.com/d4rkc0d3r)** for
[d4rkAvatarOptimizer](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer), which set the bar for what
avatar optimization on VRChat should look like and remains the right tool for the build-time half of
the job. Lucent is built to hand it clean inputs, not to replace it.

Thanks to the VRChat creator community for years of documenting what the ranking system actually
measures.

---

## License

MIT. See [LICENSE](LICENSE).

Maintaining this repository and publishing new versions: [MAINTAINING.md](MAINTAINING.md).

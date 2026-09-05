# Lucent — quality-first optimizer for VRChat PC

Unity's defaults quietly cost you quality. Lucent's job is the opposite of most optimizers:
**it starts from "what does this texture actually need to look perfect", and only then looks for VRAM
that nobody will miss.** It never trades a visible pixel for a megabyte.

Target: Unity 2022.3.22f1, VRChat PC (Windows standalone).

---

## The two-minute version

1. Install (below), open **Tools → Lucent → Optimizer** (`Ctrl+Shift+L`).
2. Go to **Project Settings** tab → **Fix all project settings**. This is the one that stops Unity
   rendering your 4K textures at 2K.
3. Drop your avatar into the **Avatar / root** field → **Scan**.
4. Read the **Textures** tab. Every row tells you what it wants to change and why.
5. Run **Deep Scan** when you want resolution advice that is measured rather than guessed.
6. **Apply selected**.
7. *Then* run d4rkAvatarOptimizer at upload time. Lucent and d4rk do different jobs — see below.

---

## Why Unity was downscaling your 4K textures

There are three separate places Unity throws away resolution, and fixing only one of them looks like
the tool is broken.

**1. Texture Importer → Max Size (default 2048).**
Written into the `.meta` the first moment an asset is imported. A 4096×4096 PNG becomes a 2048×2048
GPU texture and Unity never mentions it. Lucent's `LucentTexturePostprocessor` intercepts the *first*
import of every new texture and writes `4096` instead. It checks `importSettingsMissing`, so it only
ever touches assets that have no `.meta` yet — it will never stomp settings you tuned by hand.

**2. Quality Settings → Global Mipmap Limit** (called *Texture Quality* before Unity 2022.2).
This drops the top mip level of **every texture in the project** at runtime. Unity ships several
quality levels with it set to Half Resolution. Your importer says 4096, your GPU gets 2048, and
nothing in the inspector tells you. `Fix all project settings` forces it to Full Resolution on
*every* quality level, not just the active one.

**3. Mipmap Streaming.** If it is on without a streaming budget, textures load in at a low mip and
sometimes never climb back up. Off, for avatars.

---

## What Lucent actually changes, and why each one is free quality

| Change | Why it is not a trade-off |
|---|---|
| **BC7 instead of DXT5** on colour maps | Identical 8 bits per pixel, identical VRAM. BC7 has far more block modes, so gradients, skin tones and hair stop banding. This is the single biggest free quality win in Unity and almost nobody flips it. |
| **BC5 instead of DXT5nm** on normal maps | Same 8bpp. Two dedicated channels instead of an alpha-swizzle hack — no more shimmering on curved surfaces. |
| **BC4 for single-channel maps** | 4bpp instead of 8. If a texture is grayscale and opaque, the other three channels were storing a copy of the first one. |
| **Stripping a fully opaque alpha channel** | 8bpp → 4bpp with a mathematically identical result. Lucent reads the actual pixels to confirm the alpha is unused before doing it. |
| **Crunch OFF** | Crunch saves *download* size, not one byte of VRAM, and it is lossy on top of BC. It also makes d4rkAvatarOptimizer refuse to merge the texture into an array. |
| **Mip maps ON, Kaiser filter, aniso 4** | Mips are not a quality cost, they are a quality *gain* at any distance, and they make the texture cache stop thrashing. Kaiser keeps mips sharp instead of mushy. |
| **Correct sRGB flag** | A mask map imported as sRGB feeds gamma-curved numbers to a shader expecting linear ones. Free correctness. |
| **Read/Write OFF on meshes** | Unity keeps a second full copy in system RAM. VRChat never reads it. |
| **Mesh Compression OFF** | It quantizes vertex positions and UVs. It shrinks the file on disk, not runtime memory. Pure loss. |
| **Skinned Motion Vectors OFF** | Roughly doubles the skinning cost of a mesh. VRChat does not use them. |
| **Update When Offscreen OFF** | Forces a full skinning pass every frame even when the mesh is behind you. |

---

## Deep Scan — the honest resolution test

Every "optimizer" that tells you to halve your textures is guessing. Deep Scan measures.

For each texture it renders the full-resolution version, renders a half-resolution version scaled
back up, and computes PSNR between the two. If the difference is above the threshold (48 dB by
default — invisible even in a side-by-side A/B at 100% zoom), the extra resolution is carrying no
information and Lucent will offer to halve it. If it is below, Lucent explicitly tells you to
**leave it alone**.

This is why a Lucent scan will often *raise* a texture's Max Size and lower a different one in the
same avatar. That is the point.

Deep Scan reads textures back from the GPU at full resolution, so it is slow — a few seconds per 4K
texture. Run it once per avatar, not every time.

---

## Lucent vs d4rkAvatarOptimizer — use both, in this order

They do not overlap.

**Lucent** changes how assets are *stored*: import settings, formats, resolutions, renderer flags.
Non-destructive, reversible, and it improves your source project permanently.

**[d4rkAvatarOptimizer](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer)** changes how the avatar is
*structured* at build time: merges skinned meshes, merges materials into constant buffers, builds
`Texture2DArray`s, bakes blend shapes, collapses FX layers into direct blend trees. Destructive
transforms, applied to a copy at upload.

Run Lucent first. d4rk's texture-array merging requires textures that share dimensions and
compression format and are **not crunched** — which is exactly the state Lucent puts them in. Feeding
d4rk a project with mixed DXT1/DXT5/crunched textures is why its "merge same dimension textures"
silently does nothing for most people.

Lucent deliberately does **not** merge meshes, merge materials, bake blend shapes, or touch your
animator. d4rk already does those correctly and has years of edge cases baked in.

---

## Install

### → [Add Lucent to VCC](https://thadoom.github.io/vpm/)

Open that page and click **Add to VCC**. Then in the Creator Companion open your project, go to
**Manage Project**, and install **Lucent Optimizer**.

If the button does not open VCC, add the listing manually under
**VCC → Settings → Packages → Add Repository**:

```
https://thadoom.github.io/vpm/index.json
```

Lucent is Editor-only — the assembly definition is scoped to the Editor, so nothing it contains can
end up in an upload.

---

## Manual settings cheat sheet

If you want to sanity-check what Lucent writes, or set it by hand on one asset:

**Texture Importer — colour map**
```
Texture Type         Default
sRGB (Color Texture) ON
Alpha Source         Input Texture  (None if the alpha is unused)
Generate Mip Maps    ON
Mip Map Filtering    Kaiser
Wrap / Filter        Repeat / Trilinear
Aniso Level          4
Max Size             4096
Resize Algorithm     Mitchell
Compression          High Quality
Format (Standalone)  BC7
Use Crunch           OFF
```

**Texture Importer — normal map**
```
Texture Type         Normal map
Max Size             4096
Format (Standalone)  BC5
Aniso Level          4
```

**Texture Importer — mask / metallic / roughness / AO**
```
Texture Type         Default
sRGB                 OFF
Alpha Source         None (unless a 4th channel is packed)
Format (Standalone)  BC7        <- never DXT1, it correlates the channels
Aniso Level          2
```

**Model Importer (FBX)**
```
Mesh Compression     Off
Read/Write           Off
Optimize Mesh        Everything
Weld Vertices        On
Import Cameras       Off
Import Lights        Off
Blend Shape Normals  None   (unless your shapes genuinely reshade)
Import Tangents      Calculate Mikktspace  (or Import, if the DCC exported them)
```

**Project Settings → Quality** (every level, not just the active one)
```
Global Mipmap Limit    Full Resolution
Texture Streaming      Off
Anisotropic Textures   Per Texture
```

**Project Settings → Player**
```
Color Space            Linear
```

---

## VRChat PC texture memory thresholds

| Rank | Texture memory |
|---|---|
| Excellent | < 40 MB |
| Good | < 75 MB |
| Medium | < 110 MB |
| Poor | < 150 MB |
| Very Poor | ≥ 150 MB |

Lucent shows your avatar against these live, before and after the proposed fixes.

---

## Safety

- Every texture fix is a `.meta` change and is covered by Undo.
- Nothing is destructive to your meshes, materials, animator, or prefab structure.
- The import automation only fires on assets that have never been imported before.
- Exclude any folder you do not want touched in **Settings → Excluded folders**.

Take a backup or commit before a bulk retarget of a large folder anyway. Reimporting thousands of
4K textures takes real time and you want to be able to walk it back.

---

## Credits

Thanks to **[d4rkc0d3r](https://github.com/d4rkc0d3r)** for
[d4rkAvatarOptimizer](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer), which set the bar for what
avatar optimization on VRChat should look like and remains the right tool for the build-time half of
the job. Lucent is built to hand it clean inputs, not to replace it.

Thanks to the VRChat creator community for years of documenting what the ranking system actually
measures.

MIT licensed.

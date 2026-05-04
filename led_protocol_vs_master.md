# LED Protocol Branch vs Master: Differences

This document summarizes every file added or modified on the `led_protocol` branch that does not exist on `master`.

---

## Modified Files (exist on both branches, but changed)

### 1. `org.janelia.background/Runtime/BackgroundChanger.cs`

**What changed:** Added public read-only access to the currently displayed texture filename.

Added in `UseSeparatorTexture()`:
```csharp
_currentTextureName = "";
```

Added in `UseCurrentTexture()`:
```csharp
_currentTextureName = Path.GetFileName(_texturePaths[_current]);
```

Added as static members:
```csharp
public static string CurrentTextureName => _currentTextureName;
private static string _currentTextureName = "";
```

**Why it's needed:** The `TalkToNiDaq_LED_MovieProtocol.cs` script reads `BackgroundChanger.CurrentTextureName` each frame to decide whether to turn the LED on based on which PNG is currently displayed.

> **Note for new computers on master:** You must manually add these changes to `C:\janelia-unity-toolkit\org.janelia.background\Runtime\BackgroundChanger.cs` — see steps in lab notes.

---

### 2. `org.janelia.background/Assets/Resources/TextureMix.shader`

**What changed:** Fixed UV texture tiling calculation in the fragment shader.

Before:
```glsl
fixed4 color1 = tex2D(_MainTex, i.uv + _MainTex_ST.xy);
fixed4 color2 = tex2D(_SecondTex, i.uv + _SecondTex_ST.xy);
```

After:
```glsl
fixed4 color1 = tex2D(_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw);
fixed4 color2 = tex2D(_SecondTex, i.uv * _SecondTex_ST.xy + _SecondTex_ST.zw);
```

**Why it's needed:** Correct standard Unity UV tiling/offset formula so texture scale and offset work properly for the loom disc stimulus.

---

## New Files (only on `led_protocol`, not on `master`)

### Animate Scripts (stimulus controllers)

| File | What it does |
|------|-------------|
| `AnimateCylinderTexture.cs` | Rotates a cylinder texture through elevation steps at configurable speeds. Base script for most stimulus worlds. |
| `AnimateCylinderTextureBottomLimit.cs` | Same as above but with a `maxEl` parameter to cap the elevation range. |
| `AnimateCylinderTextureElevationPause.cs` | Adds a two-phase hold (LED ON then LED OFF) at a configurable azimuth on the first sweep of each elevation step. |
| `AnimateCylinderTextureLoom.cs` | Drives a looming stimulus on the cylinder using configurable per-rate configs (`l`, `v`, `maxScaleFactor`, pauses). Supports regressive looming. |
| `AnimateCylinderTextureOscillate.cs` | Oscillates the cylinder back and forth between two azimuth angles at configurable speeds. |
| `AnimateCylinderTextureOscillateBlock.cs` | Same as oscillate but with inter-block intervals and a solid block variant. |
| `AnimateLoomDisc.cs` | Drives a 3D disc GameObject that looms (scales up) toward the fly. Replaces the texture-tiling loom approach. |

---

### TalkToNiDaq LED Scripts (DAQ I/O controllers)

Each script reads analog inputs from the NI-DAQ (`ai0`, `ai1`, `ai2`) and writes analog outputs:
- `ao0` — photodiode toggle (flips each frame)
- `ao1` — rotation/modulation signal
- `ao2` — LED on/off

| File | LED trigger condition | Paired animate script |
|------|-----------------------|-----------------------|
| `TalkToNiDaq_LED_CylinderRotation.cs` | LED ON when cylinder azimuth is within configurable `AngleRange[]` | `AnimateCylinderTexture` or `AnimateCylinderTextureBottomLimit` |
| `TalkToNiDaq_LED_ElevationPause.cs` | LED ON/OFF controlled by the two-phase hold state in the animate script | `AnimateCylinderTextureElevationPause` |
| `TalkToNiDaq_LED_Loom.cs` | LED ON during loom expansion phase, OFF during inter-sweep pause | `AnimateCylinderTextureLoom` |
| `TalkToNiDaq_LED_LoomDisc.cs` | LED ON during disc loom expansion, OFF during pause | `AnimateLoomDisc` |
| `TalkToNiDaq_LED_MovieProtocol.cs` | LED ON when displayed PNG filename matches `ledOnTextureNames[]` or numeric range `ledOnTextureRanges[]` | `BackgroundChanger` (movie/PNG sequence worlds) |
| `TalkToNiDaq_LED_Oscillate.cs` | LED ON when oscillating azimuth is within configurable `AngleRange[]` | `AnimateCylinderTextureOscillate` |
| `TalkToNiDaq_LED_OscillateBlock.cs` | LED ON when oscillating block azimuth is within configurable `AngleRange[]` | `AnimateCylinderTextureOscillateBlock` |

---

## Summary: What to copy to a new computer (master branch)

If you are building a world on a computer that only has the `master` branch, you need to:

1. **Manually patch `BackgroundChanger.cs`** (if using `TalkToNiDaq_LED_MovieProtocol`) — see the changes described above.
2. **Copy the relevant `.cs` scripts** from this repo into your Unity project's `Assets/Scripts/` folder:
   - The `AnimateCylinderTexture*.cs` or `AnimateLoomDisc.cs` that matches your stimulus type
   - The matching `TalkToNiDaq_LED_*.cs` script

You do **not** need to switch to the `led_protocol` branch — all these files can be copied individually.

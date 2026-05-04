# Janelia Unity Toolkit — LED Protocol Branch Workflow

**Branch:** `led_protocol`
**Last updated:** 2026-03-10

---

## Overview

This branch adds LED trigger functionality to the Janelia Unity Toolkit. Two new NiDaq scripts trigger an LED based on either cylinder rotation angle or which PNG is currently displayed in a movie protocol. Supporting changes expose internal state from existing scripts and fix a shader bug.

---

## New Files Created

### 1. `TalkToNiDaq_LED_CylinderRotation.cs`
**Purpose:** Triggers LED (ao2) based on the cylinder's current rotation angle.

**How it works:**
- Auto-finds `AnimateCylinderTexture` or `AnimateCylinderTextureBottomLimit` in the scene
- Reads `AzimuthDeg` (0–360) each frame
- Turns LED ON when azimuth falls within any configured angle range
- Supports wrap-around ranges (e.g., 330° to 30° wraps through 0°)

**Inspector fields:**
- `cylinderTexture` / `cylinderTextureBottomLimit` — auto-found if empty
- `ledOnAngleRanges[]` — array of `{fromDeg, toDeg}` ranges (0–360)
- `showEachWrite` / `showEachRead` — debug logging

**NiDaq channels:**
- ai0–ai2: 3 input channels (photodiode, frame trigger, LED readback)
- ao0: toggling photodiode signal
- ao1: rotation Y modulation
- ao2: LED ON/OFF based on angle

---

### 2. `TalkToNiDaq_LED_MovieProtocol.cs`
**Purpose:** Triggers LED (ao2) based on which PNG filename is currently displayed by BackgroundChanger.

**How it works:**
- Reads `BackgroundChanger.CurrentTextureName` each frame
- Matches against configured filenames or numeric ranges
- Turns LED ON when a matching texture is displayed

**Inspector fields:**
- `ledOnTextureNames[]` — specific filenames to match (e.g., `1.png`, `special.png`). Include the `.png` extension.
- `ledOnTextureRanges[]` — numeric ranges `{fromNumber, toNumber}` (e.g., 500 to 600 matches `500.png` through `600.png`)
- `showEachWrite` / `showEachRead` — debug logging

**NiDaq channels:** Same as cylinder rotation script (ai0–ai2, ao0–ao2)

---

### 3. `AnimateCylinderTextureBottomLimit.cs`
**Purpose:** Copy of `AnimateCylinderTexture` with an additional bottom elevation limit.

**Difference from original:**
- Adds `maxEl` field (default 1.0) — elevation stops at this value instead of always going to 1.0
- Elevation step size = `(maxEl - offsetEl) / numElevationSteps`

**Inspector fields (same as AnimateCylinderTexture, plus):**
- `maxEl` — maximum elevation limit (0.0–1.0, default 1.0)

**Example:** `offsetEl = 0.1`, `maxEl = 0.5`, `numElevationSteps = 10` → steps from 0.1 to 0.5 in increments of 0.04

---

### 4. `AnimateCylinderTextureOscillate.cs`
**Purpose:** Oscillating spot stimulus — sweeps texture back and forth between two azimuth positions at a fixed elevation.

**How it works:**
- Iterates through an array of speeds (`vRotDeg_per_sec[]`), each with a corresponding number of sweeps (`sweepRepeatVec[]`)
- Uses `Mathf.PingPong` for smooth back-and-forth oscillation between `oscillateFromDeg` and `oscillateToDeg`
- Optionally delays before each speed block (`delaySeconds`)
- Exposes `AzimuthDeg` for LED trigger scripts

**Inspector fields:**
- `vRotDeg_per_sec[]` — array of speeds in deg/s (runs in sequence)
- `sweepRepeatVec[]` — number of back-and-forth sweeps per speed
- `oscillateFromDeg` / `oscillateToDeg` — azimuth range (default 120°–240°)
- `elevation` — fixed y position (default 0)
- `delaySeconds` — delay before each speed block (default 0)
- `showDebugLog` — diagnostic logging

---

### 5. `TalkToNiDaq_LED_Oscillate.cs`
**Purpose:** LED trigger for `AnimateCylinderTextureOscillate`. Same pattern as other LED triggers.

---

### 6. `AnimateCylinderTextureOscillateBlock.cs`
**Purpose:** Oscillating spot with inter-block interval — parks the spot at a configurable position before each speed block starts.

**How it works:**
- Two-phase state machine: BLOCK → OSCILLATE → BLOCK → OSCILLATE → ...
- **BLOCK phase:** Holds spot at `blockAzimuthDeg` (e.g., 180° = behind the fly) for `blockDurationSeconds`
- **OSCILLATION phase:** PingPong oscillation at the current speed for the configured number of sweeps
- After each speed completes, returns to BLOCK phase before the next speed

**Inspector fields (same as Oscillate, plus):**
- `blockDurationSeconds` — hold duration at block position (default 5s)
- `blockAzimuthDeg` — where to park the spot during the block (default 180°)

---

### 7. `TalkToNiDaq_LED_OscillateBlock.cs`
**Purpose:** LED trigger for `AnimateCylinderTextureOscillateBlock`. Same pattern as other LED triggers.

---

### 8. `AnimateCylinderTextureLoom.cs`
**Purpose:** Looming spot stimulus — spot expands/contracts following the standard l/|v| temporal profile, with per-position multi-rate support.

**How it works:**
- Uses texture tiling (scale) to zoom in/out on the spot PNG, making it appear to approach/recede
- Looming dynamics follow: `scale(t) = tc / (tc - t)` where `tc = (l/v) × maxScaleFactor`
- Protocol uses nested config: each position has an azimuth and an array of loom rates (l, v, sweeps)
- State machine: BLOCK (LED ON) → [Rate 1 × N sweeps → Rate 2 × M sweeps → ...] → BLOCK → next position

**Inspector fields:**
- `positions[]` — array of `PositionConfig`, each containing:
  - `azimuthDeg` — azimuth position (0–360°)
  - `loomRates[]` — array of `LoomRateConfig`, each containing:
    - `l` — half-size of the virtual approaching object
    - `v` — approach velocity
    - `numSweeps` — number of loom cycles at this rate
    - `maxScaleFactor` — maximum expansion ratio (e.g., 10 = spot appears 10× larger at peak)
    - `includeRegressive` — if true, loom includes both expand and contract phases
    - `interLoomPauseSec` — pause between individual looms at base size
    - `peakPauseSec` — optional pause at peak expansion before regressive phase
- `elevation` — fixed y position
- `blockDurationSeconds` — hold at block position before each position (seconds)
- `blockAzimuthDeg` — where to park spot during blocks (default 180°)

**Loom duration (derived):** `(l/v) × (maxScaleFactor − 1)` seconds per expand or contract phase.

**Public API:**
- `AzimuthDeg` — current spot azimuth
- `CurrentScaleFactor` — current expansion ratio (1.0 = base size)
- `IsInBlock` — true during block phase (LED should be ON)
- `IsLooming` — true when actively expanding/contracting

**Example protocol:**
```
positions[0]: azimuth=240°
  loomRates[0]: l=10, v=200, sweeps=10   (l/v = 0.05s)
  loomRates[1]: l=10, v=100, sweeps=5    (l/v = 0.10s)
positions[1]: azimuth=180°
  loomRates[0]: l=10, v=200, sweeps=10
positions[2]: azimuth=120°
  loomRates[0]: l=10, v=200, sweeps=10
```
Flow: BLOCK → 240° (rate1 ×10, rate2 ×5) → BLOCK → 180° (rate1 ×10) → BLOCK → 120° (rate1 ×10) → FINISHED

---

### 9. `TalkToNiDaq_LED_Loom.cs`
**Purpose:** LED trigger for `AnimateCylinderTextureLoom`.

**LED logic:** LED ON during block phases (spot at back), LED OFF during looming. No angle ranges needed — reads `IsInBlock` directly from the loom script.

---

### 10. `AnimateLoomDisc.cs`
**Purpose:** 3D disc looming stimulus — replaces the texture-tiling approach to fix UV seam and distortion issues.

**How it works:**
- Creates a flat circular disc mesh at runtime (64-segment circle, double-sided)
- Positions the disc inside the cylinder at the desired azimuth/elevation relative to the fly (camera)
- Moves the disc closer (progressive) / farther (regressive) to change apparent angular size
- Angular size follows naturally from 3D perspective — always a perfect circle, no UV seam dependency
- Same l/v loom dynamics and state machine as `AnimateCylinderTextureLoom`

**Why this replaces texture tiling:**
- Texture-tiling loom (AnimateCylinderTextureLoom) had two problems:
  1. UV seam — the spot was "cut" by a vertical line at the cylinder mesh seam
  2. Distortion — the spot lost its circular shape at large sizes on the curved surface
- The 3D disc avoids both: it's a real object in space, not a texture on a curved surface

**Inspector fields:**
- `positions[]` — same nested `PositionConfig` / `LoomRateConfig[]` as before
- `baseSpotAngularRadiusDeg` — angular radius at scaleFactor=1 (degrees). Peak = base × maxScaleFactor
- `spotColor` — disc color (default black)
- `elevationDeg` — elevation angle (0 = horizontal)
- `azimuthOffsetDeg` — calibration offset for azimuth-to-world mapping
- `blockDurationSeconds`, `blockAzimuthDeg` — block settings (same as before)
- `cylinderRadius` — for distance clamping (default 1.0)

**Public API (same as AnimateCylinderTextureLoom):**
- `AzimuthDeg`, `CurrentScaleFactor`, `IsInBlock`, `IsLooming`

**Note:** Use a plain background PNG on the cylinder (e.g., all white) since the spot is now a separate 3D disc object.

---

### 11. `TalkToNiDaq_LED_LoomDisc.cs`
**Purpose:** LED trigger for `AnimateLoomDisc`. Same logic as `TalkToNiDaq_LED_Loom.cs` — LED ON during blocks, OFF during looming.

---

## Modified Files

### 10. `AnimateCylinderTexture.cs`
**What changed:**
- Added `AzimuthDeg` public property (read-only, 0–360°) so LED trigger scripts can read current cylinder rotation
- Added `showDebugLog` checkbox for diagnostic logging (first 5 frames show material status, array config, rotation values)
- Added array bounds guard to prevent `IndexOutOfRangeException` when all velocities complete

**AzimuthDeg computation:**
```csharp
_azimuthDeg = ((x % 1f) + 1f) % 1f * 360f;
```
Where `x` is the texture offset in UV space (0–1 maps to 0–360°).

---

### 11. `org.janelia.background/Runtime/BackgroundChanger.cs`
**What changed:**
- Added `CurrentTextureName` public static property exposing the filename of the currently displayed texture
- Set in `UseCurrentTexture()` to the current PNG filename
- Cleared in `UseSeparatorTexture()` to empty string

---

### 12. `org.janelia.background/Assets/Resources/TextureMix.shader`
**What changed:**
- Fixed fragment shader to read `_MainTex_ST.zw` (offset) instead of `_MainTex_ST.xy` (tiling)
- Without this fix, `SetTextureOffset()` calls had no visual effect — the shader was reading the wrong components
- Added tiling multiplication (`i.uv * _MainTex_ST.xy`) for looming stimulus support — backward-compatible since default tiling is (1,1)

**Before:** `tex2D(_MainTex, i.uv + _MainTex_ST.xy)` — reads tiling, ignores offset
**Current:** `tex2D(_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw)` — applies both tiling and offset

---

## Bug Fixes

| Bug | Cause | Fix | File |
|-----|-------|-----|------|
| `IndexOutOfRangeException` in Update() | `vel` increments past array bounds after all velocities complete | Added early return guard at top of Update() | `AnimateCylinderTexture.cs` |
| "Task specified is invalid" on quit | NiDaq task already cleaned up when OnDestroy runs | Wrapped OnDestroy in try-catch | Both TalkToNiDaq scripts |
| Cylinder texture not visually rotating | TextureMix shader read `.xy` (tiling) instead of `.zw` (offset) | Fixed shader to use `.zw` | `TextureMix.shader` |
| LED trigger not finding BottomLimit script | Only searched for `AnimateCylinderTexture` type | Added search for both types | `TalkToNiDaq_LED_CylinderRotation.cs` |

---

## Elevation Behavior Notes

- `offsetEl` = starting elevation (top offset)
- `maxEl` = ending elevation (bottom limit, only in BottomLimit variant)
- `numElevationSteps` divides the range `(maxEl - offsetEl)` into equal steps
- Steps always evenly fill the range — no gaps, no overlaps
- If you computed N steps for the full range (0 to 1) and then add an offset, use fewer steps to maintain the same step size: `N × (maxEl - offsetEl)`

---

## Commit History

| Commit | Description |
|--------|-------------|
| `1b1a2ea` | Add LED trigger scripts based on cylinder rotation and movie protocol |
| `223e507` | Switch LED angle ranges to 0-360 convention with wrap-around support |
| `2b5fca5` | Replace AnimateCylinderTexture with correct original version |
| `7c48afa` | Fix OnDestroy crash when NiDaq task is already cleaned up |
| `e4ee581` | Fix array out of bounds when all velocities are completed |
| `a67b8f9` | Add debug logging to AnimateCylinderTexture for rotation diagnostics |
| `dbd7d9c` | Fix TextureMix shader reading tiling instead of offset |
| `fc3a2a2` | Add AnimateCylinderTextureBottomLimit with configurable max elevation |
| `db70ffe` | Support both cylinder texture scripts in LED trigger |
| `1b4b404` | Add oscillating spot stimulus with LED trigger |
| `464807a` | Support multiple speeds and sweep counts in oscillating texture |
| `256d843` | Add oscillating spot with inter-block interval variant |
| `15dba28` | Add LED trigger for oscillating block stimulus variant |
| `8b39f2d` | Add looming stimulus with position sequence and shader tiling support |
| `7605f8b` | Redesign looming protocol with per-position multi-rate support |
| `0dd70a9` | Move loom settings (maxScale, regressive, pauses) into per-rate config |
| `0f0b1a8` | Add 3D disc looming stimulus to replace texture-tiling approach |
| `061a95b` | Add cylinder rotation with first-sweep LED and pause per elevation |

---

## Changelog

_Update this section each time new changes are made._

### 2026-03-10
- Created `TalkToNiDaq_LED_CylinderRotation.cs` — LED trigger based on cylinder rotation angle
- Created `TalkToNiDaq_LED_MovieProtocol.cs` — LED trigger based on displayed PNG filename
- Created `AnimateCylinderTextureBottomLimit.cs` — cylinder animation with configurable max elevation
- Added `AzimuthDeg` property to `AnimateCylinderTexture.cs`
- Added `CurrentTextureName` property to `BackgroundChanger.cs`
- Fixed TextureMix shader offset bug (`.xy` → `.zw`)
- Fixed OnDestroy NiDaq crash
- Fixed array out of bounds in AnimateCylinderTexture
- Added debug logging toggle to cylinder texture scripts
- Made LED trigger compatible with both cylinder texture script variants

### 2026-03-10 (continued)
- Created `AnimateCylinderTextureOscillate.cs` — oscillating spot stimulus, sweeps back and forth between two azimuth positions at fixed elevation
- Created `TalkToNiDaq_LED_Oscillate.cs` — standalone LED trigger for oscillating stimulus
- Created `AnimateCylinderTextureOscillateBlock.cs` — oscillating spot with inter-block interval (parks spot at configurable azimuth for N seconds before each speed block)
- Created `TalkToNiDaq_LED_OscillateBlock.cs` — standalone LED trigger for the oscillate-block variant
- Created `AnimateCylinderTextureLoom.cs` — looming stimulus with l/|v| temporal profile, sequences through multiple azimuth positions
- Created `TalkToNiDaq_LED_Loom.cs` — LED trigger for looming stimulus (with optional loom-phase-only filtering)
- Updated TextureMix.shader to apply tiling multiplication for zoom support (backward-compatible)
- Redesigned `AnimateCylinderTextureLoom.cs` — per-position multi-rate config (nested PositionConfig with LoomRateConfig arrays)
- Redesigned `TalkToNiDaq_LED_Loom.cs` — simplified LED logic: ON during block, OFF during looming
- Moved loom settings (`maxScaleFactor`, `includeRegressive`, `interLoomPauseSec`, `peakPauseSec`) from global into `LoomRateConfig` — each rate now has independent settings
- Created `AnimateLoomDisc.cs` — 3D disc looming stimulus (fixes UV seam and distortion from texture-tiling approach)
- Created `TalkToNiDaq_LED_LoomDisc.cs` — LED trigger for disc loom stimulus
- Created `AnimateCylinderTextureElevationPause.cs` — cylinder rotation with pause on first sweep per elevation (LED + 10s pause at configurable azimuth, remaining sweeps normal)
- Created `TalkToNiDaq_LED_ElevationPause.cs` — LED trigger that fires only on first sweep at each elevation, OFF during pause and subsequent sweeps

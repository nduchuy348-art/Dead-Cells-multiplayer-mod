# Native HashLink Bytecode & Master Functions Reference for DCCM

## 1. `dc.en.Hero.landStun(sec, stun)` & `dc.en.Hero.onLand(dy)`
- `Hero.landStun(sec: Float, stun: Bool)`: Function index `f@2037` (`deadcells.exe`) / `f@2039` (`hlboot.dat`).
- `Hero.onLand(dy: Float)`: Function index `f@2042` (`deadcells.exe`) / `f@2044` (`hlboot.dat`).
- **Crouch Pose Mechanism**: `Hero.landStun(sec, true)` sets Affect 8 (`Stun`), clears `accelFactor = 0.0`, and calls `spr.anim.stopWithStateAnims()`. This evaluates `hasAffect(8)` and switches to the crouched/stunned recovery pose.
- **Remote Peer Clones**: Do NOT call `onLand` or `landStun` directly on remote clones (it locks controls locally and breaks position interpolation). Instead, simulate the native visuals and audio:
  - `dy >= 0.3`: `_level.lAudio.playEventAt(hxd.Res.load("sfx/hero/land.wav").toSound(), px, py, 0.25, null, null)` + `playStepSound()`.
  - `0.4 <= dy <= 0.6`: `_level.fx.landSmoke(px, py, 20)`.
  - `0.6 < dy <= 1.3`: `_level.fx.landSmoke(px, py, 20)`.
  - `1.3 < dy <= 2.0`: `_level.fx.landSmoke(px, py, 48)`.
  - `2.0 < dy <= 3.0`: `_level.fx.landSmoke(px, py, 48)` + `bool top = false; _level.fx.landDirt(px, py, ref top)` + `_level.viewport.shakeS(0.2, 0.5, 0.5)`.
  - `dy > 3.0`: `_level.viewport.shakeS(0.2, 0.5, 1.3)` + `_level.fx.landHeavy(px, py, null, null)`.

---

## 2. `dc.tool.mainSkills.DiveAttack.onOwnerLand(height)`
- Function index: `f@10573` (`deadcells.exe`) / `f@10570` (`hlboot.dat`).
- Sets Affect 63 (guard suppressing `Hero.onLand`). Note: `landSmoke` is NEVER called by DiveAttack.
- Audio: `high ? "sfx/hero/stomp_char2.wav" : "sfx/hero/stomp_char1.wav"` played via `playEventAt(snd, px, py, 1.0, null, null)`.
- Ram Rune: `hero.itemMeta.hasMeta("BreakableGroundKey")` doubles radius `r`.
- Affixes: `DiveAttackFire` (color = 16754176), `DiveAttackOil`.
- FX calls:
  - If high: `lvl.fx.customMask(color, 0.15, 0.04, 0.1, 0.15, null)`.
  - `lvl.fx.landHeavy(px, py, null, null)`.
  - `dc._FlashLight.create.Invoke(lvl, px, py - hero.hei * 0.5, color, 24.0 * r, 1.5, 1.5, null)`.
  - `lvl.viewport.shakeS(0.5, 1.0, 0.3)`.
  - Loop `ox` in `[-r .. r]`: if solid floor, `lvl.fx.diveAttack(waveX, py, color, ref powerScale)`.

---

## 3. Footstep & Locomotion Audio
- In `Hero.playStepSound`:
  - Wood/Trees: `WoodCollision` or biome in `["Cliff", "Greenhouse"]` -> `hero.footStepWoodSounds`.
  - Pipe/Sewers: `PipeCollision` or biome in `["Sewer", "SewerOld"]` -> `hero.footStepPipeSounds`.
  - Water: `inWaterTimer > 0` or `WaterCollision` -> `hero.footStepWaterSounds`.
  - Wet ground: `map.isWetGround(cx, cy)` -> switches to `footStepWetStoneSounds`, `footStepWetWoodSounds`, `footStepWetPipeSounds`.
  - Default: `hero.footStepStoneSounds`.
- Climbing audio in `Hero.playClimbSfx`:
  - Automatically selects `chainClimbSounds`, `vineClimbSounds`, `pipeClimbSounds`, `woodClimbSounds`, `ropeLadderClimbSounds`.
- Remote players: Must spatialize using `_level.lAudio.playEventAt(snd, px, py, volume, pitch, null)` or `playEventOn(...)`.

---

## 4. Weapon Slash Trails in `Weapon.dynOnFxFrame`
- Calls `playReleaseSfx` (spatialized for remote peers).
- Spawns slash sprite via `_level.fx.playWeaponAnim(owner, cinf, castSpeed, customId, innerColor, outerColor)`.
- Attached as child of `owner.spr`, inherits facing scale (-1 / +1) and animation speed.

# Missing Fixes

Known problems that could not be solved yet. Entries are added only when the user asks for it.
Newest entries go at the top. Everything in this file is written in English.

## Entry template

```
- [DD Month YYYY]
  - Live: [live number, or "General"]
  - Frame: [frame number or interval, or "General"]
  - Description: [what is wrong, compared with the game]
  - Tried:
    - [very short item]
    - ...
    - Details: Logs/[session log file name].md
```

## Entries

- 10 November 2026
  - Live: 1156 (checked there; likely General)
  - Frame: General
  - Description: Some stage objects are on layer 0 in the viewer but on layer 12 in the game: `*_building_r` (blinklight_building), `Sunshafts`, `LensFlare`, `gate_decoration`, `horseshoes_000`. No visible effect found so far (CameraTimeline1 culling mask includes both layers).
  - Tried:
    - Ported `StageController.UpdateObjectLayer` (cutt object key LayerType): live 1156 has no key with a layer type, so it changes nothing there.
    - Checked the JP `Gallop.Live.*` classes that call SetLayer: none touches these objects. Source (prefab data or another call) not found.
    - Details: Logs/261109-Pending items review.md (iterations 18-19)

- 10 November 2026
  - Live: 1176
  - Frame: General (the effect is active almost the whole live; strongest at 2947-3168, 1765-1892 and 2313)
  - Description: Edge style has no visible effect in the viewer. The post effect list entry exists and the track data (`edgeStyleKeys`, 123 keys) is bound, but nothing draws it. In the game the keys are copied by `Director.OnUpdateEdgeStyle` into the camera's RainSplash parameter and drawn by `RainSplashPass` (mist and splash around the characters).
  - Tried:
    - Read the JP code: `RainSplashPass.Execute` (about 700 decompiled lines: character mask drawn with `CommandBuffer.DrawRenderer`, several downscaled RTs, 4+ blits, material properties 0x1f8-0x20d) plus `LiveImageEffect.SetupEdgeStyleController/SetupEdgeStyleRendererList` (per-character renderer lists).
    - Not ported: needs the RainSplash parameter, the visible-renderer plumbing and the pass with its RT formats and constants.
    - Details: Logs/PENDING_PARALLEL_SEGMENTS.md (update of 2026-11-10, EdgeStyle)

- 09 November 2026
  - Live: 1151
  - Frame: around 5259-6036 (confirmed by the user in this range; other frames are also affected)
  - Description: The live looks very wrong in the viewer: many camera cuts, lighting and effects fail. The user has no access to this live in the original game, so there is no game reference to compare against.
  - Tried:
    - Only the `preColorCorrectionDataLists` track was investigated (JP Ghidra + cutt data): it is enabled f5259-6036 (saturation 0.29, 4-point curves) while the normal ColorCorrection is disabled in the same window; the viewer has no handler for the track. Not ported: it is unclear whether the game shows it, since the Pre parameter seems to reach the screen only through the exclusion camera, inactive here (character list empty).
    - Still open: camera cuts, lighting and the rest of the effects were not analysed. A game capture is not possible, so compare with the data of the cutt only (tracks with data, camera keys, lights).
    - Details: Logs/PENDING_PARALLEL_SEGMENTS.md (S1, PreColorCorrection)

- 09 November 2026
  - Live: General (first seen on live 1034)
  - Frame: General (first seen around frame 3642 of live 1034)
  - Description: Verify lyrics call. The lyric caption shows more text at once than the game. A long sentence that the game (Global) shows in two consecutive segments is shown whole in the viewer. Example from live 1034 around frame 3642: the viewer shows "To balmy blue skies we'll jump! (Jump!)" while the game capture shows "Jump into blue skies! (Jump!)".
  - Tried:
    - Copied the lyrics from the Global (English) client and called them while using the JP asset.
    - The calls return far more text than needed (a sentence that Global splits in two parts now comes out in one piece).
    - Still open: verify how the lyrics call picks the text range of each segment.
    - Details: Logs/261109-Hachimi JP scene probe.md (iteration 17)

- 05 November 2026
  - Live: 1034
  - Frame: 739 (the whole mirror section of the cutt is affected: Environment keys from frame 183, rate 0.141)
  - Description: The stage floor reflection (`mirror000`, shader `Cygames/MirrorAndShadow/ReceiveMirror`) looks stronger and sharper than in the game. In the viewer the reflected stage props (frames, panels) show clear edges across the floor. In the game reference they are faint and soft.
  - Tried:
    - Cutt data: the active Environment key matches the game (mirror on, rate 0.141, BG and 3D layers, not toon).
    - Rate and `_Color` applied to the material exactly as the game's `UpdateMirrorParams`.
    - Mirror camera clear colour: same blue as the game's cameras; alpha 0 vs 1 gives a pixel-identical frame (the shader ignores alpha). Discarded.
    - Mirror texture size and format: game 512x289 vs viewer 512x281; 16-bit depth vs none is pixel-identical. Discarded.
    - DOF: same game shader, parameters and passes; at this cut (quality OnlyBackground) it only blurs the background in both. Discarded.
    - Still open: the reference is a compressed video frame (it may hide low-contrast detail), and the reconstructed `SimpleMirrorCaster` mirror renderer has not been checked. Next step: an uncompressed game screenshot of frame 739.
    - Pending (user): verify against an uncompressed game screenshot. Frame 739 was added to the Hachimi JP probe capture list (`probe_scene_capture.txt` in the probe output dir), so the game DLL takes the screenshot when live 1034 reaches that frame.
    - Details: Logs/261105-Live 1034 mirror reflection intensity.md

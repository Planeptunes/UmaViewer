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
    - Details: Logs/261105-Live 1034 mirror reflection intensity.md

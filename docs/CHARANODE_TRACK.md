# Live timeline track `CharaNode` (CySpring on/off per part and per bone)

Status: **implemented, NOT verified**. It compiles, but it has not been confirmed in Play Mode that
the track is read from the timeline data or that it changes anything visible. In 1091 no difference
was noticed (its keys only affect short skirt/ribbon sections). Treat it as an open question until
it is checked with the songs listed below.

## What it does in the game
`LiveTimelineControl.AlterUpdate_CharaNode`: for each enabled CharaNode track, the current key is
applied to every character whose bit is set in `PositionFlag` (up to 20 positions), for each of its
`LiveModelController`s:

- `SetEnableHeadCySpring(EnableHeadCySpring)` — whole head part (`CySpring.Enable/DisableAllCySpringBones`)
- `SetEnableEarCySpring(EnableEarCySpring)` — head roots with prefix `Sp_He_Ear`
- `SetEnableBodyCySpring(EnableBodyCySpring)` — whole body part
- `SetEnableTailCySpring(EnableTailCySpring)` — first tail root `Sp_Hi_Tail0_B_00`
- `SetEnableSkirtCySpring(EnableSkirtCySpring)` — skirt roots of the body part
- then, for each `i`: `CySpringController.SetEnableCySpringBone(TargetCySpringBornEnableList[i],
  TargetCySpringBornNameList[i], isPrefix: false)`.

All of them end in `CySpringRootBone.SetShouldReflectCalcResult` (root **and** children): the chain
is still simulated but its result is not applied, so the motion clip drives those bones.

Key layout (`LiveTimelineKeyCharaNodeData`): `PositionFlag 0x30, EnableHeadCySpring 0x34,
EnableEarCySpring 0x35, EnableBodyCySpring 0x36, EnableSkirtCySpring 0x37, EnableTailCySpring 0x38,
TargetCySpringBornNameList 0x40, TargetCySpringBornEnableList 0x48`. Timeline field:
`CharaNodeDataList` (camera worksheet). `LiveTimelineKeyDataType.CharaNode = 77`.

## Implementation in the viewer
- `LiveTimelineCharaNodeData.cs`: key / key list / group classes; `CharaNodeDataList` field in
  `LiveTimelineWorkSheet`.
- `LiveTimelineControl.AlterUpdate_CharaNode` (worksheet loop; honours the track's Disable
  attribute and play mode) → `UmaContainerCharacter.ApplyLiveCharaNode` →
  `CySpringController.ApplyCharaNode` (applied only when the key changes; re-armed in `EndLoad`
  after a physics reload).
- `CySpring.SetEnableCySpringBone` / `EnableAllCySpringBones` / `DisableAllCySpringBones` now switch
  the root and its children, as the game does (they used to switch only the root; this also fixes
  the UI physics toggle leaving child bones simulated).

## Open points
- The game finds the skirt roots through a static string array (assumed here to be
  `Sp_Hi_LSkirt` / `Sp_Hi_CSkirt` / `Sp_Hi_MSkirt`) plus a second, unidentified root name that is
  not ported. The songs checked list their skirt roots explicitly, so this should not matter there.
- Not verified that `CharaNodeDataList` deserializes from the timeline asset.

## Usage in the data (where to look)
`PositionFlag` is a mask: 1 = slot 0 (centre), 2 = slot 1, 4 = slot 2, 64 = slot 6…

- **1153 (most visible):** from frame 0, slots 0 and 1 have head, body, skirt and tail OFF for the
  whole song, plus a long list of roots (tail, bust, accessories, `Sp_Hi_MSkirt0_*`, sleeves,
  ribbons). Expected: their hair, tail, skirt and bust do not swing; toggling physics in the UI
  should not change them. Slot 2: ears OFF (`Sp_He_Ear0_R_00`) in frames 1748–2033.
- **1180:** slot 0: chest mantle/ribbons OFF in 1611–1665, `Sp_He_Hair4_C/L/R_00` OFF in
  2884–3445. Slot 1: `Sp_Hi_MSkirt0_*` OFF in 456–1368, 1611–1665, 1783–2799; also
  `Sp_He_Hair2_L_00` from 2464 and the tail from 2680 (until 2799).
- **1091:** slot 0: skirt and `Sp_Hi_Ribbon1_*` OFF in 1775–2347 and 2635–2900; slot 1: skirt and
  tail OFF from 2947 to 3226.
- **1092:** slot 6: tail OFF in 5259–5615. **1152:** slot 1: skirt OFF in 4292–4359 and 4535–4979.
- 1126 / 1150 / 1155 / 1157: only "all ON" keys (no expected difference).

## How to verify
1. Load 1153 and look at slots 0/1: no hair/tail/skirt swing during the whole song, unlike the
   other characters.
2. If nothing changes, check with `execute_code` that `data.worksheetList[0].CharaNodeDataList`
   holds 3 groups with keys and that the roots of slots 0/1 have `ShouldReflectCalcResult == false`.

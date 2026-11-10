---
name: create-model-from-fbx
description: "Use when importing or replacing a character model from an FBX in an s&box project: configure a .vmdl, materials, textures, animation clips, and an enemy prefab reference. Does not implement gameplay animation-state logic."
---

# Create a Model from FBX

Set up an existing FBX character for use as a model in an s&box game. This
skill covers the model asset, its materials and clips, and optionally swapping
the model in a prefab. It does not decide when gameplay plays animations or
implement animation-state logic; use a separate gameplay workflow for that.

## Workflow

1. **Orient to the project and editor.**
   - Follow the engine-provided `sbox-cli` skill and the project's s&box editor
     workflow.
   - Confirm the intended worktree, editor project, active scene, compile state,
     and play state before making changes.
   - Preserve user edits. Don't launch a duplicate editor or open Blender unless
     the user asks for it.

2. **Inspect the inputs.**
   - Find the FBX and related texture files in the asset system and on disk.
     Use the editor's asset information, file/dependency inspection, and
     thumbnail tools where available.
   - Determine the FBX's actual mesh/object names, materials, texture
     references, and animation takes. Do not assume names or take ordering from
     another FBX, even if it is replacing a similar character.
   - Check whether the provided texture is an atlas and whether the model's UVs
     and material reference match it. Reuse an existing material only when it is
     genuinely compatible; don't change a shared material to suit one model.
   - Record the existing model's scale, orientation, material setup, mesh
     filters, and clip names as references, not defaults.

3. **Create or update the `.vmdl`.**
   - Prefer the editor's ModelDoc workflow. If it is unavailable, use a
     compatible sibling `.vmdl` as a format reference and make a focused new
     asset; avoid changing the existing model just to create a variant.
   - Set the render mesh to the intended FBX and use mesh filters that include
     the desired character geometry while excluding unrelated geometry.
     Re-check object names in the new FBX rather than carrying over an old
     exception list.
   - Assign the verified material or material group. Use scale, rotation, and
     origin adjustments only as needed for this specific FBX.
   - Add only animation takes that actually exist in the FBX. Preserve their
     exact take names and source. Mark continuous locomotion/idle clips as
     looping only when appropriate; leave actions non-looping unless the asset
     requires otherwise.
   - Keep animation playback decisions out of this asset-setup step. A clip
     being present in a model does not make gameplay play it.
   - Ensure the model asset has a unique, valid asset GUID/metadata entry.

4. **Optionally swap a prefab to the new model.**
   - Read the prefab through the editor asset tools before editing it. Preserve
     its other components, collider setup, and tuning.
   - Change the model reference's canonical asset path **and** its asset GUID
     (from the new model's metadata). Updating only the path can leave the
     prefab resolving the old model.
   - Check that the chosen model's proportions and scale still make sense with
     the prefab's existing collider and gameplay scale. Adjust those only when
     needed and verify the result.
   - Prefer editor asset-writing tools over hand-editing prefab JSON. Do not
     rewrite an entire prefab from a stale copy.

5. **Compile and verify in s&box.**
   - Compile the model and inspect the editor console for model/FBX errors.
     Confirm the model is compiled and up to date, its dependencies resolve,
     and the expected material/texture is referenced.
   - Inspect the model thumbnail or preview for silhouette, texture, scale, and
     orientation. Check that the intended animation clips are available.
   - If the prefab was changed, compile it and verify its model reference path
     and GUID. Stop and restart play mode so spawned objects don't keep a cached
     copy of the old prefab; inspect a live renderer and capture a screenshot.
   - Finish with the editor open in the state requested by the user. Report what
     was verified and any untested animation behavior clearly.

## Common pitfalls

- An FBX may reference a texture using an authoring-machine path. Confirm the
  project texture is actually connected through a working s&box material.
- A correct-looking `.vmdl` text file is not proof of a successful import:
  compile it and inspect the preview.
- Model and prefab asset identity includes metadata, not just filenames. Keep
  GUIDs unique and update both the prefab's model path and GUID when replacing
  its model.
- Runtime enemies can retain the old prefab/model until play mode is restarted.
- Do not add code for movement, attacks, hit reactions, or animation selection
  here; those belong to the separate gameplay animation workflow.

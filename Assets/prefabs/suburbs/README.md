# Suburban blockout prefabs

Drag these prefabs from the Asset Browser into a scene. They use the imported
Kenney suburban models and the same materials and collision as the initial
neighborhood. The scene now uses linked lot prefabs with nested house, fence,
tree, and driveway instances. Roads and sidewalks use scaled surface
prefabs, lane markings use dash prefabs, and crossings use crosswalk prefabs.
Prefab edits update their instances; explicit per-instance overrides are retained.
The upper row rotates the same lot prefabs 180 degrees. Only driveway placement
and tree rotations are overridden to preserve the original mirrored layout;
houses and fences inherit their prefab transforms.
Grass is one continuous base plane across each scene, tinted muted grass green
(`#66854F`). Lots contain no separate lawn slabs.

| Prefab | Contents and pivot |
| --- | --- |
| `house_building-type-{a,b,d,g}` | One house with a static bounds collider. Origin is its ground-level center; front faces local -X. |
| `lot_building-type-{a,b,d,g}` | House, driveway, trees, and side/rear fences with an open front and a 180-unit rear opening. Origin is the house base; front faces local +Y. |
| `fence_segment` | One 142.5-unit segment along local Y, with a static collider. Origin is its ground-level center. Scale local Y to change its length. |
| `tree_small` | Decorative tree without collision. Origin is its base. |
| `crosswalk` | Seven stripes crossing a 280-unit road along local Y. Origin is the crossing center. |
| `lobby_front_yard` | Nested type-a lot, rotated so the house/front yard face local -X. Origin is the house base. Used as the lobby backdrop. |
| `road_surface`, `sidewalk_surface`, `road_dash`, `driveway_surface` | Centered 50-unit cube with the corresponding material/tint. Scale X/Y/Z to the desired dimensions divided by 50. These preserve the current scene's custom-sized slabs without adding tile seams. |

## Placement

- Houses, fences, and trees: place the pivot on the ground surface.
- Complete lots: place the pivot directly on the grass surface. Houses, fences,
  and trees have ground-level bases; driveway bottoms also meet that surface.
- In the game scene, the grass visual matches the existing collision surface at
  Z 2.5. The lobby ground surface is Z 0. Ground collision has not moved.
- Seat road and sidewalk slab bottoms on the grass surface. Place markings just
  above the road tops to avoid coplanar surfaces.
- Scale road surface instances for the desired length and width. Rotate
  90 degrees for north/south roads; arrange sidewalk and dash instances alongside.
- Road, sidewalk, and driveway slabs are visual only: provide a shared
  ground collider beneath them. House interiors are not accessible; their
  collision uses the full model bounds.
- The scene retains its original road dimensions using surface instances.
- All objects use `NetworkMode.Never`, like the static neighborhood. Do not
  network-spawn them as moving gameplay objects.

Source models are from Kenney City Kit Suburban (CC0); the original license is
preserved with the source asset pack.

# Character sprites

Source: `source/rotations.aseprite`.

Runtime contract:
- source canvas: 64x32 px;
- `character` layer contains the body;
- `ak` layer contains the weapon/arms visual;
- all 6 source frames form the right-facing run cycle;
- `character.png` is one 6-frame 384x32 body-only sheet used for every direction;
- the first frame (index 0) is also used as the static idle pose when the player is not moving;
- `weapon_ak.png` is one unchanged 64x32 weapon visual extracted from the first source frame;
- Godot renders the textures with Nearest filtering at integer scale.

Presentation behavior:
- movement in every direction uses the same run sheet;
- the body switches side immediately when the cursor crosses the character on the X axis; the left side mirrors the same animation;
- the weapon rotates only through `AimPivot`; on the left side it is mirrored to preserve the authored right-handed orientation;
- the common Aseprite canvas center is used as the body/weapon rotation origin;
- rotating the weapon up places it on the observer-right side of the body;
- rotating the weapon down places it on the observer-left side of the body;
- while the cursor is left of the character, the weapon is rendered one Z level behind the body;
- while the cursor is on or right of the character, the weapon is rendered one Z level in front of the body.

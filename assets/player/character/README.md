# Character sprites

Imported from `character(1).zip`.

Runtime contract:
- frame canvas: 64x64 px;
- idle: 4 directions, 9 frames each;
- run: 4 directions, 6 frames each;
- aim: 4 directions (`down`, `left`, `right` = 1 frame, `up` = 2 frames);
- original `.aseprite` files are preserved in `source/`;
- PNG sheets are exported 1:1 from the Aseprite RGBA canvas;
- no scaling, cropping, centering, recoloring, interpolation or redrawing is applied to source pixels;
- Godot renders the sheets with Nearest filtering and integer scale.

Presentation behavior:
- the normal idle/run animation follows the mouse-facing direction;
- holding RMB (`aim`) keeps the directional aim sprite active, so the rifle remains raised; a short aim pose is also shown after a weapon shot attempt.

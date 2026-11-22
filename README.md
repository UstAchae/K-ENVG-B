# K-ENVG-B

Justachae's ENVG-B (AN/PSQ-42) for SPT 4.1.6: a helmet night vision item with thermal outline and thermal fusion (white hot / black hot), switched with the arrow keys.

- `item/`: model pipeline and Unity bundle build
- `server/`: SPT server mod (item template, trader)
- `client/`: BepInEx plugin (thermal outline and fusion)

Works on its own. With K-3PS-C 1.9.0 beta 1 or later installed and its master switch on, the device is imaged through its eyepieces like the other night vision goggles, and the thermal picture is drawn into the right eyepiece, sized and centred on what the eye sees of it.

Thermal picture:
- Drawn the real way: a second camera at the goggle's thermal objective renders the scene at the sensor's resolution (16 px per degree), turned as the eye's view, so near things show where the objective sees them and a scope's magnification never enlarges the thermal picture. F12 advanced: 热像从物镜位置拍 (on).
- Sights cut a hole in it where the line from the objective goes on out through a red dot's, holographic sight's or scope's glass; the sight's own hood keeps its heat.
- The gun and the hands are drawn from the present frame only (no trail when the gun sways).
- The symbology (grid, range, heading, time, battery, mode) and the I2 gain bar are drawn in the thermal rectangle.

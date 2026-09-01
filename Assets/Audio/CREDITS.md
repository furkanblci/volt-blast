# Audio provenance

## Sound effects — Kenney, CC0

All sound effects come from [Kenney](https://kenney.nl/assets/category:Audio) and are
released under **CC0 1.0** (public domain dedication). No attribution is required, for
commercial use or otherwise; this file records where they came from so nobody has to work
it out again.

| Pack | Source |
|---|---|
| UI Audio | https://kenney.nl/assets/ui-audio |
| Interface Sounds | https://kenney.nl/assets/interface-sounds |
| Digital Audio | https://kenney.nl/assets/digital-audio |
| Impact Sounds | https://kenney.nl/assets/impact-sounds |

The full packs are kept in the repository rather than only the six clips in use, so
swapping a sound is a change in `Assets/Resources/SoundBank.asset` and nothing else. They
cost nothing in a build: assets outside `Resources` that nothing references are not
included.

### What is wired up

| Slot | Clip | Why this one |
|---|---|---|
| Pickup | `select_001` | 44 ms. Fires on every grab, so it has to be over before it registers as a sound. |
| Place | `impactSoft_medium_000` | 119 ms, energy at 222 Hz. The most-heard cue in the game, so it is low and short rather than bright. |
| Rejected | `error_004` | 103 ms. The brighter error clips read as scolding. |
| Clear | `powerUp7` | 522 ms, rising. Short enough to finish before the next placement. Pitched up by line count and combo, so one clip covers every tier. |
| GameOver | `lowDown` | 784 ms, energy at 188 Hz. Descending and deep — the run closing, not a failure buzzer. |
| Button | `click1` | 94 ms. |

### Levels

`SoundBank` carries a gain per clip, not one master volume. Measured over their loudest
50 ms the sources ranged from 0.11 (`click1`) to 0.49 (`lowDown`) — more than four to one —
so a single volume would leave half the set inaudible and the other half shouting.

The gains level the set first and then weight it on purpose: a line clear is the loudest
thing the game says, and the pick-up tick is the quietest, because it fires on every single
grab and must never nag.

## Music — still a placeholder

`MusicAmbient.wav` is generated (see `gen_audio.py` in the working notes) and is a
stand-in. Kenney publishes no music, so this slot needs a separate source. Public-domain
options: [freepd.com](https://freepd.com). [Incompetech](https://incompetech.com) is good
but CC-BY, which means the credit is mandatory.

Whatever replaces it **must loop seamlessly** — a click at the loop point is the most
noticeable fault in the mix, because it repeats forever.

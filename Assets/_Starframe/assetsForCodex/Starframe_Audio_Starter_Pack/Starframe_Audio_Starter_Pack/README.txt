STARFRAME — AUDIO STARTER PACK
Original synthesized WAV assets, prepared as a handoff pack for the Unity project.

CONTENTS
Music/
  01_Editor_Workbench_Loop.wav — restrained ambient editor/workbench loop.
  02_Flight_Combat_Loop.wav — more urgent variation for the flight range.
SFX/Construction/ — core power-up, metal and soft part placement, invalid placement, rotation, removal.
SFX/UI/ — selection, blueprint save, warning/confirmation.
SFX/Flight/ — thruster loop, maneuver jet, high-speed hull whine, boundary warning.
SFX/Combat/ — laser shot, metal impact, lock acquired, target lost.
SFX/Damage/ — core destruction, respawn reboot, critical damage pulse.

FORMAT
All files are mono PCM WAV at 44.1 kHz, 16-bit. The music loops are 16 seconds. Thruster_Loop.wav is intended to loop while playing; adjust its level and filter/pitch with thrust in the game. The sounds are intentionally short and restrained so gameplay remains clear.

UNITY IMPORT
Copy the Music and SFX folders into Assets/_Starframe/Audio in the project. Unity will import the WAV files. For the music loops and thruster loop, enable Loop in the imported AudioClip settings or loop the AudioSource in code. Set up mixer routing and in-game volume controls as desired. These are original procedural starter sounds; a sound designer can replace or layer them later.

DESIGN NOTES
The sound palette uses soft metallic impacts, restrained electronic tones, and a quiet void ambience. Avoid Earth-like wind; the flight whine represents hull and cockpit vibration. Keep rapid laser shots short and their impacts distinct from the core destruction cue.

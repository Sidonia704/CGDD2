"""Generate the four clean, mono sine/chirp cues used by AudioThreatInstall."""
import math
import os
import struct
import wave


RATE = 48000
OUT = os.path.join(os.path.dirname(__file__), "AudioSource")


def tone(duration, start_hz, end_hz=None, amplitude=.2):
    end_hz = start_hz if end_hz is None else end_hz
    count = round(duration * RATE)
    phase = 0.0
    result = []
    for i in range(count):
        t = i / RATE
        u = i / max(1, count - 1)
        freq = start_hz + (end_hz - start_hz) * u
        phase += 2 * math.pi * freq / RATE
        envelope = math.sin(math.pi * u) ** .7
        result.append(amplitude * envelope * math.sin(phase))
    return result


def silence(duration):
    return [0.0] * round(duration * RATE)


def write(name, samples):
    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(b"".join(struct.pack("<h", round(max(-1, min(1, s)) * 32767)) for s in samples))
    print(f"{name}: {len(samples) / RATE:.3f}s, mono PCM16")


os.makedirs(OUT, exist_ok=True)

# Two short ascending pips: subtle and easy to distinguish from the acquired-lock cue.
write("Enemy_Detected.wav", tone(.075, 520, 650, .24) + silence(.035) + tone(.09, 690, 890, .24))

# A single clear rising sweep for a confirmed enemy lock, with a rounded envelope.
write("Enemy_Lock_Warning.wav", tone(.23, 390, 760, .25))

# Repeating missile warning tone: two quick, separated descending pips.
write("Missile_Incoming.wav", tone(.085, 820, 640, .24) + silence(.045) + tone(.085, 820, 640, .24))

# Short descending tick for the successful countermeasure burst.
write("Flare_Deploy.wav", tone(.12, 1180, 480, .2))

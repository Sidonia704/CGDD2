"""Prepare one deterministic, dry incoming-missile pip without changing the installed asset.

Run InstallAudio.cs in the stopped Unity Editor to install it through native import APIs.
"""
import hashlib
import json
import math
from pathlib import Path
import struct
import wave

RATE = 48000
SECONDS = 0.060
FREQUENCY_HZ = 2000
FADE_SECONDS = 0.0025
AMPLITUDE = 0.65


def main():
    folder = Path(__file__).resolve().parent
    output = folder / "AudioSource" / "Missile_Incoming.wav"
    output.parent.mkdir(parents=True, exist_ok=True)
    count = round(RATE * SECONDS)
    fade = round(RATE * FADE_SECONDS)
    samples = []
    for index in range(count):
        # Cosine edges remove the start/stop click while retaining a flat, crisp pip body.
        edge = min(index, count - 1 - index)
        envelope = 0.5 - 0.5 * math.cos(math.pi * min(1, edge / fade))
        value = AMPLITUDE * envelope * math.sin(2 * math.pi * FREQUENCY_HZ * index / RATE)
        samples.append(round(value * 32767))
    assert samples[0] == samples[-1] == 0
    assert max(abs(value) for value in samples) < 32767
    with wave.open(str(output), "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(struct.pack("<" + "h" * count, *samples))
    summary = {
        "preparedClip": str(output.relative_to(folder)),
        "sampleRate": RATE,
        "channels": 1,
        "bits": 16,
        "durationSeconds": count / RATE,
        "frequencyHz": FREQUENCY_HZ,
        "edgeFadeSeconds": FADE_SECONDS,
        "peakLinear": max(abs(value) for value in samples) / 32767,
        "rmsLinear": math.sqrt(sum((value / 32767) ** 2 for value in samples) / count),
        "firstSample": samples[0],
        "lastSample": samples[-1],
        "clippedSamples": sum(abs(value) >= 32767 for value in samples),
        "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
        "content": "One sine pip. No noise, chirp, delay, reverb or tail; runtime controls repetition."
    }
    evidence = folder.parents[1] / "Documentation" / "FinalBalanceRound1-2026-11-03" / "incoming-beep-waveform.json"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, separators=(",", ":")))


if __name__ == "__main__":
    main()

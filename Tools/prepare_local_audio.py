"""Prepare the user-provided Factory Wars sound clips for the local Unity build."""

import argparse
import array
import math
from pathlib import Path
import subprocess
import wave


RATE = 24000
# Source, start and length in seconds, loop overlap in seconds.
CLIPS = {
    "RifleFire": ("머신건.mp3", 0.02, 1.27, 0),
    "SniperFire": ("저격총소리.mp3", 0.02, 1.58, 0),
    "RocketFire": ("로켓포 발사.mp3", 0.00, 1.45, 0),
    "MissileWarning": ("미사일경고음.mp3", 1.25, 2.50, 0),
    "JetFlyby": ("제트기.mp3", 0.23, 3.30, 0),
    "GroundEngine": ("탱크 움직임.mp3", 7.00, 5.20, 0.32),
    "RotorEngine": ("헬리콥터.mp3", 6.50, 4.80, 0.38),
    "AirEngine": ("항공기.mp3", 21.00, 5.40, 0.42),
    "CombatAmbience": ("freesound_community-battlefield-ambience-38564.mp3", 0.30, 4.05, 0.65),
}


def prepare(ffmpeg, source, start, duration, overlap):
    samples = array.array("f")
    samples.frombytes(subprocess.check_output([
        ffmpeg, "-v", "error", "-ss", str(start), "-i", str(source), "-t", str(duration),
        "-vn", "-af", "highpass=f=38", "-ac", "1", "-ar", str(RATE), "-f", "f32le", "pipe:1",
    ]))
    if len(samples) < RATE * duration * 0.95:
        raise ValueError(f"Incomplete sound: {source.name}")
    if overlap:
        count = round(RATE * overlap)
        # Blend the tail into the head. Start the exported loop after the shared interval.
        for i in range(count):
            mix = 0.5 - 0.5 * math.cos(math.pi * i / (count - 1))
            samples[-count + i] = samples[-count + i] * (1 - mix) + samples[i] * mix
        samples = samples[count:]
    else:
        for i in range(round(RATE * 0.008)):
            samples[i] *= i / (RATE * 0.008)
        tail = round(RATE * 0.18)
        for i in range(tail):
            samples[-tail + i] *= 1 - i / (tail - 1)
    peak = max(map(abs, samples))
    rms = math.sqrt(sum(v * v for v in samples) / len(samples))
    if rms < 1e-6:
        raise ValueError(f"Silent sound: {source.name}")
    gain = min(10 ** (-4 / 20) / peak, 10 ** (-19 / 20) / rms)
    return array.array("h", (round(v * gain * 32767) for v in samples))


def main():
    project = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default="ffmpeg", help="Path to the local FFmpeg executable")
    args = parser.parse_args()
    source = project.parent / "temp-assets" / "Sounds"
    destination = project / "Assets/ExternalAssets/FactoryWarsLocalAudio/Resources/FactoryWars/Audio/Local"
    destination.mkdir(parents=True, exist_ok=True)
    for event, (file, start, duration, overlap) in CLIPS.items():
        samples = prepare(args.ffmpeg, source / file, start, duration, overlap)
        with wave.open(str(destination / f"{event}.wav"), "wb") as output:
            output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
            output.writeframes(samples.tobytes())
        print(f"{event}: {len(samples) / RATE:.2f} s")


if __name__ == "__main__":
    main()

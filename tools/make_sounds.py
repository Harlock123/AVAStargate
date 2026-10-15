#!/usr/bin/env python3
"""Synthesises AVAStargate's sound effects and tunes into ../src/AVAStargate/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAStargate", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The laser: a bright falling zing; the enemy's shot, a short blip; the engine, a rushing roar.
    write("Fire.wav", mix(tone(0.16, 3200, 900, "saw", release=0.9), tone(0.16, 1600, 450, "square", release=0.9, gain=0.4)), 0.24)
    write("EnemyFire.wav", tone(0.09, 1100, 700, "square", release=0.8), 0.16)
    write("Thrust.wav", mix(tone(0.32, 900, 500, "noise", attack=0.03, release=0.4), tone(0.32, 55, 50, "saw", release=0.4, gain=0.5)), 0.2)
    # Blowing things up: a crackling burst, and a deep boom for the big ones.
    write("Explode.wav", mix(tone(0.45, 3000, 150, "noise", release=0.8), tone(0.3, 400, 60, "square", release=0.8, gain=0.5)), 0.4)
    write("BigExplode.wav", mix(tone(0.9, 2200, 60, "noise", release=0.6), tone(0.8, 200, 30, "saw", release=0.6, gain=0.8)), 0.5)
    write("Materialise.wav", cat(*[tone(0.05, f, kind="tri", release=0.4) for f in [400, 600, 500, 800, 700, 1100, 1000, 1500]]), 0.22)
    # The colonists: snatched up, screaming as they fall, caught, set down, and lost.
    write("Snatch.wav", tone(0.5, 300, 1400, "square", release=0.3, vibrato=0.08, vib_rate=14), 0.24)
    write("Scream.wav", tone(1.1, 1800, 400, "tri", release=0.3, vibrato=0.06, vib_rate=11), 0.26)
    write("Catch.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6"]]), 0.34)
    write("SetDown.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.4) for n in ["G5", "E5", "C5", "G5", "C6"]]), 0.34)
    write("Mutate.wav", mix(tone(0.7, 200, 900, "saw", release=0.4, vibrato=0.2, vib_rate=25), tone(0.7, 1200, 150, "square", release=0.4, gain=0.4)), 0.32)
    write("ColonistLost.wav", cat(*[tone(0.16, hz(n), kind="tri", release=0.5) for n in ["E5", "C5", "A4"]]), 0.3)
    # The enemy's tricks: a mine clicking into place, a fireball's whoosh, a Hunter's warning, a Dart hatching.
    write("Mine.wav", tone(0.05, 2400, 2000, "pulse", release=0.8), 0.14)
    write("Fireball.wav", mix(tone(0.4, 600, 200, "noise", attack=0.05, release=0.6), tone(0.4, 180, 120, "saw", release=0.6, gain=0.4)), 0.24)
    write("HunterIn.wav", cat(*[tone(0.12, f, kind="square", release=0.3) for f in [900, 1200, 900, 1200]]), 0.24)
    write("Hatch.wav", tone(0.12, 500, 1600, "tri", release=0.5), 0.2)
    # The ship's emergency buttons: the smart bomb's blast, hyperspace's swoop, the cloak's shimmer, the gate's rush.
    write("SmartBomb.wav", mix(tone(1.0, 4000, 40, "noise", release=0.5), tone(1.0, 900, 30, "square", release=0.5, gain=0.6)), 0.55)
    write("Hyperspace.wav", tone(0.6, 200, 3000, "saw", release=0.2, vibrato=0.05, vib_rate=30), 0.26)
    write("Cloak.wav", tone(0.4, 1500, 2500, "sine", release=0.3, vibrato=0.25, vib_rate=40), 0.26)
    write("Gate.wav", mix(tone(0.8, 120, 2400, "saw", release=0.2), tone(0.8, 2400, 200, "tri", release=0.2, gain=0.5)), 0.3)
    write("WarpAhead.wav", cat(tone(0.6, 150, 3000, "saw", release=0.1), *[tone(0.09, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6", "E6", "G6", "C7"]]), 0.36)
    write("Death.wav", cat(mix(tone(1.2, 3500, 50, "noise", release=0.5), tone(1.2, 500, 30, "saw", release=0.5, gain=0.7)),
                           *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["G4", "Eb4", "C4"]]), 0.5)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["C5", "G5", "C6"]]), 0.32)
    write("WaveDone.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6", "G5", "C6", "E6"]]), 0.36)
    write("PlanetLost.wav", cat(mix(tone(2.0, 1500, 30, "noise", release=0.4), tone(2.0, 160, 25, "saw", release=0.4, gain=0.8)),
                                *[tone(0.25, hz(n), kind="tri", release=0.5) for n in ["C4", "B3", "Bb3", "A3"]]), 0.55)
    write("PlanetBack.wav", cat(*[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["C4", "E4", "G4", "C5", "E5", "G5", "C6"]]), 0.36)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "G5", "C6", "G5", "C6", "E6"]]), 0.38)

    # The engine's throb: a low pulsing drone. Loops (four seconds, whole cycles).
    n = int(RATE * 4)
    hum = []
    for i in range(n):
        t = i / RATE
        beat = 0.55 + 0.45 * math.sin(2 * math.pi * 2 * t) ** 2
        hum.append(beat * (math.sin(2 * math.pi * 45 * t) + 0.4 * math.sin(2 * math.pi * 90 * t) + 0.15 * (2 * ((60 * t) % 1) - 1)))
    write("Music.wav", hum, 0.22)

    # The title: a racing anthem in D minor (an original tune).
    lead = [("D5", 0.5), ("F5", 0.5), ("A5", 1), ("D6", 0.5), ("C6", 0.5), ("A5", 1), ("Bb5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("E5", 2),
            ("F5", 0.5), ("G5", 0.5), ("A5", 1), ("C6", 0.5), ("Bb5", 0.5), ("A5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5), ("C#5", 0.5), ("D5", 2),
            ("A5", 0.5), ("A5", 0.5), ("D6", 1), ("E6", 0.5), ("F6", 0.5), ("E6", 1), ("D6", 0.5), ("C6", 0.5), ("A5", 1), ("C#6", 1), ("D6", 3)]
    bass = [("D3", 1), ("D3", 1), ("F3", 1), ("F3", 1), ("G2", 1), ("G2", 1), ("A2", 2),
            ("F2", 1), ("F2", 1), ("C3", 1), ("C3", 1), ("G2", 1), ("A2", 1), ("D3", 2),
            ("D3", 1), ("Bb2", 1), ("C3", 1), ("A2", 1), ("F2", 1), ("A2", 1), ("D3", 3)]
    write("TitleTune.wav", cat(tune(176, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(170, [("D5", 0.5), ("A5", 0.5), ("D6", 0.5), ("F6", 0.5), ("E6", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(72, [("A4", 1), ("F4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D3", 2), ("Bb2", 2), ("A2", 2), ("D2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()

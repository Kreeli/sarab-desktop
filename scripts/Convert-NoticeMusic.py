#!/usr/bin/env python3
"""Convert the OpenGameArt 20-loop WAV pack to compact embedded PCM assets.

Usage: python scripts/Convert-NoticeMusic.py build/downloads/videogame-loops.zip
"""

import io
import os
import struct
import sys
import wave
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT = os.path.join(ROOT, "assets", "notice-music")
EXPECTED_TRACKS = 20
TARGET_RATE = 11025


def convert_without_audioop(raw):
    # A nearest-neighbour fallback is adequate for the deliberately lo-fi output.
    # Stereo 16-bit frames are 4 bytes, so one sample per four frames is 16 bytes.
    output = bytearray()
    for frame_offset in range(0, len(raw) - 3, 4 * 4):
        left, right = struct.unpack_from("<hh", raw, frame_offset)
        mono = (left + right) // 2
        output.append(max(0, min(255, (mono >> 8) + 128)))
    return bytes(output)


def convert(raw, channels, width, sample_rate):
    if channels != 2 or width != 2 or sample_rate != 44100:
        raise ValueError("Expected stereo 44.1 kHz, 16-bit PCM source audio")

    try:
        import audioop
    except ImportError:  # Python 3.13 removed audioop; keep the converter dependency-free.
        return convert_without_audioop(raw)

    mono = audioop.tomono(raw, 2, 0.5, 0.5)
    resampled, _ = audioop.ratecv(mono, 2, 1, sample_rate, TARGET_RATE, None)
    signed_8bit = audioop.lin2lin(resampled, 2, 1)
    return audioop.bias(signed_8bit, 1, 128)


def main():
    if len(sys.argv) != 2:
        raise SystemExit("Pass the downloaded 20-loop WAV ZIP path.")
    archive_path = os.path.abspath(sys.argv[1])
    if not os.path.isfile(archive_path):
        raise SystemExit("Source ZIP not found: " + archive_path)

    with zipfile.ZipFile(archive_path) as archive:
        members = sorted(
            (name for name in archive.namelist() if name.lower().endswith(".wav")),
            key=lambda name: (os.path.basename(name).casefold(), name.casefold()),
        )
        if len(members) != EXPECTED_TRACKS:
            raise SystemExit("Expected 20 WAV tracks; found %d." % len(members))

        os.makedirs(OUTPUT, exist_ok=True)
        mapping = [
            "# Embedded notice music",
            "",
            "Source: Snabisch, [20 excellent 8 bit loops](https://opengameart.org/content/20-excellent-8-bit-loops), CC-BY 3.0.",
            "Converted from stereo 44.1 kHz / 16-bit PCM to mono 11.025 kHz / 8-bit PCM for compact retro-style playback.",
            "The desktop app embeds these files in `SarabDesktop.exe`; it extracts only the currently playing track to the user's temporary folder and removes it when playback ends or the certificate closes.",
            "",
            "| Embedded file | Original track |",
            "| --- | --- |",
        ]
        for index, member in enumerate(members, 1):
            source = io.BytesIO(archive.read(member))
            with wave.open(source, "rb") as reader:
                if reader.getcomptype() != "NONE":
                    raise SystemExit("Compressed source WAV is unsupported: " + member)
                channels, width, rate = reader.getnchannels(), reader.getsampwidth(), reader.getframerate()
                raw = reader.readframes(reader.getnframes())
            pcm = convert(raw, channels, width, rate)
            if not pcm:
                raise SystemExit("Track converted to an empty file: " + member)

            filename = "track-%02d.wav" % index
            output_path = os.path.join(OUTPUT, filename)
            with wave.open(output_path, "wb") as writer:
                writer.setnchannels(1)
                writer.setsampwidth(1)
                writer.setframerate(TARGET_RATE)
                writer.writeframes(pcm)
            original_name = os.path.basename(member).replace("|", "\\|")
            mapping.append("| `%s` | %s |" % (filename, original_name))
            print("%s  %s  %d bytes" % (filename, original_name, os.path.getsize(output_path)))

        readme = os.path.join(OUTPUT, "README.md")
        with open(readme, "w", encoding="utf-8", newline="\n") as file:
            file.write("\n".join(mapping) + "\n")
        print("Converted %d tracks into %s" % (len(members), OUTPUT))


if __name__ == "__main__":
    main()

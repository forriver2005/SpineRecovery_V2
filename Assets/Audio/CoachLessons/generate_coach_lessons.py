"""Development-only static speech generation; Unity never calls this script."""
import asyncio
import argparse
import csv
import hashlib
import json
import shutil
import tempfile
from pathlib import Path

import edge_tts
import soundfile as sf

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[2]
OUTPUT = PROJECT / "Assets/Resources/CoachLessons"
VOICE = "zh-CN-XiaoxiaoNeural"


async def generate():
    parser = argparse.ArgumentParser()
    parser.add_argument("--action", help="Regenerate only this action; retain all other audio")
    action = parser.parse_args().action
    with (ROOT / "lesson-lines.tsv").open(encoding="utf-8-sig", newline="") as source:
        lines = list(csv.DictReader(source))
    if action:
        lines = [line for line in lines if line["action"] == action]
        if not lines:
            raise ValueError(f"Unknown action: {action}")
    manifest = []
    with tempfile.TemporaryDirectory(prefix="coach-lessons-") as temporary:
        staging = Path(temporary)
        semaphore = asyncio.Semaphore(3)

        async def one(line):
            async with semaphore:
                name = f"Coach_{line['action']}_{line['key']}"
                if not line["text"].strip():
                    raise ValueError(f"Empty text: {name}")
                mp3 = staging / f"{name}.mp3"
                wav = staging / f"{name}.wav"
                for attempt in range(3):
                    try:
                        await edge_tts.Communicate(
                            line["text"], VOICE, rate="+0%", volume="+0%", pitch="+0Hz", connect_timeout=10, receive_timeout=20
                        ).save(str(mp3))
                        break
                    except Exception:
                        if attempt == 2:
                            raise
                        await asyncio.sleep(2)
                samples, rate = sf.read(mp3)
                # Decode the complete clip. No trimming, time stretching or pitch shift.
                sf.write(wav, samples, rate, subtype="PCM_16")
                manifest.append({
                    **line,
                    "path": (OUTPUT / wav.name).relative_to(PROJECT).as_posix(),
                    "seconds": round(len(samples) / rate, 4),
                    "format": f"{rate} Hz / 16 bit / 1 ch",
                    "sha256": hashlib.sha256(wav.read_bytes()).hexdigest(),
                    "engine": "Microsoft Edge neural TTS (development only)",
                    "voice": VOICE, "rate": "+0%", "pitch": "+0Hz", "volume": "+0%",
                })
                print(f"Generated {name}: {len(samples) / rate:.3f}s", flush=True)
        await asyncio.gather(*(one(line) for line in lines))
        manifest.sort(key=lambda item: next(i for i, line in enumerate(lines) if line["action"] == item["action"] and line["key"] == item["key"]))
        # Publish only after the entire batch succeeds; retain Unity .meta GUIDs.
        OUTPUT.mkdir(parents=True, exist_ok=True)
        for item in manifest:
            target = PROJECT / item["path"]
            shutil.copyfile(staging / target.name, target)
        retained = []
        if action:
            retained = [item for item in json.loads((ROOT / "audio-manifest.json").read_text(encoding="utf-8")) if item["action"] != action]
        (ROOT / "audio-manifest.json").write_text(
            json.dumps(retained + manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
    print(f"Published {len(manifest)} clips with {VOICE}.")


if __name__ == "__main__":
    asyncio.run(generate())

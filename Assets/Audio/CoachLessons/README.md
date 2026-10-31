# Coach lesson speech

Generate static Unity audio on the development machine:

```powershell
python -m pip install -r Assets/Audio/CoachLessons/requirements.txt
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/Audio/CoachLessons/Generate-CoachLessons.ps1
```

Input: `lesson-lines.tsv` (legacy filename; comma-separated CSV, UTF-8 BOM).
Engine: Microsoft Edge neural TTS via edge-tts 7.3.1.
Voice: `zh-CN-XiaoxiaoNeural`; rate `+0%`, volume `+0%`, pitch `+0Hz`.
Development generation needs network access; Unity only plays packaged static files.
The full returned MP3 is decoded into 24000 Hz, mono, 16-bit PCM WAV.
No trimming, resampling, time stretching, pitch shifting or runtime TTS.

Output: `Assets/Resources/CoachLessons/Coach_<action>_<key>.wav`.
All clips are staged before publication. Existing Unity .meta GUIDs are retained.
`audio-manifest.json` records text, voice parameters, duration and SHA-256.
Playback nodes and rationale: `docs/coach-mode-redesign/14-module-a-audio-table.md`.

## Voice match evidence (2026-10-29)

Original speech in `Assets/Audio/VoicePrompts` has no voice-name metadata.
Default Xiaoxiao synthesis of two identical original phrases gives:

| Phrase | Original / candidate duration | Decoded PCM Pearson correlation |
|---|---|---|
| 请根据教练演示调整动作 | 3.120 / 3.120 s | 0.996778 |
| 请跟随教练前往下一个动作 | 3.144 / 3.144 s | 0.998253 |

Comparison uses unshifted, unmodified decoded samples at 24000 Hz.
This is strong signal evidence for the matching voice/settings, not a claim of human listening acceptance or recovered historical provenance.
The shorter 请保持住 candidate differed in duration and is not used as a matching proof.
Original introduction/countdown/number/hold/start recordings remain untouched.
The former Huihui lesson WAVs and generation script are backed up outside Assets
at `.codex/voice-match/huihui-backup`.

The earlier duplicate Resources tree was moved intact to
`Assets/Assets/LegacyCoachAudio` by the previous implementation.
Older generated `Assets/Resources/Audio/VoicePrompts/Coach_*` files are preserved;
formal lessons load only the exact `CoachLessons/Coach_*` resource keys above.

Listening and device speaker acceptance remain separate from file generation,
signal comparison and Unity import verification.

# Wisdom Hub — AI Video Generator
**ASP.NET Core MVC app that turns your script into a full YouTube video automatically.**

---

## What it does
1. You paste your script (one line = one scene)
2. Claude generates a charcoal/sketch image prompt for each line
3. Gemini generates the image
4. Edge TTS (Microsoft, free) speaks the line in a warm neural voice
5. FFmpeg merges image + audio into a clip with a subtle Ken Burns zoom
6. All clips are joined into one final MP4

---

## Prerequisites

### 1. Python + Edge TTS (voiceover)
```bash
pip install edge-tts
```

### 2. FFmpeg (video merging)
**Windows:**
```
winget install ffmpeg
```
Or download from https://ffmpeg.org and add `bin/` to PATH.

**Verify:**
```
ffmpeg -version
ffprobe -version
```

### 3. API Keys (both free tier)

| Key | Where to get it | Free limit |
|-----|----------------|------------|
| Anthropic | https://console.anthropic.com | ~$5 free credit |
| Gemini | https://ai.google.dev | 1500 images/day |

Put both in `appsettings.json`:
```json
{
  "Anthropic": { "ApiKey": "sk-ant-..." },
  "Gemini":    { "ApiKey": "AIza..." }
}
```

---

## Run the app
```bash
dotnet restore
dotnet run
```
Open: **http://localhost:5000**

---

## Voices available
| Voice | Character |
|-------|-----------|
| `en-US-AriaNeural` | Warm, expressive — **best for wisdom/relationship content** |
| `en-US-JennyNeural` | Friendly, upbeat |
| `en-US-GuyNeural` | Deep, calm |
| `en-GB-SoniaNeural` | British, authoritative |

All are completely free via Edge TTS — no API key needed.

---

## Script tips
- One complete thought per line
- 8–20 words per line for natural pacing
- 5–10 lines = 60–90 second video
- The app auto-adds 0.5s pause between each statement

---

## Output
Videos are saved to `wwwroot/output/{jobId}/final_video.mp4`
You can download directly from the browser after generation.

---

## Folder structure
```
WisdomVideoApp/
├── Controllers/
│   └── VideoController.cs       ← SSE streaming, download endpoint
├── Models/
│   └── VideoModels.cs
├── Services/
│   └── VideoGenerationService.cs ← All the pipeline logic
├── Views/Video/
│   └── Create.cshtml            ← The UI
├── wwwroot/output/              ← Generated videos land here
├── appsettings.json             ← Put your API keys here
├── Program.cs
└── setup.bat                    ← Run this first on Windows
```

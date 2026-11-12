@echo off
echo ===================================================
echo  Wisdom Hub Video Generator - Setup
echo ===================================================
echo.

echo [1/4] Installing Edge TTS (Python voiceover)...
pip install edge-tts
echo.

echo [2/4] Checking FFmpeg...
ffmpeg -version >nul 2>&1
IF %ERRORLEVEL% NEQ 0 (
    echo FFmpeg NOT found. Please install it:
    echo   1. Download from https://ffmpeg.org/download.html
    echo   2. Extract and add the 'bin' folder to your PATH
    echo   OR run:  winget install ffmpeg
) ELSE (
    echo FFmpeg is already installed. OK.
)
echo.

echo [3/4] Checking Python...
python --version
IF %ERRORLEVEL% NEQ 0 (
    echo Python NOT found. Install from https://python.org
)
echo.

echo [4/4] Restoring .NET packages...
dotnet restore
echo.

echo ===================================================
echo  NEXT STEPS:
echo  1. Open appsettings.json
echo  2. Set your Anthropic API key  (https://console.anthropic.com)
echo  3. Set your Gemini API key     (https://ai.google.dev)
echo  4. Run: dotnet run
echo  5. Open: http://localhost:5000
echo ===================================================
pause

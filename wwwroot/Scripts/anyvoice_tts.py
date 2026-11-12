import sys
from playwright.sync_api import sync_playwright
import time

TTS_URL = "https://anyvoicelab.com/voices/anyvoicelab-voice/"


def generate_audio(text, output_path):
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context()
        page = context.new_page()
        page.set_default_timeout(60000)

        page.goto(TTS_URL, wait_until="domcontentloaded", timeout=90000)

        page.fill("#to-convert-text", text)
        page.click("button.tts-voice-button")
        page.wait_for_selector("button.download-audio-button", timeout=180000)
        time.sleep(1)

        with page.expect_download() as dl_info:
            page.click("button.download-audio-button")
        dl_info.value.save_as(output_path)

        browser.close()
        print(f"Saved: {output_path}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python anyvoice_tts.py input.txt output.mp3")
        sys.exit(1)

    with open(sys.argv[1], "r", encoding="utf-8") as f:
        text = f.read()

    generate_audio(text, sys.argv[2])
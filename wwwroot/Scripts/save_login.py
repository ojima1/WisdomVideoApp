from playwright.sync_api import sync_playwright

with sync_playwright() as p:
    browser = p.chromium.launch(headless=False)

    context = browser.new_context()

    page = context.new_page()

    page.goto("https://anyvoicelab.com/login/")

    input("Log in completely, then press ENTER here...")

    context.storage_state(path="anyvoicelab_auth.json")

    browser.close()

print("Login saved successfully!")
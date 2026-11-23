"""Run View and tests/BrowserHost first. Uses a simulated microphone and synthetic AI, not paid services."""
import os
import uuid
from playwright.sync_api import sync_playwright, expect

base = os.getenv('SPEAKREADY_VIEW_URL', 'http://localhost:5012')
with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=os.getenv('CHROMIUM_PATH', '/usr/bin/chromium'), headless=True,
        args=['--no-sandbox', '--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'])
    context = browser.new_context(permissions=['microphone'], viewport={'width': 1280, 'height': 900})
    page = context.new_page()
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    name = 'browser' + uuid.uuid4().hex[:12]
    page.goto(base + '/register')
    page.locator('#reg-name').fill('Browser Test')
    page.locator('#reg-username').fill(name)
    page.locator('#reg-email').fill(name + '@example.com')
    page.locator('#reg-password').fill('Browser-test-password')
    page.locator('#reg-confirm').fill('Browser-test-password')
    page.get_by_role('button', name='יוצרים חשבון').click()
    expect(page).to_have_url(base + '/login', timeout=20000)
    page.locator('#username').fill(name)
    page.locator('#password').fill('Browser-test-password')
    page.get_by_role('button', name='כניסה', exact=True).click()
    expect(page).to_have_url(base + '/student', timeout=15000)
    expect(page.get_by_text('כאן יופיעו התשובות והמשובים שלך לאחר התרגול הראשון.')).to_be_visible()
    page.reload()
    expect(page.get_by_role('link', name='לתרגול', exact=True)).to_have_count(3)
    assert any(c['name'] == 'SpeakReady.Session' and c['httpOnly'] for c in context.cookies())
    page.get_by_role('link', name='לתרגול', exact=True).first.click()
    page.evaluate('''window.originalGetUserMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
        navigator.mediaDevices.getUserMedia = () => Promise.reject(new DOMException('Denied', 'NotAllowedError')); void 0;''')
    page.get_by_role('button', name='התחלת הקלטה').click()
    expect(page.get_by_text('לא ניתן להפעיל מיקרופון. בדקו הרשאות בדפדפן ונסו שוב.')).to_be_visible()
    page.evaluate('navigator.mediaDevices.getUserMedia = window.originalGetUserMedia; void 0')
    page.get_by_role('button', name='התחלת הקלטה').click()
    page.wait_for_timeout(2200)
    page.get_by_role('button', name='עצירה', exact=True).click()
    expect(page.locator('audio')).to_be_visible()
    expect(page.get_by_role('button', name='שליחה לקבלת משוב')).to_be_disabled()
    page.get_by_role('checkbox').check()
    page.route('**/practice/upload', lambda route: route.abort())
    page.get_by_role('button', name='שליחה לקבלת משוב').click()
    expect(page.get_by_text('החיבור נותק. נסו לשלוח שוב; לא ייווצר ניסיון כפול.')).to_be_visible()
    expect(page.locator('audio')).to_be_visible()
    page.unroute('**/practice/upload')
    page.get_by_role('button', name='שליחה לקבלת משוב').click()
    expect(page).to_have_url(__import__('re').compile('/student/report/\\d+'), timeout=20000)
    expect(page.get_by_text('המשוב מוכן', exact=True)).to_be_visible(timeout=30000)
    expect(page.get_by_text('I enjoy reading because I learn new things.', exact=True)).to_be_visible()
    page.locator('audio').evaluate('(audio) => audio.play()')
    page.wait_for_function('document.querySelector("audio").currentTime > 0.1')
    page.locator('audio').evaluate('(audio) => audio.pause()')
    report_url = page.url
    # Verify private audio and CSRF protection from the actual browser session.
    audio_url = page.locator('audio').get_attribute('src')
    assert context.request.get(base + audio_url).status == 200
    assert context.request.post(base + '/account/logout').status == 400
    other = browser.new_context()
    assert other.request.get(base + audio_url).status == 401
    other.close()
    page.reload()
    expect(page.get_by_text('המשוב מוכן', exact=True)).to_be_visible(timeout=15000)
    page.set_viewport_size({'width': 390, 'height': 844})
    assert page.evaluate('document.documentElement.scrollWidth <= window.innerWidth'), 'Mobile horizontal overflow'
    page.screenshot(path='/tmp/speakready-mobile.png', full_page=True)
    page.get_by_role('button', name='מחיקת הניסיון', exact=True).click()
    page.get_by_role('button', name='כן, למחוק', exact=True).click()
    expect(page).to_have_url(base + '/student')
    expect(page.get_by_text('כאן יופיעו התשובות והמשובים שלך לאחר התרגול הראשון.')).to_be_visible()
    assert context.request.get(base + audio_url).status == 404
    page.get_by_role('button', name='התנתקות', exact=True).click()
    expect(page).to_have_url(base + '/')
    page.goto(report_url)
    expect(page).to_have_url(base + '/login', timeout=15000)
    assert not errors, errors
    browser.close()
    print('PASS: register, login, refresh, microphone denial, recording, consent, network retry, upload, feedback, playback, private audio, CSRF, mobile, delete, logout')

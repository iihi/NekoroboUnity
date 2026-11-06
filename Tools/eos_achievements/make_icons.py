# 実績の仮のアイコン（1024x1024 の PNG）を Chrome で描く。本番の絵が来たら差し替える。
# python make_icons.py  … icons/ に star1〜3.png（取った）と locked.png（まだ）を作る
import os, subprocess, re
here = os.path.dirname(os.path.abspath(__file__))
chrome = r"C:/Program Files/Google/Chrome/Application/chrome.exe"
src = open(os.path.join(here, "..", "..", "Assets", "Nekorobo", "Resources", "Ui", "Title", "medal.png"), "rb")  # 存在の確認だけ
src.close()
MEDAL = '<path d="M22 8l7 17M42 8l-7 17"/><circle cx="32" cy="38" r="15"/><path d="M32 30l2.6 5.3 5.8.8-4.2 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z" fill="currentColor" stroke-width="2"/>'
def svg(stars, locked):
    bg1, bg2, ink, edge = ("#6b7280", "#374151", "#1f2937", "#111827") if locked else ("#fff6c4", "#ffd34a", "#6a3c00", "#4a3500")
    st = ""
    if not locked:
        for i in range(3):
            x = 512 + (i - 1) * 150
            st += '<text x="%d" y="930" font-size="150" text-anchor="middle" fill="%s" stroke="%s" stroke-width="10" paint-order="stroke">★</text>' % (x, "#ffd34a" if i < stars else "#ffffff40", edge)
    q = '<text x="512" y="600" font-size="300" text-anchor="middle" fill="#d1d5db" font-family="Segoe UI" font-weight="900">?</text>' if locked else ""
    med = "" if locked else '<g transform="translate(192 150) scale(10)" fill="none" stroke="%s" stroke-width="4.4" stroke-linecap="round" stroke-linejoin="round" color="%s">%s</g>' % (ink, ink, MEDAL)
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">'
            '<defs><radialGradient id="g" cx="36%%" cy="30%%" r="80%%"><stop offset="0" stop-color="%s"/><stop offset="0.5" stop-color="%s"/><stop offset="1" stop-color="%s"/></radialGradient></defs>'
            '<rect width="1024" height="1024" rx="160" fill="#1c2538"/>'
            '<circle cx="512" cy="470" r="380" fill="url(#g)" stroke="%s" stroke-width="40"/>%s%s%s</svg>') % (bg1, bg2, edge, edge, med, q, st)
for name, stars, locked in [("star1", 1, False), ("star2", 2, False), ("star3", 3, False), ("locked", 0, True)]:
    h = os.path.join(here, "icons", name + ".html")
    open(h, "w", encoding="utf-8").write('<html><body style="margin:0;background:transparent">' + svg(stars, locked) + "</body></html>")
    png = os.path.join(here, "icons", name + ".png")
    subprocess.run([chrome, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000",
                    "--window-size=1024,1024", "--screenshot=" + png, "file:///" + h.replace("\\", "/")], capture_output=True)
    os.remove(h)
    print(name, os.path.exists(png))

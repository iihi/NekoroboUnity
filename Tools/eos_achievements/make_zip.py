# Dev Portal の「一括インポート」に入れる ZIP を作る（EOS のテンプレート BulkTemplate.zip と同じ形）。
#   python make_zip.py  … achievements.csv と icons/ から nekorobo_achievements.zip を作る
#
# ZIP の中（すべて直下）
#   achievementDefinitions.csv   … name,hidden,statThresholds（統計は使わないので空。ゲームが送って解除する）
#   achievementLocalizations.csv … name,locale,lockedTitle,lockedDescription,unlockedTitle,unlockedDescription,flavorText,lockedIcon,unlockedIcon
#                                  locale が空の行が既定（英語）、ja の行が日本語の上書き
#   stats.csv                    … name,aggregationType（統計は使わないので見出しだけ）
#   locked.png・star1〜3.png     … アイコン（256x256）
# CSV は BOM なしの UTF-8・改行は CRLF（テンプレートと同じ）。
# ★2・★3 は隠し（hidden）にして、取る前の名前と説明は ???（ゲームの一覧と同じ）。
import csv, io, os, zipfile
here = os.path.dirname(os.path.abspath(__file__))

rows = list(csv.DictReader(open(os.path.join(here, "achievements.csv"), encoding="utf-8-sig")))

def table(header, lines):
    b = io.StringIO()
    w = csv.writer(b, lineterminator="\r\n")
    w.writerow(header)
    for l in lines: w.writerow(l)
    return b.getvalue().encode("utf-8")

defs = [[r["id"], "TRUE" if r["hidden"] == "yes" else "FALSE", ""] for r in rows]
locs = []
for r in rows:
    hid = r["hidden"] == "yes"
    icon = "star%d.png" % int(r["stars"])
    locs.append([r["id"], "", "???" if hid else r["name_en"], "???" if hid else r["description_en"],
                 r["name_en"], r["description_en"], "", "locked.png", icon])
    locs.append([r["id"], "ja", "？？？" if hid else r["name_ja"], "？？？" if hid else r["description_ja"],
                 r["name_ja"], r["description_ja"], "", "locked.png", icon])

out = os.path.join(here, "nekorobo_achievements.zip")
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("achievementDefinitions.csv", table(["name", "hidden", "statThresholds"], defs))
    z.writestr("achievementLocalizations.csv", table(["name", "locale", "lockedTitle", "lockedDescription", "unlockedTitle",
                                                      "unlockedDescription", "flavorText", "lockedIcon", "unlockedIcon"], locs))
    z.writestr("stats.csv", table(["name", "aggregationType"], []))
    for n in ["locked", "star1", "star2", "star3"]:
        z.write(os.path.join(here, "icons", n + ".png"), n + ".png")
print(out, len(defs), "achievements", os.path.getsize(out) // 1024, "KB")

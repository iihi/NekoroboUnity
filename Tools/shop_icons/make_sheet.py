import json, io, sys
# 品物の絵（HTML版 ICONS をその色で）と、ショップの指（👆）を1枚の紙に並べる。
# 1マス 156×104（SVG の 78×52 の2倍）。指は 64×64。
src = json.load(io.open(sys.argv[1], encoding='utf-8'))
cells = []
x = 0
html = ['<!doctype html><html><head><meta charset="utf-8"><style>',
        'html,body{margin:0;background:transparent}',
        '.c{position:absolute;top:0;width:156px;height:104px}',
        '.c svg{width:156px;height:104px;display:block}',
        '.f{position:absolute;top:0;width:64px;height:64px;font-size:44px;line-height:64px;text-align:center;',
        'font-family:"Segoe UI Emoji";filter:drop-shadow(0 2px 3px #0007)}',
        '</style></head><body>']
for k, col, svg in src:
    html.append('<div class="c" style="left:%dpx">%s</div>' % (x, svg))
    cells.append([k, x, 156, 104])
    x += 160
html.append('<div class="f" style="left:%dpx">👆</div>' % x)
cells.append(['finger', x, 64, 64])
x += 64
html.append('</body></html>')
io.open(sys.argv[2], 'w', encoding='utf-8').write('\n'.join(html))
json.dump({'w': x, 'cells': cells}, io.open(sys.argv[3], 'w', encoding='utf-8'))
print(x)

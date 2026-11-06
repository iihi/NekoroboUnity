# EOS の実績（Epic Developer Portal での登録）

ゲームの実績（39個）を EOS（Epic Online Services）の実績にも送っています（`Scripts/Net/EosAch.cs`）。
Dev Portal に**同じ ID で**登録すると、取った実績が EOS 側にも記録されます。
登録が済むまでは、EOS へは送れずに**この端末の記録だけ**になります（遊ぶのには困りません）。

- 名乗りは Connect の端末ID（Epic アカウントは要りません。オンライン対戦の `NetEos` と同じ）。
  スイッチなどに出すときは、`EosAch.Login` を各機種のログイン（Connect の外部の資格情報）に替えれば、送り方はそのまま使えます。
- ログインしたら EOS 側の取った実績を読み、端末だけで取っていた物を送ります。EOS だけで取っていた物（別の PC で取った物）は、知らせを出さずに端末の記録へ入れます。
- 送れなかった物は 30 秒おきに送り直します。Dev Portal に無い ID は捨てます。

## 1. クライアントの方針（Client Policy）に実績の許可を足す

いまの方針（Peer2Peer）には実績の許可がなく、送ると次のエラーが返ります（2026-11-06 に確認）。

```
Client ... missing achievements:unlockAchievementForLocalUser
```

Product「Cat Serving」→ Product Settings → Clients で、クライアント **NekoroboClient** の方針（Peer2Peer）を開き、
**Achievements** の次の2つを許可してください。

| 許可 | 使う所 |
|---|---|
| `unlockAchievementForLocalUser` | 取った実績を送る（**これが無いと送れません**） |
| `findAchievementsForLocalUser` | ログインしたときに、EOS 側の取った実績を読む（端末の記録と合わせる） |

## 2. 実績を登録する

Game Services → Achievements →「実績の作成」→ **単一の実績（統計データなし）** で、下の表の**ID のまま**39個を作ってください（表と同じ中身の CSV：`achievements.csv`）。
統計（Stats）は使いません。ゲームが「取った」と送って解除する形です（クライアントの方針の `unlockAchievementForLocalUser` で送ります）。

| 画面の欄 | 入れる物 |
|---|---|
| 実績ID | 表の ID（`deliver` など） |
| ロケール | 最初に作ったロケールが既定（ほかの言語の代わり）になるので、**English を先に**入れ、日本語は上書き（オーバーライド）で足す |
| 可視性 | ★1 は「表示済み」、★2・★3 は「非表示」（ゲームの一覧でも取るまで？？？） |
| ロックされたアイコン | `icons/locked.png` |
| アンロックされたアイコン | ★の数で `icons/star1.png`〜`star3.png` |
| ロックされた表示名・説明文 | ★1 は名前・条件と同じ。★2・★3 は `???`（日本語は `？？？`） |
| ロック解除された表示名・説明 | 名前・条件 |
| フレーバーテキスト | 空のまま |

アイコンは仮の絵です（256×256 の PNG、Dev Portal の決まりは 256×256・256KB 以下）。作り直すときは `python make_icons.py`。本番の絵が来たら差し替えてください。

**一括インポート（ZIP）**：形式の説明が見つからなかったので、1つ手で作ってから「一括エクスポート」で書き出した ZIP を見本にして、残りを同じ形で作るのが確実です。
保存したら、Dev Portal の指示に従って公開（Publish）してください。

最後の1つ（`tbd`・準備中）は登録しません。足すときは、ゲームの `Game.ACHS` と、この表の両方に足してください。

## 3. 確かめる

eos.json（鍵）がある状態で遊び、何か実績を取ると、Unity のコンソールに次のように出ます。

- `[EOS実績] ログインしました: …` … EOS にログインできた
- `[EOS実績] 送りました: deliver` … 送れた
- `[EOS実績] Dev Portal のクライアントの方針（Client Policy）に、実績の許可がありません` … 1. がまだ
- `[EOS実績] Dev Portal に無い実績です: …` … 2. のその ID がまだ

## 実績の一覧

（元は `Scripts/Game/Game.Ach.cs` の `ACHS`。変えたら、この表と `achievements.csv` も合わせてください）

| ID | 仕分け | ★ | 隠す | 名前 | 条件 | Name (en) | Description (en) |
|---|---|---|---|---|---|---|---|
| `deliver` | はじめて | ★ |  | はじめての配膳 | 料理を1つ届ける | First Delivery | Deliver a dish |
| `clear` | はじめて | ★ |  | ラウンドクリア | 面を1つクリアする | Round Clear | Clear a stage |
| `buy` | はじめて | ★ |  | お買い物デビュー | ショップで何か買う | First Purchase | Buy something at the shop |
| `item` | はじめて | ★ |  | アイテムを使ってみた | アイテムを1回使う | Item Rookie | Use an item |
| `jumpout` | はじめて | ★ |  | ジャンプ！ | 池や川からジャンプして岸へ上がる | Jump! | Jump out of a pond or river onto the shore |
| `vip` | はじめて | ★ |  | VIPのおもてなし | VIPに料理を届ける | VIP Treatment | Deliver a dish to a VIP |
| `perfect` | 配膳の腕 | ★ |  | 崩さず運ぶ | 料理を崩さずに届ける | Steady Hands | Deliver a dish without messing it up |
| `pickup` | 配膳の腕 | ★ |  | 拾ってセーフ | 床に落ちた料理を拾う | Saved It! | Pick up a dish from the floor |
| `rush` | 配膳の腕 | ★ |  | 速達便 | 速達ボーナスを付けて届ける | Express Delivery | Deliver with an express bonus |
| `combo5` | 配膳の腕 | ★★ | ○ | 5連続 | 続けて届けて、5連続にする | 5 in a Row | Reach a 5-delivery streak |
| `combo10` | 配膳の腕 | ★★★ | ○ | 10連続 | 続けて届けて、10連続にする | 10 in a Row | Reach a 10-delivery streak |
| `rich` | 配膳の腕 | ★★ | ○ | 高級店の信頼 | 高額オーダーを届ける | Fine Dining Trust | Deliver a premium order |
| `nodmg` | 配膳の腕 | ★★ | ○ | ノーダメージ | 機体のダメージ 0% のまま面をクリアする（注文6品以上の面） | No Damage | Clear a stage with 0% robot damage (stages with 6+ orders) |
| `noshop` | 配膳の腕 | ★★ | ○ | お店を守った | 店舗損壊率 0% で面をクリアする（注文6品以上の面） | Shop Guardian | Clear a stage with 0% shop damage (stages with 6+ orders) |
| `flawless` | 配膳の腕 | ★★★ | ○ | 完璧な仕事 | 届けた料理を1つも崩さずに、面を配りきる（注文6品以上の面） | Flawless Work | Serve every order without messing up a single dish (stages with 6+ orders) |
| `net10k` | お金 | ★★ | ○ | 1面で1万円 | 1面の差引を ¥10,000 以上にする | ¥10,000 Stage | Make a net of ¥10,000 or more in one stage |
| `net30k` | お金 | ★★★ | ○ | 1面で3万円 | 1面の差引を ¥30,000 以上にする | ¥30,000 Stage | Make a net of ¥30,000 or more in one stage |
| `cash50k` | お金 | ★★ | ○ | 小金持ち | 所持金を ¥50,000 以上にする | Well Off | Have ¥50,000 or more |
| `upmax` | お金 | ★★ | ○ | 能力を最大まで | 能力強化のどれか1つを最大Lvにする | Maxed Out | Raise any upgrade to its max level |
| `upall` | お金 | ★★★ | ○ | フル強化 | 能力強化を4つとも最大Lvにする | Fully Upgraded | Raise all four upgrades to their max level |
| `banana` | アイテム | ★ |  | バナナの皮 | 置いたバナナで相手を転ばせる | Banana Peel | Make a rival slip on your banana |
| `steal` | アイテム | ★★ | ○ | 横取り成功 | ブーメランで相手の料理を奪う | Snatched! | Steal a rival's dish with the boomerang |
| `hit` | アイテム | ★★ | ○ | 命中！ | レーザーかミサイルを相手に当てる | Direct Hit! | Hit a rival with a laser or missile |
| `star` | アイテム | ★★ | ○ | 無敵モード | 無敵の間に体当たりする | Invincible | Ram something while invincible |
| `allitems` | アイテム | ★★★ | ○ | アイテム全種 | 8種類のアイテムをすべて使う（通算） | Item Collector | Use all 8 kinds of items (in total) |
| `sea` | ドタバタ | ★ |  | おぼれるにゃ～ | 海に落ちる | Overboard | Fall into the sea |
| `lava` | ドタバタ | ★ |  | あつい！ | 溶岩に入る | Too Hot! | Go into lava |
| `amb` | ドタバタ | ★ |  | 救急車 | お客さんを昇天させる | Ambulance | Knock out a customer |
| `traffic` | ドタバタ | ★ |  | 交通事故 | 車か電車にぶつかる | Traffic Accident | Get hit by a car or train |
| `wreck` | ドタバタ | ★★ | ○ | 大暴れ | 大暴れボーナスをもらう | Rampage | Earn the rampage bonus |
| `allamb` | ドタバタ | ★★★ | ○ | 全員昇天 | 1つの面で、店内のお客さんを全員昇天させる | Total Knockout | Knock out every customer in the shop in one stage |
| `ice` | ステージ | ★ |  | 氷上の配膳 | 凍りの床がある面をクリアする | Serving on Ice | Clear a stage with an icy floor |
| `raft` | ステージ | ★★ | ○ | 船出 | いかだに乗ったまま料理を届ける | Set Sail | Deliver a dish while riding a raft |
| `air` | ステージ | ★★ | ○ | 空中配達 | 跳んでいる間に料理を届ける | Air Delivery | Deliver a dish in midair |
| `kaikin` | やり込み | ★ |  | 皆勤 | 10ラウンドのラウンドマッチを最後まで遊ぶ | Perfect Attendance | Play a 10-round Round Match to the end |
| `story` | やり込み | ★★ | ○ | 研修修了 | ストーリーを最後までクリアする | Training Complete | Clear the whole story |
| `storyblack` | やり込み | ★★ | ○ | 黒字で修了 | ストーリーを黒字で終える | In the Black | Finish the story in the black |
| `podium` | やり込み | ★★ | ○ | 表彰台のてっぺん | 2人以上のラウンドマッチで1位になる | Top of the Podium | Win a Round Match with 2 or more players |
| `allstage` | やり込み | ★★★ | ○ | 全ステージ制覇 | フリープレイの面をすべて遊ぶ | Stage Master | Play every Free Play stage |

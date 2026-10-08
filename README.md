# ネコ配（仮） Unity版

HTML版（`H:\Source\AI\ClaudeCode\Serving\nekorobo3d`）を Unity へ移しているところです。
**いまは「1面が遊べる」ところまで**（受け取り → 配達 → 結果。NPC との対戦と、同じ画面の4人まで）。

- Unity **6.3 LTS（6000.3.24f1）** / URP / 新 Input System
- 前に作った手触りの試作（`H:\Source\Unity\NekoroboSpike`）の操作とカメラを引き継いでいます。試作は比べる用に残してあります

## 動かし方

`Assets/Scenes/Main.unity` を開いて **Play** を押すだけです。
シーンにあるのは `Game` 1つとカメラと光だけで、床もロボも遊び始めたときにステージのファイルから作ります。
（シーンが無いときは、開いたときに自動で作ります。メニューの「Nekorobo → シーンを作り直す」でも作れます）

始めはストーリーの1面（`stages/course.json` の1つ目）です。`Game` の `courseIndex` で何面目かを、
`stageFile` にファイル名（例 `sample_normal`）を入れると、コースを使わずにその面を遊べます。

**対戦**は右パネル（Tab）の「対戦」で、ルール（個人戦・協力・チーム戦）と相手の NPC（まじめ・普通・暴走・かんたん）を選べます。
4人まで遊ぶときは `Game` の `participants` に HTML版の `?p=` と同じ書き方で並べます
（`key` キーボード、`pad0`〜`pad3` n台目のパッド、`npc-serious` / `npc-normal` / `npc-wild` / `npc-easy`）。
例：`key,pad0,npc-normal,npc-wild`。コースの面（ストーリー）は、course.json の `rivals` のとおりに NPC が付きます。

| 操作 | |
|---|---|
| ← → | 旋回 |
| ↑ ↓ | 前進・バック |
| Space | ジャンプ |
| R | やり直し |
| Tab（⚙） | 検証用パネル（HTML版の右パネルと同じ。調子・カメラ・店舗・数値のつまみ・既定として保存） |
| F2 | 遊びの調子（普通 → 爽快 → カスタム）。既定は**普通**（HTML版のコードに書いてある内蔵の数値） |
| Esc（F1） | 面を選ぶ（コースの面と、stages フォルダの全部のファイル） |
| 結果で Enter ／ N | もう一度 ／ 次の面 |

パッドは HTML版と同じ割り当てです（A/R2 加速、B/L2 バック、X ジャンプ、Y アイテム、L1/R1 持ち替え）。
パッドを枠に割り当てていなければ、どのパッドでもキーボードの人を動かせます。

## データは HTML版のファイルをそのまま読む

**ステージ・数値・絵は、HTML版のフォルダから直接読みます。** コピーはしません。

- `stages/*.json` … HTML版のエディタで作ったステージが、そのまま Unity でも遊べます
- `tune.json` … 右パネルで保存した数値（加速・減衰・吹っ飛び…）。調子を「カスタム」にしたときに効きます
  （いまの tune.json は「爽快」とほぼ同じ値なので、既定は「普通」にしてあります）
- `assets/tex/*.png` と `assets/models.json` の designs … 床・壁・家具の見た目
- `assets/models.json` の models と、そこに書いた `assets/**/*.glb` … 客・家具・車などのモデル。
  glb は Unity の glTF 読み込み（glTFast）で、遊ぶ前に全部読みます。客の配り方（どの席にどの人）も JS版と同じです

置き場所は自動で探します（環境変数 `NEKOROBO_DATA` → `StreamingAssets/nekorobo3d` →
このプロジェクトから見た `../../AI/ClaudeCode/Serving/nekorobo3d` の順）。

## JS版と同じになっているか

- **注文の並び**（どの客に・どの料理が・どの順で）は JS版と同じ乱数を同じ順で使っているので、**同じになります。**
  見本の「せせらぎ定食」で6品とも一致することを確かめました
- 操作のモデル（加速・前後と横の減衰・旋回・ジャンプ）は試作で実測して、最高速のずれが1%未満でした
- 客の見た目（種類・肌・服・髪の色）も同じ乱数で配るので、同じステージなら同じ顔ぶれになります

## 移してある物・まだの物

| | |
|---|---|
| **移した** | 床・壁（低い／高い／見えない）・板の道・道路・線路・橋・凍りの床・水・溶岩・海・穴・受取カウンター・屋台・テーブル・イス・ベンチ・客・開始位置・ジャンプ台・場所ごとの床（zones）・車・動く床・いかだ・ルート（走る物・歩く客）・置き物カタログ（コードで組んだ55種＋catalog.json のモデル425種）・道路の白線・線路・橋の欄干・盤面の外へ伸ばす道・まわりの飾り（地面・木・建物、models.json の sceneryModels）・水と溶岩の流れ・壁の飾り・落ちた料理（拾える・池や時間切れで作り直し）・アイテム8種（バナナ・リペア・ドローン・レーザー・弾道ミサイル・追尾ミサイル・ブーメラン・無敵）と爆風・財布（所持金・強化・持ち物）・演出（飛沫・土ぼこり・戻ってきた光の柱と矢印・客の虹）・NPC（4つの強さ・道探し・体当たり・アイテム）・対戦（個人戦・協力・チーム戦、4人まで、キーボードとパッド4台）・対戦の一覧・順位・ステージボーナス・受け取りと配達・配膳コンボ・客の吹っ飛び・店の損壊・機体の故障（旋回／駆動）・料理の乱れ・大暴れボーナス・池と海と穴・カメラの自動調整・結果（収支・タイム・ベスト） |
| **まだ** | ショップ（NPC の買い物も）・タイトルとメニュー・結果の画面の見た目・ストーリー（会話と案内）・オンライン・音・仕上げの処理（ブルーム・AO） |

まだの物を置いた面は、その物を飛ばして組み立てます（コンソールに「まだ移していない物があります」と1回ずつ出ます）。
stages フォルダの全42面が、エラーなしで組み立てられることを確かめてあります。

## 座標の決まり（ここを間違えると「なんとなく変」になる）

three.js は右手系、Unity は左手系です。**z の符号を反転するだけ**で、画面に映る絵は同じになります。

| | three.js | Unity |
|---|---|---|
| 位置 | (x, y, z) | (x, y, **-z**) |
| 回転 | x軸 θ ／ y軸 θ ／ z軸 θ | **-θ** ／ **-θ** ／ θ |
| ロボ・客・イスの前 | -Z | +Z |
| マス (i, j) の中心 | ((i+.5), 0, (j+.5)) | ((i+.5), 0, **-(j+.5)**) |

決まりは `Scripts/Core/Coord.cs` に集めてあります。見た目の数値（部品の位置・大きさ）は JS版と同じ数字を書き、
`Coord.W(x, y, z)` と `Part.Euler3(x, y, z)` を通して置きます。
図形（`MeshGen`）は three.js と同じ作り方で頂点を作り、z を反転したうえで**三角形の並びを逆にします**
（three.js は反時計回りが表、Unity は時計回りが表。逆にしないと箱の内側が見える）。

試作のときに分かった注意（接地判定・物理の刻み・摩擦の合成）は `NekoroboSpike/README.md` にあります。
こちらでも同じ扱いにしてあります（刻み 1/60 秒、ロボの摩擦は Minimum、接地は機体の中心から下へ撃つ）。

## 中身

| ファイル | |
|---|---|
| `Scripts/Core/Coord.cs` | 座標の決まり。JS版と同じ並びの乱数（`Lcg`） |
| `Scripts/Core/MeshGen.cs` | three.js と同じ図形（箱・円柱・球・輪…） |
| `Scripts/Core/Mats.cs` | 素材と日本語の字 |
| `Scripts/Data/DataRoot.cs` | HTML版のフォルダを探して読む |
| `Scripts/Data/Tune.cs` | 数値（JS版の DEFAULT_TUNE と同じ名前）と tune.json |
| `Scripts/Data/Tiles.cs` | マップチップ・デザイン・客の種類・お店・料理（tiles.js / shops.js / DISHES） |
| `Scripts/Data/Stage.cs` | ステージとコースの読み込み |
| `Scripts/Data/ModelStore.cs` | モデル（glb）の読み込みと差し替え（JS版 loadAssets / applyModel）。models.json の models・dishes・shops |
| `Scripts/Game/Game.cs` | 本体（JS版の G と step）。操作・受け取りと配達・ぶつかり・池・終わり・カメラ |
| `Scripts/Game/Game.Build.cs` | ステージの組み立て（JS版 buildStage） |
| `Scripts/Game/Ent.cs` | 当たり判定を持つ物1つ（JS版の ent） |
| `Scripts/Game/Player.cs` | ロボ1台ぶんの記録と見た目 |
| `Scripts/Game/Game.Objects.cs` | 車・動く床・ルート・歩く客・いかだ・置き物・道路の白線と線路と欄干 |
| `Scripts/Data/Props.cs` | 置き物カタログ（JS版 props.js の表。キーは JS版と同じ） |
| `Scripts/Game/PropLooks.cs` | 置き物の見た目（JS版 props.js の BUILD を写したもの） |
| `Scripts/Game/Looks.cs` | 客・家具・看板の見た目（仮）と、コードで描く絵（水の流れ・壁の飾り） |
| `Scripts/Game/Game.Items.cs` | アイテム・爆風・財布（JS版 ITEMS / UPGRADES と同じ値） |
| `Scripts/Game/Game.Npc.cs` | NPC（JS版 NPC_LV・findPath・driveNpc）。道探しに教えるマス（動く床・いかだ・置き物） |
| `Scripts/Game/Game.Fx.cs` | 演出（飛沫・モクモク・閃光・光の柱・矢印・客の虹）と落ちた料理 |
| `Scripts/Game/Scenery.cs` | ステージのまわりの飾り（JS版 scenery.js） |
| `Scripts/Game/Overhead.cs` | 頭の上の表示（客の HP・ロボの名前と耐久と故障・セリフ・▼Target!） |
| `Scripts/Game/Hud.cs` | 画面の表示（カウントダウン・数字の吹き出し・結果（仮）・面選び） |
| `Scripts/Game/HudTop.cs` | 上の帯（面の名前・店舗ダメージ・NEXT・コンボ・タイム・お金）と右の人ごとの札。JS版 #topbar / #pcards |
| `Scripts/Game/DishPic.cs` | 運んでいる料理の今の状態の絵（JS版 drawDishState） |
| `Scripts/Game/SettingsPanel.cs` | 検証用パネル（JS版の右パネル）。保存すると HTML版の tune.json に書く（Unity が知らない項目は残す） |
| `Scripts/Game/UiKit.cs` | 画面の部品（丸い札・枠・影）。CSS の数字をそのまま写すため |
| `Editor/BackgroundStep.cs` | 確かめる用：Unity が裏にあっても遊びを進める（既定は切） |
| `Editor/NekoroboSetup.cs` | シーンを作る |

## 確かめる用

**新しい .cs ファイルを足したら、先に Unity に読み込ませてからコンパイルする**（外からファイルを作って、
読み込まれる前にコンパイルを頼むと、その後もそのファイルがコンパイルの対象に入らないことがあった）。

**Unity が前に出ていないと、遊んでいる途中でも1フレームも進みません。**
外からコマンドで動かして確かめるときは、メニューの「Nekorobo → 裏でも遊びを進める（確かめる用）」を入にすると、
裏にあるあいだ1フレームずつ送って進めます（Unity を前に戻すと、ふつうの再生に戻ります）。

`Game.autoPlay = true` にすると、受け取り → 配達を自動でくり返します（まっすぐ向かうだけで、池に落ちたら跳んで上がる）。
NPC を移すまでのつなぎです。遊ぶときは使いません。

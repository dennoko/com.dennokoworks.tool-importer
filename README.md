# dennokoworks Tool Importer (BLM連携)

BOOTH Library Manager (BLM) でダウンロードした BOOTH アイテム（`.unitypackage`）を Unity エディタ内から簡単にプロジェクトへインポートするための VPM 対応パッケージです。dennokoworks 製ツールについては最新バージョンの確認もできます。

---

## 主な機能
- **BLM 連携**: BLM のデータ（`%APPDATA%\pm.booth.library-manager\data.db`）とアイテム保存先を自動検出。手動指定も可能
- **ショップ切り替え**: 既定は `dennokoworks`。BLM に登録されているショップから追加したり、サブドメインを直接入力して任意のショップを追加可能
- **アイテム一覧**: サムネイル・導入状況（未導入 / 一部導入 / 導入済み）を表示。名前で絞り込み可能
- **ワンクリックインポート**: 最新の `.unitypackage` をインポート。複数バージョンがある場合は ▼ から選択
- **まとめてインポート**: 「複数選択モード」でチェックしたアイテムを順番にインポート
- **BOOTH 商品ページ**: BLM のアイテム情報から BOOTH の商品ページを開く
- **最新バージョン確認（dennokoworks のみ）**: プロジェクト内のツール・BLM でダウンロード済みのパッケージ・公開中の最新バージョンを比較し、更新があれば通知

---

## 動作環境
- Unity 2022.3 以降
- Windows 10 以降（OS 同梱の `winsqlite3.dll` を使用） / macOS（`/usr/lib/libsqlite3.dylib` を使用）
- BOOTH Library Manager

---

## 使い方
1. Unity メニューの `Tools > dennokoworks > Tool Importer` または `Window > dennokoworks > Tool Importer` を開きます。
2. BLM が自動検出されない場合は、ツールバーの「設定」から `data.db` とアイテム保存先フォルダを指定します。
3. ツールバーのショップ名をクリックして表示するショップを切り替えます。「＋ ショップ追加」でショップを追加・管理できます。
4. 一覧から「インポート」を押すと、Unity のインポートダイアログが開きます。
   - 複数のツールを入れる場合は「複数選択モード」をオンにし、チェックを入れて「まとめてインポート」を押します。1 件ずつ順番にインポートされます（「内容を確認しながら」をオンにすると 1 件ずつダイアログを表示）。
5. dennokoworks 選択時は「最新バージョン確認」で公開中の最新バージョンを確認できます。新しいバージョンがある場合は BOOTH / BLM で最新版をダウンロードし、「一覧を再読み込み」を押してください。

### 補足
- 導入状況は `.unitypackage` 内のアセット GUID がプロジェクトに存在するかで判定します。解析結果は `Library/DennokoworksToolImporter/` にキャッシュされます。
- ショップ一覧と BLM パスの手動指定は `EditorPrefs` に保存され、全プロジェクトで共通です。
- 最新バージョンの問い合わせは「最新バージョン確認」を押したときだけ行います。

---

## パッケージ構成
```
com.dennokoworks.tool-importer/
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE.txt
├── vpm-repository-template.json
├── Docs/Impl/implementation_plan.md
└── Editor/
    ├── Dennokoworks.ToolImporter.Editor.asmdef
    ├── Windows/   # メインウィンドウ・ショップ管理ダイアログ
    ├── Core/      # BLM 読み込み・SQLite・パッケージ解析・インポート・バージョン確認
    └── Models/    # データモデル
```

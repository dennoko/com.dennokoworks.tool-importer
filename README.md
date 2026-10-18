# dennokoworks Tool Importer (BLM連携)

BOOTH Library Manager (BLM) でダウンロードされた dennokoworks 製のUnity拡張ツールをUnityエディタ内から簡単にプロジェクトへインポートし、最新バージョンの確認を行うためのVPM対応パッケージです。

---

## 🌟 主な機能（予定）
- **BLM連携**: BOOTH Library Manager のダウンロードディレクトリを自動検出／手動指定して連携
- **ツール一覧表示**: dennokoworks 製ツールのダウンロード状況およびプロジェクトへの導入状況を可視化
- **ワンクリックインポート**: BLMで取得済みの `.unitypackage` やフォルダをUnityプロジェクト内に直接インポート
- **最新バージョン確認**: 最新アップデートの有無を確認し、更新されたツールを一目で把握・アップデート

---

## 📁 パッケージ構成
```
com.dennokoworks.tool-importer/
├── package.json                          # VPM / UPM パッケージ定義
├── README.md                             # パッケージ概要・ドキュメント
├── CHANGELOG.md                          # 更新履歴
├── LICENSE.txt                           # ライセンス情報
├── vpm-repository-template.json          # VPMリポジトリ公開用テンプレート
└── Editor/
    ├── Dennokoworks.ToolImporter.Editor.asmdef  # Editor用アセンブリ定義
    └── DennokoworksToolImporterWindow.cs         # エディタウィンドウUI
```

---

## 🚀 使い方
1. Unityメニューの `Tools > dennokoworks > Tool Importer` または `Window > dennokoworks > Tool Importer` を開きます。
2. 「BOOTH Library Manager 保存先フォルダ」にBLMのダウンロードフォルダが指定されていることを確認します。
3. 検出されたツール一覧からインポートしたいツールを選択し、インポートを実行します。

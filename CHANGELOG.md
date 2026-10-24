# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- 一覧のサムネイルにマウスオーバーすると、縦横 3 倍の拡大サムネイルを右隣に表示

## [0.2.1] - 2026-10-24
### Changed
- ツール名を **dennokoworks Tool Importer** から **dennokoworks Asset Importer** に変更（あらゆるアセットの導入に適した名称に統一）
- ウィンドウを開くメニュー項目を `dennokoworks > Asset Importer` に集約し、表示優先度を最上位（`-100`）に設定（`Tools > dennokoworks` メニューを削除）

### Fixed
- 言語を切り替えるとアイテム一覧が表示されなくなり、ショップ変更や再読み込みでも回復しない問題を修正（検索欄のラベルが「検索」⇔「Search」に変わる際の変更イベントを検索文字の入力として扱い、絞り込みが効いたままになっていた）
- 一覧の更新後にスクロールすると、画面外にあった行が更新前の内容のまま表示されることがある問題を修正

### Added
- 英語版ユーザーマニュアル（`Docs/user_manual_en.md` / `Docs/user_manual_en.pdf`）を追加
- UI 言語が英語モードの場合、同梱の英語版 PDF マニュアルを優先して開く処理を追加（未作成時は日本語版へフォールバック）
- ユーザーマニュアル（日・英）を最新の UI 構成・機能に合わせて全面改訂（絵文字や特殊装飾記号が「□」として表示される仕様の解説を追加）

## [0.2.0] - 2026-10-24
### Added
- dennokoworks フローティングデザイン（`dennokoworks_color_schema`）に準拠した UI Toolkit (UXML / USS) による UI リメイク
- GitHub リモートリポジトリからのバージョンチェック機能（`version.json` による版数判定、更新通知、手動再確認 `↻`）
- 日本語・英語の多言語ローカライズ対応（ヘッダーの `EN` / `JA` 切替ボタン、システム言語による自動選択）
- ツールバーの「マニュアル」ボタンから同梱の PDF マニュアル（`Docs/user_manual_ja.pdf`）を開く導線
- BLM の `data.db` を OS 同梱 SQLite（読み取り専用）で読み込み、アイテム保存先・ショップ・アイテムを取得
- ショップの切り替え・追加・表示名編集・削除（`EditorPrefs` に保存）。`None` で全ショップのアイテムを表示
- `.unitypackage` が無いアイテムを一覧から除外（除外件数を表示）
- 一覧の仮想スクロール（`ListView` の可変高さ仮想化で表示範囲の行だけ生成）とサムネイルの取得取り消し・上限付きキャッシュ
- `.unitypackage` の再帰探索・バージョン順の並べ替え・複数パッケージの選択インポート（インポートボタンからパッケージ一覧を表示し、チェックボックスで選択）
- `.unitypackage` の GUID 解析による導入状況の判定（`Library/` にキャッシュ）
- dennokoworks 製ツールのプロジェクト内 / BLM 内 / 公開中の最新バージョン比較による更新通知
- サムネイル表示・検索フィルター・BLM 未検出時のガイダンス
- 複数選択モードとまとめてインポート（キューで 1 件ずつ順次インポート。ドメインリロードを挟んでも継続）
- BLM のアイテム情報（商品 ID・ショップ）から BOOTH 商品ページを開くボタン

### Changed
- ウィンドウ実装を IMGUI から UI Toolkit (UXML/USS) に移行し、Personal Light / Dark 両テーマで一貫したフローティングデザインを適用
- ウィンドウを開くメニューを `Window > dennokoworks` からメニューバー直下の `dennokoworks > Tool Importer` に変更
- プレースホルダー表示だったウィンドウを実データに対応させ、`Editor/Windows`・`Editor/Core`・`Editor/Models` に分割

## [0.1.0] - 2026-10-18
### Added
- 初期テンプレート作成
- VPM対応用 `package.json` の作成
- `Dennokoworks.ToolImporter.Editor.asmdef` の追加
- BLM連携・ツール一覧表示用 EditorWindow (`DennokoworksToolImporterWindow`) の骨組み実装
- VPMリポジトリ公開用テンプレート (`vpm-repository-template.json`) の追加

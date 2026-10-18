# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- BLM の `data.db` を OS 同梱 SQLite（読み取り専用）で読み込み、アイテム保存先・ショップ・アイテムを取得
- ショップの切り替え・追加・表示名編集・削除（`EditorPrefs` に保存）。`None` で全ショップのアイテムを表示
- `.unitypackage` が無いアイテムを一覧から除外（除外件数を表示）
- `.unitypackage` の再帰探索・バージョン順の並べ替え・複数パッケージの選択インポート（インポートボタンからパッケージ一覧を表示し、チェックボックスで選択）
- `.unitypackage` の GUID 解析による導入状況の判定（`Library/` にキャッシュ）
- dennokoworks 製ツールのプロジェクト内 / BLM 内 / 公開中の最新バージョン比較による更新通知
- サムネイル表示・検索フィルター・BLM 未検出時のガイダンス
- 複数選択モードとまとめてインポート（キューで 1 件ずつ順次インポート。ドメインリロードを挟んでも継続）
- BLM のアイテム情報（商品 ID・ショップ）から BOOTH 商品ページを開くボタン

### Changed
- プレースホルダー表示だったウィンドウを実データに対応させ、`Editor/Windows`・`Editor/Core`・`Editor/Models` に分割

## [0.1.0] - 2026-10-18
### Added
- 初期テンプレート作成
- VPM対応用 `package.json` の作成
- `Dennokoworks.ToolImporter.Editor.asmdef` の追加
- BLM連携・ツール一覧表示用 EditorWindow (`DennokoworksToolImporterWindow`) の骨組み実装
- VPMリポジトリ公開用テンプレート (`vpm-repository-template.json`) の追加

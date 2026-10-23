using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    public enum Language
    {
        Japanese,
        English
    }

    public static class ToolImporterLoc
    {
        private const string PrefsKey = "Dennokoworks.ToolImporter.Language";
        private static Language? _currentLanguage;

        public static event Action OnLanguageChanged;

        public static Language CurrentLanguage
        {
            get
            {
                if (!_currentLanguage.HasValue)
                {
                    string saved = EditorPrefs.GetString(PrefsKey, string.Empty);
                    if (saved == "en")
                        _currentLanguage = Language.English;
                    else if (saved == "ja")
                        _currentLanguage = Language.Japanese;
                    else
                        _currentLanguage = Application.systemLanguage == SystemLanguage.Japanese
                            ? Language.Japanese
                            : Language.English;
                }
                return _currentLanguage.Value;
            }
            set
            {
                if (_currentLanguage == value) return;
                _currentLanguage = value;
                EditorPrefs.SetString(PrefsKey, value == Language.English ? "en" : "ja");
                OnLanguageChanged?.Invoke();
            }
        }

        public static string Tr(string key, params object[] args)
        {
            var dict = CurrentLanguage == Language.English ? StringsEn : StringsJa;
            if (!dict.TryGetValue(key, out string text))
            {
                if (!StringsJa.TryGetValue(key, out text))
                    text = key;
            }

            if (args != null && args.Length > 0)
            {
                try { return string.Format(text, args); }
                catch { return text; }
            }
            return text;
        }

        private static readonly Dictionary<string, string> StringsJa = new Dictionary<string, string>
        {
            // ヘッダー
            { "WindowTitle", "dennokoworks Importer" },
            { "HeaderTitle", "dennokoworks Importer" },
            { "LangButton", "EN" },
            { "LangButtonTooltip", "英語に切り替えます" },
            { "ReloadVersionTooltip", "アップデートを再確認" },
            { "VerChecking", "確認中..." },
            { "VerUpdateAvailable", "更新あり {0}" },
            { "VerError", "最新版を取得できません" },
            { "ShopLabel", "ショップ:" },
            { "AllShops", "すべてのショップ" },
            { "ManageShops", "ショップを追加・管理..." },
            { "AddShop", "＋ ショップ追加" },
            { "Manual", "マニュアル" },
            { "ManualTooltip", "使い方マニュアル（PDF）を開きます" },
            { "Settings", "設定" },

            // BLM 設定
            { "BlmSettingsTitle", "BOOTH Library Manager 設定" },
            { "DbPathLabel", "BLM データ (data.db)" },
            { "ItemDirLabel", "アイテム保存先" },
            { "Browse", "参照..." },
            { "Auto", "自動" },
            { "AutoTooltip", "手動指定を解除して自動検出に戻す" },
            { "ManualStatus", "手動指定" },
            { "AutoStatus", "自動検出" },
            { "SelectDbDialogTitle", "BLM の data.db を選択" },
            { "SelectDirDialogTitle", "BLM のアイテム保存先フォルダを選択" },
            { "ClearCache", "パッケージ解析キャッシュを削除" },
            { "NotDetected", "(未検出)" },

            // 操作バー
            { "CheckAllRemote", "最新バージョン確認" },
            { "CheckAllRemoteChecking", "確認中..." },
            { "ReloadList", "一覧を再読み込み" },
            { "SearchLabel", "検索" },
            { "SearchPlaceholder", "アイテム名・ショップ名で絞り込み..." },
            { "SearchTooltip", "アイテム名・ショップ名で絞り込み" },
            { "MultiSelectMode", "複数選択モード" },

            // 一括操作バー
            { "SelectAll", "すべて選択" },
            { "SelectUpdates", "更新ありを選択" },
            { "SelectUpdatesTooltip", "BLM 内に新しいバージョンがあるツールを選択" },
            { "DeselectAll", "選択解除" },
            { "BatchInteractive", "内容を確認しながら" },
            { "BatchInteractiveTooltip", "オンにすると 1 件ずつインポートダイアログを表示します" },
            { "BatchImport", "まとめてインポート ({0} 件)" },

            // キュー進行状況
            { "QueueImporting", "インポート中 ({0}/{1}): {2}" },
            { "QueueWaiting", "次のインポートを待機中 ({0}/{1})" },
            { "QueuePendingCount", "残り {0} 件" },
            { "QueueSkipCurrent", "現在の項目をスキップ" },
            { "QueueSkipCurrentTooltip", "インポートが終わらない場合に次へ進みます" },
            { "QueueCancelPending", "残りを中止" },

            // アイテム一覧
            { "ItemListHeader", "アイテム一覧 ({0}): {1} 件" },
            { "ScanningCount", "パッケージ解析中 ({0})" },
            { "HiddenItemsTooltip", ".unitypackage が見つからないアイテム {0} 件は表示していません（未ダウンロード・Unity 用以外など）。" },
            { "HiddenArchivesTooltip", "\nうち {0} 件は zip が未展開の可能性があります。BLM で展開してから「一覧を再読み込み」を押してください。" },
            { "NoItemsMessage", "{0}インポートできるアイテム（.unitypackage）がありません。\nBLM でアイテムを追加・ダウンロードしてから「一覧を再読み込み」を押してください。" },
            { "InBlm", "BLM に" },
            { "InBlmShop", "BLM の「{0}」に" },
            { "BatchTargetsLabel", "まとめてインポート対象: {0}" },
            { "NewVersionAvailable", "新しいバージョン {0} があります。BLM でダウンロードしてください。" },
            { "OpenBoothItemPage", "BOOTH の商品ページを開く" },
            { "BoothButton", "BOOTH" },
            { "FolderButton", "フォルダ" },
            { "NoPackage", "パッケージなし" },
            { "Import", "インポート" },
            { "ImportUpdate", "更新をインポート" },
            { "Reimport", "再インポート" },
            { "SelectPackagesDropdown", "含まれるパッケージ（{0} 件）から選んでインポート" },

            // その他の導入済みツール
            { "OtherLocalToolsTitle", "その他の導入済みツール" },
            { "OtherToolUpdateAvailable", "新しいバージョン {0} があります。BOOTH で最新版を入手してください。" },
            { "PingButton", "選択" },

            // フッター
            { "OpenBoothShop", "BOOTH ショップを開く ({0})" },
            { "StatusReady", "Ready" },

            // エラー・ダイアログ
            { "OsNotSupported", "この OS では BOOTH Library Manager との連携に対応していません（Windows / macOS のみ）。" },
            { "DbNotFoundDefault", "BOOTH Library Manager のデータが見つかりませんでした。\nBLM をインストールして一度起動するか、「設定」から data.db の場所を指定してください。" },
            { "DbNotFoundManual", "指定された data.db が見つかりません。\n{0}" },
            { "DbReadError", "BLM のデータベースを読み込めませんでした。\n{0}" },
            { "ItemDirNotFound", "BLM のアイテム保存先フォルダが見つかりません。「設定」から指定してください。\n{0}" },
            { "NoLocalToolsFound", "version.json を持つ dennokoworks 製ツールがプロジェクト内に見つかりませんでした。\n（Assets/dennokoworks/[ツール]/version.json を確認します）" },
            { "ManualNotFound", "マニュアルが見つかりませんでした。\n{0}" },

            // ポップアップ
            { "PopupPrompt", "インポートするパッケージを選択してください" },
            { "PopupSelectedCount", "{0} 件選択中" },
            { "PopupMultiSequentialHint", "複数選んだ場合は古い順に 1 件ずつインポートします" },
            { "PopupSetBatchButton", "まとめてインポート対象に設定" },
            { "PopupSetBatchTooltip", "チェックしたパッケージを「まとめてインポート」の対象にします" },
            { "PopupImportButton", "インポート ({0} 件)" },

            // ショップ管理
            { "ShopManageTitle", "ショップの管理" },
            { "ShopManageDefaultShop", "起動時のデフォルトショップ" },
            { "ShopManageRegisteredShops", "登録済みショップ" },
            { "ShopManageBlmShops", "BLM からショップを追加" },
            { "ShopManageManualInput", "サブドメイン直接入力で追加" },
            { "ShopManageClose", "閉じる" },
            { "ShopManageDefaultLabel", "デフォルト:" },
            { "ShopManageDefaultAll", "None（すべてのショップを表示）" },
            { "PopupLatestSuffix", "  （最新）" },
            { "ShopManageReopen", "ツールウィンドウから開き直してください。" },
            { "ShopManageDefaultBadge", "[既定]" },
            { "ShopManageSetDefault", "既定" },
            { "ShopManageOpen", "開く" },
            { "ShopManageDelete", "削除" },
            { "ShopManageDeleteTitle", "ショップの削除" },
            { "ShopManageDeleteMessage", "「{0}」を一覧から削除しますか？" },
            { "ShopManageDeleteOk", "削除" },
            { "Cancel", "キャンセル" },
            { "ShopManageNameHint", "表示名は入力後 Enter で保存されます。dennokoworks は登録解除できません。" },
            { "ShopManageNoBlmShops", "BLM にアイテムが登録されたショップが見つかりませんでした。" },
            { "ShopManageItemCount", "{0} 件" },
            { "ShopManageRegistered", "登録済み" },
            { "ShopManageAdd", "追加" },
            { "ShopManageSubdomain", "サブドメイン" },
            { "ShopManageSubdomainTooltip", "例: dennokoworks（https://dennokoworks.booth.pm/ の URL も可）" },
            { "ShopManageDisplayName", "表示名（任意）" }
        };

        private static readonly Dictionary<string, string> StringsEn = new Dictionary<string, string>
        {
            // Header
            { "WindowTitle", "dennokoworks Importer" },
            { "HeaderTitle", "dennokoworks Importer" },
            { "LangButton", "JA" },
            { "LangButtonTooltip", "Switch to Japanese" },
            { "ReloadVersionTooltip", "Recheck for updates" },
            { "VerChecking", "Checking..." },
            { "VerUpdateAvailable", "Update {0}" },
            { "VerError", "Failed to check version" },
            { "ShopLabel", "Shop:" },
            { "AllShops", "All Shops" },
            { "ManageShops", "Add & Manage Shops..." },
            { "AddShop", "＋ Add Shop" },
            { "Manual", "Manual" },
            { "ManualTooltip", "Open user manual (PDF)" },
            { "Settings", "Settings" },

            // BLM Settings
            { "BlmSettingsTitle", "BOOTH Library Manager Settings" },
            { "DbPathLabel", "BLM Data (data.db)" },
            { "ItemDirLabel", "Item Directory" },
            { "Browse", "Browse..." },
            { "Auto", "Auto" },
            { "AutoTooltip", "Reset manual override to auto-detection" },
            { "ManualStatus", "Manual override" },
            { "AutoStatus", "Auto-detected" },
            { "SelectDbDialogTitle", "Select BLM data.db" },
            { "SelectDirDialogTitle", "Select BLM Item Directory" },
            { "ClearCache", "Clear Package Cache" },
            { "NotDetected", "(Not detected)" },

            // Action Bar
            { "CheckAllRemote", "Check Latest Versions" },
            { "CheckAllRemoteChecking", "Checking..." },
            { "ReloadList", "Reload List" },
            { "SearchLabel", "Search" },
            { "SearchPlaceholder", "Filter by item or shop name..." },
            { "SearchTooltip", "Filter by item or shop name" },
            { "MultiSelectMode", "Multi-select Mode" },

            // Batch Action Bar
            { "SelectAll", "Select All" },
            { "SelectUpdates", "Select Updates" },
            { "SelectUpdatesTooltip", "Select tools with newer versions in BLM" },
            { "DeselectAll", "Deselect All" },
            { "BatchInteractive", "Confirm Step-by-Step" },
            { "BatchInteractiveTooltip", "Shows import dialog for each package when enabled" },
            { "BatchImport", "Batch Import ({0} items)" },

            // Queue Status
            { "QueueImporting", "Importing ({0}/{1}): {2}" },
            { "QueueWaiting", "Waiting for next import ({0}/{1})" },
            { "QueuePendingCount", "{0} items remaining" },
            { "QueueSkipCurrent", "Skip Current Item" },
            { "QueueSkipCurrentTooltip", "Proceed to next if import does not complete" },
            { "QueueCancelPending", "Cancel Remaining" },

            // Item List
            { "ItemListHeader", "Item List ({0}): {1} items" },
            { "ScanningCount", "Analyzing packages ({0})" },
            { "HiddenItemsTooltip", "{0} items without .unitypackage are hidden (not downloaded, not for Unity, etc.)." },
            { "HiddenArchivesTooltip", "\n{0} items may contain unextracted zip files. Please extract in BLM and reload." },
            { "NoItemsMessage", "No importable items (.unitypackage) found in {0}.\nPlease add and download items in BLM, then click 'Reload List'." },
            { "InBlm", "BLM" },
            { "InBlmShop", "BLM '{0}'" },
            { "BatchTargetsLabel", "Batch import targets: {0}" },
            { "NewVersionAvailable", "New version {0} is available. Please download in BLM." },
            { "OpenBoothItemPage", "Open BOOTH Product Page" },
            { "BoothButton", "BOOTH" },
            { "FolderButton", "Folder" },
            { "NoPackage", "No Package" },
            { "Import", "Import" },
            { "ImportUpdate", "Import Update" },
            { "Reimport", "Re-import" },
            { "SelectPackagesDropdown", "Select from included packages ({0})" },

            // Other Local Tools
            { "OtherLocalToolsTitle", "Other Installed Tools" },
            { "OtherToolUpdateAvailable", "New version {0} is available. Please obtain it from BOOTH." },
            { "PingButton", "Select" },

            // Footer
            { "OpenBoothShop", "Open BOOTH Shop ({0})" },
            { "StatusReady", "Ready" },

            // Errors & Dialogs
            { "OsNotSupported", "BOOTH Library Manager integration is not supported on this OS (Windows / macOS only)." },
            { "DbNotFoundDefault", "BOOTH Library Manager data not found.\nPlease install and launch BLM once, or specify data.db path in Settings." },
            { "DbNotFoundManual", "Specified data.db not found.\n{0}" },
            { "DbReadError", "Failed to read BLM database.\n{0}" },
            { "ItemDirNotFound", "BLM item directory not found. Please specify in Settings.\n{0}" },
            { "NoLocalToolsFound", "No dennokoworks tools with version.json found in this project.\n(Checking Assets/dennokoworks/[Tool]/version.json)" },
            { "ManualNotFound", "Manual not found.\n{0}" },

            // Popup
            { "PopupPrompt", "Select packages to import" },
            { "PopupSelectedCount", "{0} selected" },
            { "PopupMultiSequentialHint", "Multiple packages will be imported sequentially from oldest to newest" },
            { "PopupSetBatchButton", "Set as Batch Import Target" },
            { "PopupSetBatchTooltip", "Sets checked packages as target for batch import" },
            { "PopupImportButton", "Import ({0} items)" },

            // Shop Management
            { "ShopManageTitle", "Manage Shops" },
            { "ShopManageDefaultShop", "Startup Default Shop" },
            { "ShopManageRegisteredShops", "Registered Shops" },
            { "ShopManageBlmShops", "Add Shop from BLM" },
            { "ShopManageManualInput", "Add by Subdomain" },
            { "ShopManageClose", "Close" },
            { "ShopManageDefaultLabel", "Default:" },
            { "ShopManageDefaultAll", "None (Show All Shops)" },
            { "PopupLatestSuffix", "  (Latest)" },
            { "ShopManageReopen", "Please reopen this dialog from the Tool Importer window." },
            { "ShopManageDefaultBadge", "[Default]" },
            { "ShopManageSetDefault", "Default" },
            { "ShopManageOpen", "Open" },
            { "ShopManageDelete", "Delete" },
            { "ShopManageDeleteTitle", "Remove Shop" },
            { "ShopManageDeleteMessage", "Remove '{0}' from registered shops?" },
            { "ShopManageDeleteOk", "Remove" },
            { "Cancel", "Cancel" },
            { "ShopManageNameHint", "Display names are saved on pressing Enter. dennokoworks cannot be removed." },
            { "ShopManageNoBlmShops", "No shops with registered items found in BLM." },
            { "ShopManageItemCount", "{0} items" },
            { "ShopManageRegistered", "Added" },
            { "ShopManageAdd", "Add" },
            { "ShopManageSubdomain", "Subdomain" },
            { "ShopManageSubdomainTooltip", "e.g. dennokoworks (URL is also supported)" },
            { "ShopManageDisplayName", "Display Name (Optional)" }
        };
    }
}

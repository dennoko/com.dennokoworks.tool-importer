using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    public class DennokoworksToolImporterWindow : EditorWindow
    {
        private const string WindowTitle = "dennokoworks Importer";
        private const string ShowSettingsPrefKey = "Dennokoworks.ToolImporter.ShowSettings";
        private const float ThumbnailSize = 64f;
        private const float ButtonWidth = 150f;

        private static readonly Color ImportColor = new Color(0.4f, 0.8f, 1f);
        private static readonly Color UpdateColor = new Color(1f, 0.6f, 0.2f);

        private ShopManager _shopManager;

        private string _dbPath;
        private string _itemRoot;
        private string _loadError;
        private MessageType _loadErrorType = MessageType.Warning;

        private readonly List<ToolItemState> _items = new List<ToolItemState>();
        private readonly List<LocalToolVersion> _localTools = new List<LocalToolVersion>();
        private readonly List<LocalToolVersion> _localOnlyTools = new List<LocalToolVersion>();

        private const string MultiSelectPrefKey = "Dennokoworks.ToolImporter.MultiSelect";
        private const string BatchInteractivePrefKey = "Dennokoworks.ToolImporter.BatchInteractive";

        private bool _statesDirty = true;
        private bool _showSettings;
        private bool _multiSelect;
        private bool _batchInteractive;
        /// <summary>複数選択モードで選択中のアイテム（RegisteredId → インポートするパッケージのパス）。</summary>
        private readonly Dictionary<string, string> _selection = new Dictionary<string, string>();
        private string _search = "";
        private Vector2 _scrollPosition;

        private GUIStyle _itemNameStyle;
        private GUIStyle _wrappedMiniStyle;

        [MenuItem("Tools/dennokoworks/Tool Importer", false, 100)]
        [MenuItem("Window/dennokoworks/Tool Importer", false, 2000)]
        public static void Open()
        {
            var window = GetWindow<DennokoworksToolImporterWindow>(WindowTitle);
            window.minSize = new Vector2(480, 400);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            _showSettings = EditorPrefs.GetBool(ShowSettingsPrefKey, false);
            _multiSelect = EditorPrefs.GetBool(MultiSelectPrefKey, false);
            _batchInteractive = EditorPrefs.GetBool(BatchInteractivePrefKey, false);

            _shopManager = new ShopManager();
            _shopManager.Changed += Reload;

            PackageImporter.ScanCompleted += MarkStatesDirty;
            ThumbnailCache.Loaded += Repaint;
            ImportQueue.Changed += Repaint;
            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
            EditorApplication.projectChanged += MarkStatesDirty;

            Reload();
        }

        private void OnDisable()
        {
            if (_shopManager != null) _shopManager.Changed -= Reload;
            PackageImporter.ScanCompleted -= MarkStatesDirty;
            ThumbnailCache.Loaded -= Repaint;
            ImportQueue.Changed -= Repaint;
            AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
            EditorApplication.projectChanged -= MarkStatesDirty;
        }

        private void OnImportPackageCompleted(string packageName) => MarkStatesDirty();

        private void MarkStatesDirty()
        {
            _statesDirty = true;
            Repaint();
        }

        private bool IsDennokoworks => _shopManager.IsDefaultShopSelected;

        #region Data loading

        /// <summary>BLM の DB からアイテム一覧を読み直し、パッケージを探索する。</summary>
        private void Reload()
        {
            _items.Clear();
            _loadError = null;
            _loadErrorType = MessageType.Warning;
            _dbPath = null;
            _itemRoot = null;

            if (!SqliteReader.IsSupported)
            {
                _loadError = "この OS では BOOTH Library Manager との連携に対応していません（Windows / macOS のみ）。";
                _loadErrorType = MessageType.Error;
                MarkStatesDirty();
                return;
            }

            _dbPath = BlmDataAccessor.ResolveDatabasePath();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath))
            {
                _loadError = string.IsNullOrEmpty(BlmDataAccessor.DbPathOverride)
                    ? "BOOTH Library Manager のデータが見つかりませんでした。\nBLM をインストールして一度起動するか、「設定」から data.db の場所を指定してください。"
                    : $"指定された data.db が見つかりません。\n{_dbPath}";
                _showSettings = true;
                MarkStatesDirty();
                return;
            }

            List<BlmItem> items;
            try
            {
                _itemRoot = BlmDataAccessor.ResolveItemDirectory(_dbPath);
                items = BlmDataAccessor.GetItemsByShop(_dbPath, _itemRoot, _shopManager.CurrentShop.subdomain);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _loadError = $"BLM のデータベースを読み込めませんでした。\n{e.Message}";
                _loadErrorType = MessageType.Error;
                MarkStatesDirty();
                return;
            }

            if (string.IsNullOrEmpty(_itemRoot) || !Directory.Exists(_itemRoot))
            {
                _loadError = $"BLM のアイテム保存先フォルダが見つかりません。「設定」から指定してください。\n{_itemRoot}";
                _showSettings = true;
            }

            foreach (var item in items)
            {
                var state = new ToolItemState { Item = item };
                if (!string.IsNullOrEmpty(item.ItemDirectory) && Directory.Exists(item.ItemDirectory))
                {
                    state.DirectoryExists = true;
                    state.Packages = PackageImporter.FindUnityPackages(item.ItemDirectory);
                    state.HasArchive = HasArchive(item.ItemDirectory);
                }
                _items.Add(state);
            }

            PruneSelection();
            MarkStatesDirty();
        }

        /// <summary>一覧に無くなったアイテム・存在しなくなったパッケージを選択から外す。</summary>
        private void PruneSelection()
        {
            var valid = new Dictionary<string, string>();
            foreach (var state in _items)
            {
                if (!_selection.TryGetValue(state.Item.RegisteredId, out var path)) continue;
                if (state.Packages.Exists(p => p.Path == path)) valid[state.Item.RegisteredId] = path;
            }
            _selection.Clear();
            foreach (var kv in valid) _selection[kv.Key] = kv.Value;
        }

        private void SetSelected(ToolItemState state, bool selected)
        {
            if (selected && state.LatestPackage != null)
            {
                if (!_selection.ContainsKey(state.Item.RegisteredId))
                    _selection[state.Item.RegisteredId] = state.LatestPackage.Path;
            }
            else
            {
                _selection.Remove(state.Item.RegisteredId);
            }
        }

        private bool HasUpdate(ToolItemState state)
        {
            return IsDennokoworks && state.LocalTool != null
                && DennokoVersionChecker.IsNewer(state.BlmVersion, state.LocalTool.Version);
        }

        /// <summary>選択中のアイテムを一覧の表示順でキューに積む。</summary>
        private void EnqueueSelection()
        {
            var entries = new List<ImportQueue.Entry>();
            foreach (var state in _items)
            {
                if (!_selection.TryGetValue(state.Item.RegisteredId, out var path)) continue;
                var package = state.Packages.Find(p => p.Path == path);
                var version = package?.FileVersion;
                entries.Add(new ImportQueue.Entry
                {
                    label = string.IsNullOrEmpty(version) ? state.Item.Name : $"{state.Item.Name} ({DennokoVersionChecker.Display(version)})",
                    path = path,
                });
            }
            if (entries.Count == 0) return;

            ImportQueue.Enqueue(entries, _batchInteractive);
            _selection.Clear();
        }

        private static bool HasArchive(string directory)
        {
            try
            {
                return Directory.GetFiles(directory, "*.zip", SearchOption.AllDirectories).Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>導入状態・ローカルバージョン・BLM 内バージョンを再計算する。</summary>
        private void RefreshStates()
        {
            _statesDirty = false;

            _localTools.Clear();
            _localOnlyTools.Clear();
            if (IsDennokoworks) _localTools.AddRange(DennokoVersionChecker.FindLocalTools());

            var matchedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var state in _items)
            {
                state.LocalTool = null;
                state.BlmVersion = null;
                state.InstalledRatio = 0f;

                var latest = state.LatestPackage;
                if (latest == null)
                {
                    state.InstallState = InstallState.NoPackage;
                    continue;
                }

                var scan = PackageImporter.GetScanResult(latest);
                state.InstallState = PackageImporter.EvaluateInstallState(scan, out state.InstalledRatio);
                state.BlmVersion = latest.FileVersion;

                if (!IsDennokoworks || scan == null || !scan.IsValid) continue;

                foreach (var folder in scan.dennokoToolFolders)
                {
                    var local = _localTools.Find(t => string.Equals(t.ToolFolder, folder, StringComparison.OrdinalIgnoreCase));
                    if (local == null) continue;
                    state.LocalTool = local;
                    matchedFolders.Add(local.ToolFolder);
                    break;
                }

                // パッケージ内 version.json を優先（ローカルと同じツールのものがあればそれ）
                EmbeddedVersion embedded = null;
                foreach (var v in scan.versions)
                {
                    if (state.LocalTool != null && string.Equals(v.toolFolder, state.LocalTool.ToolFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        embedded = v;
                        break;
                    }
                    if (embedded == null) embedded = v;
                }
                if (embedded != null) state.BlmVersion = embedded.version;
            }

            foreach (var tool in _localTools)
            {
                if (!matchedFolders.Contains(tool.ToolFolder)) _localOnlyTools.Add(tool);
            }
        }

        private void CheckAllRemoteVersions()
        {
            int requested = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in _localTools)
            {
                if (!tool.HasGitHubRepo || !seen.Add($"{tool.GitHubOwner}/{tool.GitHubRepo}")) continue;
                DennokoVersionChecker.CheckAsync(tool.GitHubOwner, tool.GitHubRepo, tool.ToolFolder, _ => Repaint());
                requested++;
            }

            if (requested == 0)
            {
                EditorUtility.DisplayDialog("最新バージョン確認",
                    "version.json を持つ dennokoworks 製ツールがプロジェクト内に見つかりませんでした。\n（Assets/dennokoworks/[ツール]/version.json を確認します）", "OK");
            }
        }

        private void OpenShopManageDialog()
        {
            List<BlmShop> shops = null;
            string error = null;
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath))
            {
                error = "BLM のデータベースが見つからないため、ショップ候補を表示できません。サブドメインを直接入力してください。";
            }
            else
            {
                try
                {
                    shops = BlmDataAccessor.GetShopsWithItems(_dbPath);
                }
                catch (Exception e)
                {
                    error = $"BLM のショップ一覧を読み込めませんでした: {e.Message}";
                }
            }
            ShopManageDialog.Open(_shopManager, shops, error);
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            if (_shopManager == null) return;
            if (_statesDirty && Event.current.type == EventType.Layout) RefreshStates();
            EnsureStyles();

            DrawHeader();
            if (_showSettings) DrawBlmSettings();
            DrawLoadError();
            DrawImportQueueStatus();
            DrawActionBar();
            if (_multiSelect) DrawBatchBar();
            DrawItemList();
            DrawFooter();
        }

        private void EnsureStyles()
        {
            if (_itemNameStyle != null) return;
            _itemNameStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
            _wrappedMiniStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("ショップ:", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));

                var current = _shopManager.CurrentShop;
                if (GUILayout.Button(current.Label, EditorStyles.toolbarDropDown, GUILayout.MinWidth(140)))
                {
                    var menu = new GenericMenu();
                    foreach (var shop in _shopManager.Shops)
                    {
                        var subdomain = shop.subdomain;
                        menu.AddItem(new GUIContent($"{shop.Label}  ({shop.subdomain})"),
                            string.Equals(subdomain, current.subdomain, StringComparison.OrdinalIgnoreCase),
                            () => _shopManager.Select(subdomain));
                    }
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("ショップを追加・管理..."), false, OpenShopManageDialog);
                    menu.ShowAsContext();
                }

                if (GUILayout.Button("＋ ショップ追加", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                    OpenShopManageDialog();

                GUILayout.FlexibleSpace();

                EditorGUI.BeginChangeCheck();
                _showSettings = GUILayout.Toggle(_showSettings, "設定", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
                if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(ShowSettingsPrefKey, _showSettings);
            }
        }

        private void DrawBlmSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("BOOTH Library Manager 設定", EditorStyles.boldLabel);

                DrawPathSetting(
                    "BLM データ (data.db)",
                    BlmDataAccessor.DbPathOverride,
                    _dbPath,
                    () =>
                    {
                        var dir = string.IsNullOrEmpty(_dbPath) ? "" : Path.GetDirectoryName(_dbPath);
                        return EditorUtility.OpenFilePanel("BLM の data.db を選択", dir, "db");
                    },
                    v => BlmDataAccessor.DbPathOverride = v);

                DrawPathSetting(
                    "アイテム保存先",
                    BlmDataAccessor.ItemDirectoryOverride,
                    _itemRoot,
                    () => EditorUtility.OpenFolderPanel("BLM のアイテム保存先フォルダを選択", _itemRoot ?? "", ""),
                    v => BlmDataAccessor.ItemDirectoryOverride = v);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("パッケージ解析キャッシュを削除", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        PackageImporter.ClearCache();
                        MarkStatesDirty();
                    }
                }
            }
        }

        private void DrawPathSetting(string label, string overrideValue, string resolvedValue, Func<string> browse, Action<string> setOverride)
        {
            bool isManual = !string.IsNullOrEmpty(overrideValue);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(130));
                EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(resolvedValue) ? "(未検出)" : resolvedValue,
                    EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (GUILayout.Button("参照...", EditorStyles.miniButtonLeft, GUILayout.Width(52)))
                {
                    var selected = browse();
                    if (!string.IsNullOrEmpty(selected))
                    {
                        setOverride(selected);
                        // OnGUI の途中でリストを入れ替えないよう、次フレームで再読み込み
                        EditorApplication.delayCall += Reload;
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUI.DisabledScope(!isManual))
                {
                    if (GUILayout.Button(new GUIContent("自動", "手動指定を解除して自動検出に戻す"), EditorStyles.miniButtonRight, GUILayout.Width(40)))
                    {
                        setOverride("");
                        EditorApplication.delayCall += Reload;
                    }
                }
            }
            EditorGUILayout.LabelField(isManual ? "手動指定" : "自動検出", EditorStyles.miniLabel);
        }

        private void DrawLoadError()
        {
            if (string.IsNullOrEmpty(_loadError)) return;
            EditorGUILayout.HelpBox(_loadError, _loadErrorType);
        }

        private void DrawActionBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (IsDennokoworks)
                {
                    bool checking = IsAnyRemoteChecking();
                    using (new EditorGUI.DisabledScope(checking))
                    {
                        if (GUILayout.Button(checking ? "確認中..." : "最新バージョン確認", GUILayout.Height(24)))
                            CheckAllRemoteVersions();
                    }
                }

                if (GUILayout.Button("一覧を再読み込み", GUILayout.Height(24)))
                {
                    EditorApplication.delayCall += Reload;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField("検索", _search);

                EditorGUI.BeginChangeCheck();
                _multiSelect = EditorGUILayout.ToggleLeft("複数選択モード", _multiSelect, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetBool(MultiSelectPrefKey, _multiSelect);
                    if (!_multiSelect) _selection.Clear();
                }
            }
            EditorGUILayout.Space(2);
        }

        /// <summary>複数選択モード時の一括操作バー。</summary>
        private void DrawBatchBar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("すべて選択", EditorStyles.miniButtonLeft, GUILayout.ExpandWidth(false)))
                    {
                        foreach (var state in _items)
                        {
                            if (MatchesSearch(state.Item.Name)) SetSelected(state, true);
                        }
                    }
                    if (IsDennokoworks && GUILayout.Button(new GUIContent("更新ありを選択", "BLM 内に新しいバージョンがあるツールを選択"), EditorStyles.miniButtonMid, GUILayout.ExpandWidth(false)))
                    {
                        foreach (var state in _items)
                        {
                            if (HasUpdate(state)) SetSelected(state, true);
                        }
                    }
                    if (GUILayout.Button("選択解除", EditorStyles.miniButtonRight, GUILayout.ExpandWidth(false)))
                        _selection.Clear();

                    GUILayout.FlexibleSpace();

                    EditorGUI.BeginChangeCheck();
                    _batchInteractive = EditorGUILayout.ToggleLeft(
                        new GUIContent("内容を確認しながら", "オンにすると 1 件ずつインポートダイアログを表示します"),
                        _batchInteractive, GUILayout.Width(120));
                    if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(BatchInteractivePrefKey, _batchInteractive);
                }

                using (new EditorGUI.DisabledScope(_selection.Count == 0 || ImportQueue.IsBusy))
                {
                    var prevColor = GUI.backgroundColor;
                    GUI.backgroundColor = ImportColor;
                    if (GUILayout.Button($"まとめてインポート ({_selection.Count} 件)", GUILayout.Height(24)))
                        EnqueueSelection();
                    GUI.backgroundColor = prevColor;
                }
            }
        }

        /// <summary>まとめてインポートの進行状況・結果。</summary>
        private void DrawImportQueueStatus()
        {
            if (ImportQueue.IsBusy)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    int total = Math.Max(ImportQueue.Total, 1);
                    int processed = ImportQueue.Processed;
                    var current = ImportQueue.CurrentLabel;
                    var text = current != null
                        ? $"インポート中 ({processed + 1}/{total}): {current}"
                        : $"次のインポートを待機中 ({processed}/{total})";
                    var rect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(rect, (float)processed / total, text);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"残り {ImportQueue.PendingCount} 件", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(current == null))
                        {
                            if (GUILayout.Button(new GUIContent("現在の項目をスキップ", "インポートが終わらない場合に次へ進みます"), EditorStyles.miniButtonLeft, GUILayout.ExpandWidth(false)))
                                ImportQueue.SkipCurrent();
                        }
                        using (new EditorGUI.DisabledScope(ImportQueue.PendingCount == 0))
                        {
                            if (GUILayout.Button("残りを中止", EditorStyles.miniButtonRight, GUILayout.ExpandWidth(false)))
                                ImportQueue.CancelPending();
                        }
                    }
                }
                return;
            }

            var summary = ImportQueue.LastSummary;
            if (string.IsNullOrEmpty(summary)) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(summary, summary.Contains("失敗: ") ? MessageType.Warning : MessageType.Info);
                if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(22)))
                    ImportQueue.ClearSummary();
            }
        }

        private bool IsAnyRemoteChecking()
        {
            foreach (var tool in _localTools)
            {
                if (!tool.HasGitHubRepo) continue;
                var r = DennokoVersionChecker.GetCachedResult(tool.GitHubOwner, tool.GitHubRepo);
                if (r != null && r.state == RemoteVersionState.Checking) return true;
            }
            return false;
        }

        private void DrawItemList()
        {
            var shopLabel = _shopManager.CurrentShop.Label;
            int scanning = PackageImporter.PendingCount;
            var header = $"アイテム一覧 ({shopLabel}): {_items.Count} 件";
            if (scanning > 0) header += $"  - パッケージ解析中 ({scanning})";
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scroll.scrollPosition;

                if (_items.Count == 0 && string.IsNullOrEmpty(_loadError))
                {
                    EditorGUILayout.HelpBox(
                        $"BLM に「{shopLabel}」のアイテムが登録されていません。\nBLM でアイテムを追加・ダウンロードしてから「一覧を再読み込み」を押してください。",
                        MessageType.Info);
                }

                foreach (var state in _items)
                {
                    if (!MatchesSearch(state.Item.Name)) continue;
                    DrawItem(state);
                }

                if (IsDennokoworks && _localOnlyTools.Count > 0) DrawLocalOnlyTools();
            }
        }

        private bool MatchesSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(_search)) return true;
            return (text ?? "").IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawItem(ToolItemState state)
        {
            var item = state.Item;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (_multiSelect)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(18)))
                    {
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(state.LatestPackage == null))
                        {
                            bool selected = _selection.ContainsKey(item.RegisteredId);
                            bool newSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(16));
                            if (newSelected != selected) SetSelected(state, newSelected);
                        }
                        GUILayout.FlexibleSpace();
                    }
                }

                DrawThumbnail(item.ThumbnailUrl);

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(item.Name, _itemNameStyle);
                    EditorGUILayout.LabelField(GetStatusText(state), _wrappedMiniStyle);

                    if (_multiSelect
                        && _selection.TryGetValue(item.RegisteredId, out var selectedPath)
                        && state.LatestPackage != null && selectedPath != state.LatestPackage.Path)
                    {
                        var selectedPackage = state.Packages.Find(p => p.Path == selectedPath);
                        EditorGUILayout.LabelField($"まとめてインポート対象: {selectedPackage?.RelativePath}", _wrappedMiniStyle);
                    }

                    if (IsDennokoworks) DrawVersionInfo(state);

                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(item.BoothUrl)))
                        {
                            if (GUILayout.Button(new GUIContent("BOOTH", item.BoothUrl), EditorStyles.miniButtonLeft, GUILayout.Width(56)))
                                Application.OpenURL(item.BoothUrl);
                        }
                        using (new EditorGUI.DisabledScope(!state.DirectoryExists))
                        {
                            if (GUILayout.Button("フォルダ", EditorStyles.miniButtonRight, GUILayout.Width(56)))
                                EditorUtility.RevealInFinder(item.ItemDirectory);
                        }

                        GUILayout.FlexibleSpace();
                        DrawImportButtons(state);
                    }
                }
            }
        }

        private void DrawThumbnail(string url)
        {
            var rect = GUILayoutUtility.GetRect(ThumbnailSize, ThumbnailSize, GUILayout.Width(ThumbnailSize), GUILayout.Height(ThumbnailSize));
            if (Event.current.type != EventType.Repaint) return;

            var tex = ThumbnailCache.Get(url);
            if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.ScaleAndCrop);
            else EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.2f));
        }

        private static string GetStatusText(ToolItemState state)
        {
            string packages = state.Packages.Count > 1 ? $"（パッケージ {state.Packages.Count} 個）" : "";
            switch (state.InstallState)
            {
                case InstallState.NoPackage:
                    if (!state.DirectoryExists) return "状態: 未ダウンロード（BLM でダウンロードしてください）";
                    if (state.HasArchive) return "状態: .unitypackage が見つかりません（zip が未展開の可能性があります）";
                    return "状態: .unitypackage が見つかりません";
                case InstallState.Scanning:
                    return $"状態: パッケージを解析中...{packages}";
                case InstallState.Unknown:
                    return $"状態: 導入状況を判定できません{packages}";
                case InstallState.NotInstalled:
                    return $"状態: 未導入{packages}";
                case InstallState.PartiallyInstalled:
                    return $"状態: 一部導入済み ({state.InstalledRatio:P0}){packages}";
                case InstallState.Installed:
                    return $"状態: 導入済み{packages}";
                default:
                    return "";
            }
        }

        private void DrawVersionInfo(ToolItemState state)
        {
            var local = state.LocalTool;
            var localText = local != null
                ? DennokoVersionChecker.Display(local.Version)
                : (state.InstallState == InstallState.Installed ? "導入済み (version.json なし)" : "未導入");
            var line = $"プロジェクト: {localText}  |  BLM: {DennokoVersionChecker.Display(state.BlmVersion)}";

            var remote = local != null && local.HasGitHubRepo
                ? DennokoVersionChecker.GetCachedResult(local.GitHubOwner, local.GitHubRepo)
                : null;
            line += $"  |  最新: {GetRemoteText(remote)}";
            EditorGUILayout.LabelField(line, _wrappedMiniStyle);

            if (remote != null && remote.state == RemoteVersionState.Success
                && DennokoVersionChecker.IsNewer(remote.version, local.Version)
                && (string.IsNullOrEmpty(state.BlmVersion) || DennokoVersionChecker.IsNewer(remote.version, state.BlmVersion)))
            {
                EditorGUILayout.HelpBox(
                    $"新しいバージョン {DennokoVersionChecker.Display(remote.version)} が公開されています。BLM で最新版をダウンロードしてから「一覧を再読み込み」を押してください。"
                    + (string.IsNullOrEmpty(remote.message) ? "" : $"\n{remote.message}"),
                    MessageType.Info);

                var boothUrl = state.Item.BoothUrl;
                if (!string.IsNullOrEmpty(boothUrl))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent("BOOTH の商品ページを開く", boothUrl), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                            Application.OpenURL(boothUrl);
                    }
                }
            }
        }

        private static string GetRemoteText(RemoteVersionResult remote)
        {
            if (remote == null) return "未確認";
            switch (remote.state)
            {
                case RemoteVersionState.Checking: return "確認中...";
                case RemoteVersionState.Success: return DennokoVersionChecker.Display(remote.version);
                case RemoteVersionState.Error: return "取得失敗";
                default: return "未確認";
            }
        }

        private void DrawImportButtons(ToolItemState state)
        {
            var latest = state.LatestPackage;
            if (latest == null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    GUILayout.Button("パッケージなし", GUILayout.Width(ButtonWidth), GUILayout.Height(22));
                }
                return;
            }

            // まとめてインポート中は単体インポートを受け付けない（完了イベントの対応が崩れるため）
            using (new EditorGUI.DisabledScope(ImportQueue.IsBusy))
            {
                DrawImportButtonsCore(state, latest);
            }
        }

        private void DrawImportButtonsCore(ToolItemState state, PackageFile latest)
        {
            string label;
            Color color;
            bool hasUpdate = HasUpdate(state);

            if (hasUpdate)
            {
                label = $"更新をインポート ({DennokoVersionChecker.Display(state.BlmVersion)})";
                color = UpdateColor;
            }
            else if (state.InstallState == InstallState.Installed)
            {
                label = "再インポート";
                color = Color.white;
            }
            else
            {
                label = string.IsNullOrEmpty(state.BlmVersion) ? "インポート" : $"インポート ({DennokoVersionChecker.Display(state.BlmVersion)})";
                color = ImportColor;
            }

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool multiple = state.Packages.Count > 1;
            var mainStyle = multiple ? EditorStyles.miniButtonLeft : EditorStyles.miniButton;
            if (GUILayout.Button(new GUIContent(label, latest.RelativePath), mainStyle, GUILayout.Width(ButtonWidth), GUILayout.Height(22)))
            {
                PackageImporter.ImportPackage(latest.Path);
                GUIUtility.ExitGUI();
            }
            GUI.backgroundColor = prevColor;

            var dropdownTooltip = _multiSelect ? "まとめてインポートするパッケージを選択" : "インポートするパッケージを選択";
            if (multiple && GUILayout.Button(new GUIContent("▼", dropdownTooltip), EditorStyles.miniButtonRight, GUILayout.Width(22), GUILayout.Height(22)))
            {
                var menu = new GenericMenu();
                var registeredId = state.Item.RegisteredId;
                _selection.TryGetValue(registeredId, out var selectedPath);
                foreach (var package in state.Packages)
                {
                    var path = package.Path;
                    // GenericMenu は "/" をサブメニュー区切りとして扱うため置き換える
                    var text = package.RelativePath.Replace("/", " > ").Replace("\\", " > ");
                    if (_multiSelect)
                    {
                        // 複数選択モードでは、まとめてインポートの対象パッケージを切り替える
                        menu.AddItem(new GUIContent(text), path == selectedPath, () =>
                        {
                            _selection[registeredId] = path;
                            Repaint();
                        });
                    }
                    else
                    {
                        menu.AddItem(new GUIContent(text), false, () => PackageImporter.ImportPackage(path));
                    }
                }
                menu.ShowAsContext();
            }
        }

        private void DrawLocalOnlyTools()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("その他の導入済みツール（version.json あり / BLM 未登録）", EditorStyles.boldLabel);

            foreach (var tool in _localOnlyTools)
            {
                if (!MatchesSearch(tool.ToolFolder)) continue;

                var remote = tool.HasGitHubRepo ? DennokoVersionChecker.GetCachedResult(tool.GitHubOwner, tool.GitHubRepo) : null;
                bool hasUpdate = remote != null && remote.state == RemoteVersionState.Success
                    && DennokoVersionChecker.IsNewer(remote.version, tool.Version);

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(tool.ToolFolder, EditorStyles.boldLabel);
                        var remoteText = tool.HasGitHubRepo ? GetRemoteText(remote) : "確認できません";
                        EditorGUILayout.LabelField(
                            $"プロジェクト: {DennokoVersionChecker.Display(tool.Version)}  |  最新: {remoteText}",
                            _wrappedMiniStyle);
                        if (hasUpdate)
                        {
                            EditorGUILayout.LabelField(
                                $"新しいバージョン {DennokoVersionChecker.Display(remote.version)} があります。BOOTH で最新版を入手してください。",
                                _wrappedMiniStyle);
                        }
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("選択", EditorStyles.miniButtonLeft, GUILayout.Width(44)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tool.VersionJsonPath);
                        if (asset != null) EditorGUIUtility.PingObject(asset);
                    }

                    // 配布は BOOTH のみ。version.json の url（GitHub）は開かず、ショップページへ案内する
                    var prevColor = GUI.backgroundColor;
                    if (hasUpdate) GUI.backgroundColor = UpdateColor;
                    if (GUILayout.Button("BOOTH", EditorStyles.miniButtonRight, GUILayout.Width(56)))
                        Application.OpenURL(_shopManager.CurrentShop.ShopUrl);
                    GUI.backgroundColor = prevColor;
                }
            }
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var shop = _shopManager.CurrentShop;
                if (GUILayout.Button($"BOOTH ショップを開く ({shop.Label})", EditorStyles.miniButton))
                    Application.OpenURL(shop.ShopUrl);
            }
            EditorGUILayout.Space(2);
        }

        #endregion
    }
}

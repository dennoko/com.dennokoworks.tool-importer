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
        /// <summary>.unitypackage が無いため一覧から除外したアイテム数と、そのうち zip があるもの。</summary>
        private int _hiddenItemCount;
        private int _hiddenArchiveCount;
        private readonly List<LocalToolVersion> _localTools = new List<LocalToolVersion>();
        private readonly List<LocalToolVersion> _localOnlyTools = new List<LocalToolVersion>();

        private const string MultiSelectPrefKey = "Dennokoworks.ToolImporter.MultiSelect";
        private const string BatchInteractivePrefKey = "Dennokoworks.ToolImporter.BatchInteractive";

        private bool _statesDirty = true;
        private bool _showSettings;
        private bool _multiSelect;
        /// <summary>複数選択モードで選択中のアイテム（RegisteredId → インポートするパッケージのパス、古い順）。</summary>
        private readonly Dictionary<string, List<string>> _selection = new Dictionary<string, List<string>>();

        /// <summary>まとめて（複数件）インポートするとき、1 件ずつインポートダイアログを表示するか。</summary>
        internal static bool BatchInteractive
        {
            get => EditorPrefs.GetBool(BatchInteractivePrefKey, false);
            set { if (value != BatchInteractive) EditorPrefs.SetBool(BatchInteractivePrefKey, value); }
        }
        private string _search = "";
        private Vector2 _scrollPosition;

        // 一覧の仮想スクロール。IMGUI は描画のたびに全行を処理するため、表示範囲とその上下の数行だけ描き、
        // それ以外の行は推定の高さぶんの空白で置き換えて、上から下まで一続きにスクロールできるようにする。
        /// <summary>表示範囲の上下に余分に描く行数。</summary>
        private const int OverscanRows = 8;
        /// <summary>まだ描いたことのない行の高さの初期推定値。</summary>
        private const float DefaultRowHeight = 84f;

        /// <summary>検索に一致したアイテム（Layout イベントでのみ作り直す）。</summary>
        private readonly List<ToolItemState> _visibleItems = new List<ToolItemState>();
        /// <summary>実際に描いて測った行の高さ（次の行までの間隔）。RegisteredId → 高さ。</summary>
        private readonly Dictionary<string, float> _rowHeights = new Dictionary<string, float>();
        private float _rowHeightSum;
        /// <summary>描画する行の範囲 [_rangeStart, _rangeEnd) と、その上下に置く空白の高さ。</summary>
        private int _rangeStart;
        private int _rangeEnd;
        private float _spaceAbove;
        private float _spaceBelow;
        private readonly List<Rect> _drawnRowRects = new List<Rect>();

        private GUIStyle _itemNameStyle;
        private GUIStyle _wrappedMiniStyle;

        private const string PackageName = "com.dennokoworks.tool-importer";
        private const string ManualRelativePath = "Docs/user_manual_ja.pdf";

        [MenuItem("Tools/dennokoworks/Tool Importer", false, 100)]
        [MenuItem("dennokoworks/Tool Importer", false, 0)]
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

        private bool IsDennokoworks => _shopManager.IsDennokoworksSelected;

        #region Data loading

        /// <summary>BLM の DB からアイテム一覧を読み直し、パッケージを探索する。</summary>
        private void Reload()
        {
            _items.Clear();
            _scrollPosition = Vector2.zero;
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

            // .unitypackage を含まないアイテム（未ダウンロード・zip 未展開・Unity 用でないもの）は表示しない
            _hiddenItemCount = 0;
            _hiddenArchiveCount = 0;
            foreach (var item in items)
            {
                var state = new ToolItemState { Item = item };
                if (!string.IsNullOrEmpty(item.ItemDirectory) && Directory.Exists(item.ItemDirectory))
                {
                    state.DirectoryExists = true;
                    state.Packages = PackageImporter.FindUnityPackages(item.ItemDirectory);
                }
                if (state.LatestPackage == null)
                {
                    _hiddenItemCount++;
                    if (state.DirectoryExists && HasArchive(item.ItemDirectory)) _hiddenArchiveCount++;
                    continue;
                }
                _items.Add(state);
            }

            PruneSelection();
            MarkStatesDirty();
        }

        /// <summary>一覧に無くなったアイテム・存在しなくなったパッケージを選択から外す。</summary>
        private void PruneSelection()
        {
            var valid = new Dictionary<string, List<string>>();
            foreach (var state in _items)
            {
                if (!_selection.TryGetValue(state.Item.RegisteredId, out var paths)) continue;
                var remaining = paths.FindAll(path => state.Packages.Exists(p => p.Path == path));
                if (remaining.Count > 0) valid[state.Item.RegisteredId] = remaining;
            }
            _selection.Clear();
            foreach (var kv in valid) _selection[kv.Key] = kv.Value;
        }

        /// <summary>チェックを入れたときは最新パッケージを対象にする（別のパッケージはインポートボタンから選ぶ）。</summary>
        private void SetSelected(ToolItemState state, bool selected)
        {
            if (selected && state.LatestPackage != null)
            {
                if (!_selection.ContainsKey(state.Item.RegisteredId))
                    _selection[state.Item.RegisteredId] = new List<string> { state.LatestPackage.Path };
            }
            else
            {
                _selection.Remove(state.Item.RegisteredId);
            }
        }

        private void SetSelectedPackages(ToolItemState state, List<PackageFile> packages)
        {
            if (packages == null || packages.Count == 0)
                _selection.Remove(state.Item.RegisteredId);
            else
                _selection[state.Item.RegisteredId] = packages.ConvertAll(p => p.Path);
            Repaint();
        }

        private int SelectedPackageCount
        {
            get
            {
                int count = 0;
                foreach (var paths in _selection.Values) count += paths.Count;
                return count;
            }
        }

        /// <summary>キューやログに出すパッケージ名。</summary>
        private static string GetPackageLabel(ToolItemState state, PackageFile package)
        {
            if (package == null) return state.Item.Name;
            if (!string.IsNullOrEmpty(package.FileVersion))
                return $"{state.Item.Name} ({DennokoVersionChecker.Display(package.FileVersion)})";
            return state.Packages.Count > 1 ? $"{state.Item.Name} ({package.FileName})" : state.Item.Name;
        }

        /// <summary>
        /// ポップアップで選んだパッケージをインポートする。1 件なら通常のインポート、
        /// 複数件ならキューで順番にインポートする。packages は古い順。
        /// </summary>
        private void ImportPackages(ToolItemState state, List<PackageFile> packages)
        {
            if (packages == null || packages.Count == 0) return;
            if (ImportQueue.IsBusy) return;

            if (packages.Count == 1)
            {
                var path = packages[0].Path;
                EditorApplication.delayCall += () => PackageImporter.ImportPackage(path);
                return;
            }

            var entries = packages.ConvertAll(p => new ImportQueue.Entry { label = GetPackageLabel(state, p), path = p.Path });
            ImportQueue.Enqueue(entries, BatchInteractive);
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
                if (!_selection.TryGetValue(state.Item.RegisteredId, out var paths)) continue;
                foreach (var path in paths)
                {
                    var package = state.Packages.Find(p => p.Path == path);
                    entries.Add(new ImportQueue.Entry { label = GetPackageLabel(state, package), path = path });
                }
            }
            if (entries.Count == 0) return;

            ImportQueue.Enqueue(entries, BatchInteractive);
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
                bool allShops = _shopManager.IsAllShopsSelected;
                if (GUILayout.Button(current.Label, EditorStyles.toolbarDropDown, GUILayout.MinWidth(140)))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent($"{ShopManager.AllShops.Label}  (すべてのショップ)"), allShops,
                        () => _shopManager.Select(null));
                    menu.AddSeparator("");
                    foreach (var shop in _shopManager.Shops)
                    {
                        var subdomain = shop.subdomain;
                        menu.AddItem(new GUIContent($"{shop.Label}  ({shop.subdomain})"),
                            !allShops && string.Equals(subdomain, current.subdomain, StringComparison.OrdinalIgnoreCase),
                            () => _shopManager.Select(subdomain));
                    }
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("ショップを追加・管理..."), false, OpenShopManageDialog);
                    menu.ShowAsContext();
                }

                if (GUILayout.Button("＋ ショップ追加", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                    OpenShopManageDialog();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("マニュアル", "使い方マニュアル（PDF）を開きます"), EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                    OpenManual();

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
                EditorGUI.BeginChangeCheck();
                _search = EditorGUILayout.TextField(new GUIContent("検索", "アイテム名・ショップ名で絞り込み"), _search);
                if (EditorGUI.EndChangeCheck()) _scrollPosition = Vector2.zero;

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
                            if (MatchesSearch(state)) SetSelected(state, true);
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

                    BatchInteractive = EditorGUILayout.ToggleLeft(
                        new GUIContent("内容を確認しながら", "オンにすると 1 件ずつインポートダイアログを表示します"),
                        BatchInteractive, GUILayout.Width(120));
                }

                int count = SelectedPackageCount;
                using (new EditorGUI.DisabledScope(count == 0 || ImportQueue.IsBusy))
                {
                    var prevColor = GUI.backgroundColor;
                    GUI.backgroundColor = ImportColor;
                    if (GUILayout.Button($"まとめてインポート ({count} 件)", GUILayout.Height(24)))
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
            bool allShops = _shopManager.IsAllShopsSelected;
            var shopLabel = allShops ? "すべてのショップ" : _shopManager.CurrentShop.Label;
            int scanning = PackageImporter.PendingCount;
            using (new EditorGUILayout.HorizontalScope())
            {
                var header = $"アイテム一覧 ({shopLabel}): {_items.Count} 件";
                if (scanning > 0) header += $"  - パッケージ解析中 ({scanning})";
                EditorGUILayout.LabelField(header, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));

                if (_hiddenItemCount > 0)
                {
                    var tooltip = $".unitypackage が見つからないアイテム {_hiddenItemCount} 件は表示していません（未ダウンロード・Unity 用以外など）。";
                    if (_hiddenArchiveCount > 0)
                        tooltip += $"\nうち {_hiddenArchiveCount} 件は zip が未展開の可能性があります。BLM で展開してから「一覧を再読み込み」を押してください。";

                    var iconContent = EditorGUIUtility.IconContent("console.warnicon.sml");
                    if (iconContent == null || iconContent.image == null)
                        iconContent = EditorGUIUtility.IconContent("console.infoicon");

                    var warnContent = new GUIContent(iconContent.image, tooltip);
                    GUILayout.Label(warnContent, GUILayout.Width(20), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                GUILayout.FlexibleSpace();
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scroll.scrollPosition;

                if (_items.Count == 0 && string.IsNullOrEmpty(_loadError))
                {
                    var target = allShops ? "BLM に" : $"BLM の「{shopLabel}」に";
                    EditorGUILayout.HelpBox(
                        $"{target}インポートできるアイテム（.unitypackage）がありません。\nBLM でアイテムを追加・ダウンロードしてから「一覧を再読み込み」を押してください。",
                        MessageType.Info);
                }

                // 描画範囲は Layout イベントでだけ決め、同じフレームの他のイベントでも同じ行を描く
                // （イベントごとに行が変わると IMGUI のレイアウトが食い違うため）
                if (Event.current.type == EventType.Layout) UpdateVisibleRange();

                int end = Math.Min(_rangeEnd, _visibleItems.Count);
                bool repaint = Event.current.type == EventType.Repaint;
                if (repaint) _drawnRowRects.Clear();

                if (_spaceAbove > 0) GUILayout.Space(_spaceAbove);
                for (int i = _rangeStart; i < end; i++)
                {
                    DrawItem(_visibleItems[i]);
                    if (repaint) _drawnRowRects.Add(GUILayoutUtility.GetLastRect());
                }
                if (_spaceBelow > 0) GUILayout.Space(_spaceBelow);

                if (repaint) MeasureDrawnRows(end);

                if (IsDennokoworks && _localOnlyTools.Count > 0) DrawLocalOnlyTools();
            }
        }

        private float GetRowHeight(ToolItemState state)
        {
            if (_rowHeights.TryGetValue(state.Item.RegisteredId, out var h)) return h;
            return _rowHeights.Count > 0 ? _rowHeightSum / _rowHeights.Count : DefaultRowHeight;
        }

        /// <summary>スクロール位置から、描画する行の範囲と上下の空白の高さを決める。</summary>
        private void UpdateVisibleRange()
        {
            _visibleItems.Clear();
            foreach (var state in _items)
            {
                if (MatchesSearch(state)) _visibleItems.Add(state);
            }

            // スクロールビューの高さはこの時点では分からないため、ウィンドウの高さで代用する（多めに描くだけ）
            float viewTop = _scrollPosition.y;
            float viewBottom = viewTop + position.height;

            int count = _visibleItems.Count;
            int first = count, last = -1;
            float y = 0;
            for (int i = 0; i < count; i++)
            {
                float h = GetRowHeight(_visibleItems[i]);
                if (first == count && y + h > viewTop) first = i;
                if (y < viewBottom) last = i;
                y += h;
            }
            if (first == count) first = Math.Max(0, count - 1); // スクロール位置が末尾より下（絞り込み直後など）

            _rangeStart = Math.Max(0, first - OverscanRows);
            _rangeEnd = Math.Min(count, Math.Max(last, first) + 1 + OverscanRows);

            _spaceAbove = 0;
            _spaceBelow = 0;
            for (int i = 0; i < _rangeStart; i++) _spaceAbove += GetRowHeight(_visibleItems[i]);
            for (int i = _rangeEnd; i < count; i++) _spaceBelow += GetRowHeight(_visibleItems[i]);
        }

        /// <summary>
        /// 描いた行の実際の高さを記録する。推定と違った場合は空白の高さを直すため再描画し、
        /// 表示範囲より上の行の高さが変わったぶんはスクロール位置を補正して表示がずれないようにする。
        /// </summary>
        private void MeasureDrawnRows(int end)
        {
            int n = _drawnRowRects.Count;
            if (n == 0 || n != end - _rangeStart) return;

            var margin = EditorStyles.helpBox.margin;
            float lastGap = Mathf.Max(margin.top, margin.bottom);
            float scrollCorrection = 0;
            bool changed = false;

            for (int k = 0; k < n; k++)
            {
                var rect = _drawnRowRects[k];
                float measured = k + 1 < n ? _drawnRowRects[k + 1].y - rect.y : rect.height + lastGap;
                if (measured <= 0) continue;

                var id = _visibleItems[_rangeStart + k].Item.RegisteredId;
                bool known = _rowHeights.TryGetValue(id, out var previous);
                float estimate = known ? previous : GetRowHeight(_visibleItems[_rangeStart + k]);
                if (known && Mathf.Abs(measured - previous) < 0.5f) continue;

                _rowHeightSum += measured - (known ? previous : 0);
                _rowHeights[id] = measured;
                changed |= Mathf.Abs(measured - estimate) >= 0.5f || !known;

                // 表示範囲より完全に上にある行が伸び縮みしたら、見えている内容が動かないようスクロールを補正
                if (rect.y + measured <= _scrollPosition.y) scrollCorrection += measured - estimate;
            }

            if (scrollCorrection != 0) _scrollPosition.y = Mathf.Max(0, _scrollPosition.y + scrollCorrection);
            if (changed) Repaint();
        }

        private bool MatchesSearch(ToolItemState state)
        {
            if (string.IsNullOrWhiteSpace(_search)) return true;
            if (state?.Item == null) return false;

            if (MatchesSearch(state.Item.Name)) return true;
            if (MatchesSearch(state.Item.ShopName)) return true;
            if (MatchesSearch(state.Item.ShopSubdomain)) return true;

            var registeredShop = !string.IsNullOrEmpty(state.Item.ShopSubdomain)
                ? _shopManager.Find(state.Item.ShopSubdomain)
                : null;
            if (registeredShop != null && MatchesSearch(registeredShop.displayName)) return true;

            return false;
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

                    // None（全ショップ）表示時はどのショップのアイテムか分かるようにする
                    if (_shopManager.IsAllShopsSelected && !string.IsNullOrEmpty(item.ShopSubdomain))
                    {
                        EditorGUILayout.LabelField(GetShopLabel(item.ShopSubdomain, item.ShopName), _wrappedMiniStyle);
                    }

                    // 複数パッケージがあるアイテムは、まとめてインポートで入るファイルを常に明示する
                    if (_multiSelect && state.Packages.Count > 1
                        && _selection.TryGetValue(item.RegisteredId, out var selectedPaths))
                    {
                        var names = selectedPaths.ConvertAll(path => state.Packages.Find(p => p.Path == path)?.RelativePath ?? Path.GetFileName(path));
                        EditorGUILayout.LabelField($"まとめてインポート対象: {string.Join("、", names)}", _wrappedMiniStyle);
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

        /// <summary>登録済みショップなら表示名、それ以外は BLM のショップ名またはサブドメインを返す。</summary>
        private string GetShopLabel(string subdomain, string shopName = null)
        {
            var shop = _shopManager.Find(subdomain);
            if (shop != null && shop.Label != subdomain)
            {
                return $"{shop.Label} ({subdomain})";
            }
            if (!string.IsNullOrEmpty(shopName))
            {
                return $"{shopName} ({subdomain})";
            }
            return subdomain;
        }

        private void DrawVersionInfo(ToolItemState state)
        {
            var local = state.LocalTool;
            if (local == null || !local.HasGitHubRepo) return;

            var remote = DennokoVersionChecker.GetCachedResult(local.GitHubOwner, local.GitHubRepo);
            if (remote != null && remote.state == RemoteVersionState.Success
                && DennokoVersionChecker.IsNewer(remote.version, local.Version)
                && (string.IsNullOrEmpty(state.BlmVersion) || DennokoVersionChecker.IsNewer(remote.version, state.BlmVersion)))
            {
                EditorGUILayout.HelpBox(
                    $"新しいバージョン {DennokoVersionChecker.Display(remote.version)} があります。BLM でダウンロードしてください。"
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
                label = "更新をインポート";
                color = UpdateColor;
            }
            else if (state.InstallState == InstallState.Installed)
            {
                label = "再インポート";
                color = Color.white;
            }
            else
            {
                label = "インポート";
                color = ImportColor;
            }

            bool multiple = state.Packages.Count > 1;
            var content = multiple
                ? new GUIContent($"{label} ▾", $"含まれるパッケージ（{state.Packages.Count} 件）から選んでインポート")
                : new GUIContent(label, latest.RelativePath);

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            var rect = GUILayoutUtility.GetRect(content, EditorStyles.miniButton, GUILayout.Width(ButtonWidth), GUILayout.Height(22));
            bool clicked = GUI.Button(rect, content, EditorStyles.miniButton);
            GUI.backgroundColor = prevColor;
            if (!clicked) return;

            if (!multiple)
            {
                PackageImporter.ImportPackage(latest.Path);
                GUIUtility.ExitGUI();
                return;
            }

            // 複数バージョンが保存されていることが多いため、必ず一覧から入れるものを選ばせる
            _selection.TryGetValue(state.Item.RegisteredId, out var selectedPaths);
            var popup = new PackageSelectPopup(
                state.Item.Name,
                state.Packages,
                _multiSelect ? selectedPaths : null,
                packages => ImportPackages(state, packages),
                _multiSelect ? packages => SetSelectedPackages(state, packages) : (Action<List<PackageFile>>)null);
            PopupWindow.Show(rect, popup);
        }

        private void DrawLocalOnlyTools()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("その他の導入済みツール", EditorStyles.boldLabel);

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

        /// <summary>パッケージ同梱の PDF マニュアルを既定のアプリ（通常はブラウザ / PDF ビューア）で開く。</summary>
        private static void OpenManual()
        {
            var path = GetManualPath();
            if (path == null || !File.Exists(path))
            {
                EditorUtility.DisplayDialog(WindowTitle, $"マニュアルが見つかりませんでした。\n{path ?? ManualRelativePath}", "OK");
                return;
            }
            Application.OpenURL(new Uri(path).AbsoluteUri);
        }

        private static string GetManualPath()
        {
            // VPM / 埋め込み / ローカル参照のどの形で導入されていても、パッケージの実体の場所を引く
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DennokoworksToolImporterWindow).Assembly);
            var root = info != null && !string.IsNullOrEmpty(info.resolvedPath)
                ? info.resolvedPath
                : Path.GetFullPath(Path.Combine("Packages", PackageName));
            return Path.GetFullPath(Path.Combine(root, ManualRelativePath));
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

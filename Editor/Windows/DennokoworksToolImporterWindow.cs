using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.ToolImporter.Editor
{
    public class DennokoworksToolImporterWindow : EditorWindow
    {
        private const string WindowTitle = "dennokoworks Importer";
        private const string ShowSettingsPrefKey = "Dennokoworks.ToolImporter.ShowSettings";
        private const string MultiSelectPrefKey = "Dennokoworks.ToolImporter.MultiSelect";
        private const string BatchInteractivePrefKey = "Dennokoworks.ToolImporter.BatchInteractive";

        private const string UXML_GUID = "04e7376de2b6464da001498249c38f69";
        private const string USS_THEME_GUID = "717c098cc0ab4281b2672274b79d3489";
        private const string USS_WINDOW_GUID = "7909e5123070431da8866a2a51cc9ebd";

        private const string PackageName = "com.dennokoworks.tool-importer";
        private const string ManualRelativePath = "Docs/user_manual_ja.pdf";

        public enum StatusType { Info, Success, Error }

        /// <summary>インポート状況による一覧の絞り込み。</summary>
        private enum ImportFilter { All, NotImported, Imported }

        private const string ImportFilterPrefKey = "Dennokoworks.ToolImporter.ImportFilter";

        private ShopManager _shopManager;
        private string _dbPath;
        private string _itemRoot;
        private string _loadError;

        private readonly List<ToolItemState> _items = new List<ToolItemState>();
        private int _hiddenItemCount;
        private int _hiddenArchiveCount;
        private readonly List<LocalToolVersion> _localTools = new List<LocalToolVersion>();
        private readonly List<LocalToolVersion> _localOnlyTools = new List<LocalToolVersion>();

        private bool _statesDirty = true;
        private bool _showSettings;
        private bool _multiSelect;
        private string _search = "";
        private ImportFilter _importFilter;

        private readonly Dictionary<string, List<string>> _selection = new Dictionary<string, List<string>>();
        /// <summary>一覧の 1 行。BLM のアイテム・「その他の導入済みツール」見出し・そのツールのいずれか。</summary>
        private sealed class ListRow
        {
            public ToolItemState State;
            public LocalToolVersion OtherTool;
            public bool IsOtherToolsHeader;
        }

        /// <summary>サムネイルを読み込み待ち・表示中の行を参照し続ける間隔（ThumbnailCache の取り消し猶予 1 秒より短く）。</summary>
        private const long ThumbnailTouchIntervalMs = 500;

        private readonly List<ListRow> _rows = new List<ListRow>();
        /// <summary>いま行に表示されているサムネイルの Image と URL。行が範囲外になったら外す。</summary>
        private readonly Dictionary<Image, string> _boundThumbnails = new Dictionary<Image, string>();
        private bool _allShopsInList;

        private ToolImporterVersionChecker.Result _versionResult =
            new ToolImporterVersionChecker.Result { State = ToolImporterVersionChecker.State.Checking, LocalVersion = "0.2.0" };

        internal static bool BatchInteractive
        {
            get => EditorPrefs.GetBool(BatchInteractivePrefKey, false);
            set { if (value != BatchInteractive) EditorPrefs.SetBool(BatchInteractivePrefKey, value); }
        }

        // ─── UI Toolkit 要素参照 ──────────────────────────────────────────
        private VisualElement _root;
        private Label _titleLabel;
        private Label _versionLabel;
        private Button _versionReloadBtn;
        private Button _shopDropdownBtn;
        private Label _shopPrefixLabel;
        private Button _addShopBtn;
        private Button _manualBtn;
        private Button _settingsToggleBtn;
        private Button _langBtn;

        private VisualElement _settingsCard;
        private Label _settingsTitleLabel;
        private Label _dbPathLabel;
        private Label _dbPathValue;
        private Button _dbPathBrowseBtn;
        private Button _dbPathAutoBtn;
        private Label _dbPathStatus;
        private Label _itemDirLabel;
        private Label _itemDirValue;
        private Button _itemDirBrowseBtn;
        private Button _itemDirAutoBtn;
        private Label _itemDirStatus;
        private Button _clearCacheBtn;

        private VisualElement _loadErrorContainer;
        private Label _loadErrorLabel;

        private VisualElement _queueCard;
        private ProgressBar _queueProgressBar;
        private Label _queuePendingLabel;
        private Button _queueSkipBtn;
        private Button _queueCancelBtn;
        private VisualElement _queueSummaryContainer;
        private Label _queueSummaryLabel;
        private Button _queueSummaryDismissBtn;

        private Button _checkRemoteBtn;
        private Button _reloadListBtn;
        private TextField _searchField;
        private Button _importFilterBtn;
        private Toggle _multiSelectToggle;

        private VisualElement _batchBar;
        private Button _batchSelectAllBtn;
        private Button _batchSelectUpdatesBtn;
        private Button _batchDeselectAllBtn;
        private Toggle _batchInteractiveToggle;
        private Button _batchImportBtn;

        private Label _itemsCountLabel;
        private VisualElement _itemsWarningContainer;
        private Image _itemsWarningIcon;
        private Label _itemsWarningCount;
        private VisualElement _emptyMessageCard;
        private Label _emptyMessageLabel;
        private ListView _itemsListView;

        private Button _openBoothShopBtn;
        private Label _statusLabel;
        private IVisualElementScheduledItem _statusResetSchedule;

        [MenuItem("Tools/dennokoworks/Tool Importer", false, 100)]
        [MenuItem("dennokoworks/Tool Importer", false, 0)]
        public static void Open()
        {
            var window = GetWindow<DennokoworksToolImporterWindow>(WindowTitle);
            window.minSize = new Vector2(360, 360);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            _showSettings = EditorPrefs.GetBool(ShowSettingsPrefKey, false);
            _multiSelect = EditorPrefs.GetBool(MultiSelectPrefKey, false);
            _importFilter = (ImportFilter)EditorPrefs.GetInt(ImportFilterPrefKey, (int)ImportFilter.All);
            if (!Enum.IsDefined(typeof(ImportFilter), _importFilter)) _importFilter = ImportFilter.All;

            _shopManager = new ShopManager();
            _shopManager.Changed += OnShopManagerChanged;

            PackageImporter.ScanCompleted += OnPackageScanCompleted;
            ThumbnailCache.Loaded += OnThumbnailLoaded;
            ImportQueue.Changed += UpdateQueueUI;
            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
            EditorApplication.projectChanged += OnProjectChanged;
            ToolImporterLoc.OnLanguageChanged += RefreshLocalization;

            Reload();
        }

        private void OnDisable()
        {
            if (_shopManager != null) _shopManager.Changed -= OnShopManagerChanged;
            PackageImporter.ScanCompleted -= OnPackageScanCompleted;
            ThumbnailCache.Loaded -= OnThumbnailLoaded;
            ImportQueue.Changed -= UpdateQueueUI;
            AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
            EditorApplication.projectChanged -= OnProjectChanged;
            ToolImporterLoc.OnLanguageChanged -= RefreshLocalization;
        }

        private void OnShopManagerChanged()
        {
            Reload();
            UpdateUI();
        }

        private void OnPackageScanCompleted() => MarkStatesDirty();
        private void OnImportPackageCompleted(string packageName) => MarkStatesDirty();
        private void OnProjectChanged() => MarkStatesDirty();

        private void MarkStatesDirty()
        {
            _statesDirty = true;
            if (_root != null)
            {
                RefreshStates();
                UpdateItemList();
            }
        }

        private bool IsDennokoworks => _shopManager != null && _shopManager.IsDennokoworksSelected;

        public void CreateGUI()
        {
            _root = rootVisualElement;
            _root.AddToClassList("dennoko-root");
            _root.style.flexGrow = 1;

            DennokoUIFont.Apply(_root);
            LoadStylesheets();

            var uxml = LoadAssetByGuidOrPath<VisualTreeAsset>(UXML_GUID, "Editor/UI/DennokoworksToolImporterWindow.uxml");
            if (uxml == null)
            {
                _root.Add(new Label("Error: DennokoworksToolImporterWindow.uxml not found."));
                return;
            }
            uxml.CloneTree(_root);

            BindElements();
            SetupEventHandlers();
            RefreshLocalization();
            StartVersionCheck();
            UpdateUI();
        }

        private void LoadStylesheets()
        {
            var themeUss = LoadAssetByGuidOrPath<StyleSheet>(USS_THEME_GUID, "Editor/UI/DennokoTheme.uss");
            if (themeUss != null) _root.styleSheets.Add(themeUss);

            var windowUss = LoadAssetByGuidOrPath<StyleSheet>(USS_WINDOW_GUID, "Editor/UI/DennokoworksToolImporterWindow.uss");
            if (windowUss != null) _root.styleSheets.Add(windowUss);
        }

        private static T LoadAssetByGuidOrPath<T>(string guid, string relativeSuffix) where T : UnityEngine.Object
        {
            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                    if (asset != null) return asset;
                }
            }

            string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(relativeSuffix));
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(relativeSuffix.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(p);
                    if (asset != null) return asset;
                }
            }
            return null;
        }

        private void BindElements()
        {
            _titleLabel = _root.Q<Label>("title-label");
            _versionLabel = _root.Q<Label>("version-label");
            _versionReloadBtn = _root.Q<Button>("version-reload-button");
            _shopDropdownBtn = _root.Q<Button>("shop-dropdown-button");
            _shopPrefixLabel = _root.Q<Label>("shop-prefix-label");
            _addShopBtn = _root.Q<Button>("add-shop-button");
            _manualBtn = _root.Q<Button>("manual-button");
            _settingsToggleBtn = _root.Q<Button>("settings-toggle-button");
            _langBtn = _root.Q<Button>("lang-button");

            _settingsCard = _root.Q<VisualElement>("settings-card");
            _settingsTitleLabel = _root.Q<Label>("settings-title-label");
            _dbPathLabel = _root.Q<Label>("db-path-label");
            _dbPathValue = _root.Q<Label>("db-path-value");
            _dbPathBrowseBtn = _root.Q<Button>("db-path-browse-button");
            _dbPathAutoBtn = _root.Q<Button>("db-path-auto-button");
            _dbPathStatus = _root.Q<Label>("db-path-status");
            _itemDirLabel = _root.Q<Label>("item-dir-label");
            _itemDirValue = _root.Q<Label>("item-dir-value");
            _itemDirBrowseBtn = _root.Q<Button>("item-dir-browse-button");
            _itemDirAutoBtn = _root.Q<Button>("item-dir-auto-button");
            _itemDirStatus = _root.Q<Label>("item-dir-status");
            _clearCacheBtn = _root.Q<Button>("clear-cache-button");

            _loadErrorContainer = _root.Q<VisualElement>("load-error-container");
            _loadErrorLabel = _root.Q<Label>("load-error-label");

            _queueCard = _root.Q<VisualElement>("queue-card");
            _queueProgressBar = _root.Q<ProgressBar>("queue-progress-bar");
            _queuePendingLabel = _root.Q<Label>("queue-pending-label");
            _queueSkipBtn = _root.Q<Button>("queue-skip-button");
            _queueCancelBtn = _root.Q<Button>("queue-cancel-button");
            _queueSummaryContainer = _root.Q<VisualElement>("queue-summary-container");
            _queueSummaryLabel = _root.Q<Label>("queue-summary-label");
            _queueSummaryDismissBtn = _root.Q<Button>("queue-summary-dismiss");

            _checkRemoteBtn = _root.Q<Button>("check-remote-button");
            _reloadListBtn = _root.Q<Button>("reload-list-button");
            _searchField = _root.Q<TextField>("search-field");
            _importFilterBtn = _root.Q<Button>("import-filter-button");
            _multiSelectToggle = _root.Q<Toggle>("multi-select-toggle");

            _batchBar = _root.Q<VisualElement>("batch-bar");
            _batchSelectAllBtn = _root.Q<Button>("batch-select-all-button");
            _batchSelectUpdatesBtn = _root.Q<Button>("batch-select-updates-button");
            _batchDeselectAllBtn = _root.Q<Button>("batch-deselect-all-button");
            _batchInteractiveToggle = _root.Q<Toggle>("batch-interactive-toggle");
            _batchImportBtn = _root.Q<Button>("batch-import-button");

            _itemsCountLabel = _root.Q<Label>("items-count-label");
            _itemsWarningContainer = _root.Q<VisualElement>("items-warning-container");
            _itemsWarningIcon = _root.Q<Image>("items-warning-icon");
            _itemsWarningCount = _root.Q<Label>("items-warning-count");
            _emptyMessageCard = _root.Q<VisualElement>("empty-message-card");
            _emptyMessageLabel = _root.Q<Label>("empty-message-label");
            _itemsListView = _root.Q<ListView>("items-list-view");
            SetupItemsListView();

            _openBoothShopBtn = _root.Q<Button>("open-booth-shop-button");
            _statusLabel = _root.Q<Label>("status-label");
        }

        private void SetupEventHandlers()
        {
            if (_langBtn != null)
            {
                _langBtn.clicked += () =>
                {
                    ToolImporterLoc.CurrentLanguage = ToolImporterLoc.CurrentLanguage == Language.Japanese
                        ? Language.English
                        : Language.Japanese;
                };
            }

            if (_versionReloadBtn != null)
            {
                _versionReloadBtn.clicked += () =>
                {
                    ToolImporterVersion.ForceRecheck();
                    LoadVersionResultFromSessionState();
                };
            }

            if (_shopDropdownBtn != null) _shopDropdownBtn.clicked += ShowShopDropdownMenu;
            if (_addShopBtn != null) _addShopBtn.clicked += OpenShopManageDialog;
            if (_manualBtn != null) _manualBtn.clicked += OpenManual;

            if (_settingsToggleBtn != null)
            {
                _settingsToggleBtn.clicked += () =>
                {
                    _showSettings = !_showSettings;
                    EditorPrefs.SetBool(ShowSettingsPrefKey, _showSettings);
                    UpdateSettingsUI();
                };
            }

            if (_dbPathBrowseBtn != null)
            {
                _dbPathBrowseBtn.clicked += () =>
                {
                    var dir = string.IsNullOrEmpty(_dbPath) ? "" : Path.GetDirectoryName(_dbPath);
                    var selected = EditorUtility.OpenFilePanel(ToolImporterLoc.Tr("SelectDbDialogTitle"), dir, "db");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        BlmDataAccessor.DbPathOverride = selected;
                        Reload();
                        UpdateUI();
                    }
                };
            }

            if (_dbPathAutoBtn != null)
            {
                _dbPathAutoBtn.clicked += () =>
                {
                    BlmDataAccessor.DbPathOverride = "";
                    Reload();
                    UpdateUI();
                };
            }

            if (_itemDirBrowseBtn != null)
            {
                _itemDirBrowseBtn.clicked += () =>
                {
                    var selected = EditorUtility.OpenFolderPanel(ToolImporterLoc.Tr("SelectDirDialogTitle"), _itemRoot ?? "", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        BlmDataAccessor.ItemDirectoryOverride = selected;
                        Reload();
                        UpdateUI();
                    }
                };
            }

            if (_itemDirAutoBtn != null)
            {
                _itemDirAutoBtn.clicked += () =>
                {
                    BlmDataAccessor.ItemDirectoryOverride = "";
                    Reload();
                    UpdateUI();
                };
            }

            if (_clearCacheBtn != null)
            {
                _clearCacheBtn.clicked += () =>
                {
                    PackageImporter.ClearCache();
                    MarkStatesDirty();
                    SetStatus(ToolImporterLoc.Tr("ClearCache") + ": Done", StatusType.Success);
                };
            }

            if (_checkRemoteBtn != null) _checkRemoteBtn.clicked += CheckAllRemoteVersions;
            if (_reloadListBtn != null)
            {
                _reloadListBtn.clicked += () =>
                {
                    Reload();
                    UpdateUI();
                };
            }

            if (_searchField != null)
            {
                _searchField.RegisterValueChangedCallback(evt =>
                {
                    // ラベル (Label) の文字を変えたときの ChangeEvent<string> も子要素から上がってくるので、
                    // 入力欄自身の値の変更だけを拾う（拾うと言語切替で _search がラベル文字になる）
                    if (evt.target != _searchField) return;
                    _search = evt.newValue;
                    UpdateItemList();
                });
            }

            if (_importFilterBtn != null) _importFilterBtn.clicked += ShowImportFilterMenu;

            if (_multiSelectToggle != null)
            {
                _multiSelectToggle.value = _multiSelect;
                _multiSelectToggle.RegisterValueChangedCallback(evt =>
                {
                    _multiSelect = evt.newValue;
                    EditorPrefs.SetBool(MultiSelectPrefKey, _multiSelect);
                    if (!_multiSelect) _selection.Clear();
                    UpdateBatchBarUI();
                    UpdateItemList();
                });
            }

            if (_batchSelectAllBtn != null)
            {
                _batchSelectAllBtn.clicked += () =>
                {
                    foreach (var state in _items)
                    {
                        if (IsListed(state)) SetSelected(state, true);
                    }
                    UpdateBatchBarUI();
                    UpdateItemList();
                };
            }

            if (_batchSelectUpdatesBtn != null)
            {
                _batchSelectUpdatesBtn.clicked += () =>
                {
                    foreach (var state in _items)
                    {
                        if (HasUpdate(state)) SetSelected(state, true);
                    }
                    UpdateBatchBarUI();
                    UpdateItemList();
                };
            }

            if (_batchDeselectAllBtn != null)
            {
                _batchDeselectAllBtn.clicked += () =>
                {
                    _selection.Clear();
                    UpdateBatchBarUI();
                    UpdateItemList();
                };
            }

            if (_batchInteractiveToggle != null)
            {
                _batchInteractiveToggle.value = BatchInteractive;
                _batchInteractiveToggle.RegisterValueChangedCallback(evt => BatchInteractive = evt.newValue);
            }

            if (_batchImportBtn != null) _batchImportBtn.clicked += EnqueueSelection;

            if (_queueSkipBtn != null) _queueSkipBtn.clicked += () => ImportQueue.SkipCurrent();
            if (_queueCancelBtn != null) _queueCancelBtn.clicked += () => ImportQueue.CancelPending();
            if (_queueSummaryDismissBtn != null) _queueSummaryDismissBtn.clicked += () => ImportQueue.ClearSummary();

            if (_openBoothShopBtn != null)
            {
                _openBoothShopBtn.clicked += () =>
                {
                    if (_shopManager?.CurrentShop != null)
                        Application.OpenURL(_shopManager.CurrentShop.ShopUrl);
                };
            }
        }

        private void RefreshLocalization()
        {
            if (_root == null) return;

            if (_titleLabel != null) _titleLabel.text = ToolImporterLoc.Tr("HeaderTitle");
            if (_shopPrefixLabel != null) _shopPrefixLabel.text = ToolImporterLoc.Tr("ShopLabel");
            if (_langBtn != null)
            {
                _langBtn.text = ToolImporterLoc.Tr("LangButton");
                _langBtn.tooltip = ToolImporterLoc.Tr("LangButtonTooltip");
            }
            if (_versionReloadBtn != null) _versionReloadBtn.tooltip = ToolImporterLoc.Tr("ReloadVersionTooltip");
            if (_manualBtn != null)
            {
                _manualBtn.text = ToolImporterLoc.Tr("Manual");
                _manualBtn.tooltip = ToolImporterLoc.Tr("ManualTooltip");
            }
            if (_settingsToggleBtn != null) _settingsToggleBtn.text = ToolImporterLoc.Tr("Settings");
            if (_settingsTitleLabel != null) _settingsTitleLabel.text = ToolImporterLoc.Tr("BlmSettingsTitle");
            if (_dbPathLabel != null) _dbPathLabel.text = ToolImporterLoc.Tr("DbPathLabel");
            if (_itemDirLabel != null) _itemDirLabel.text = ToolImporterLoc.Tr("ItemDirLabel");
            if (_dbPathBrowseBtn != null) _dbPathBrowseBtn.text = ToolImporterLoc.Tr("Browse");
            if (_itemDirBrowseBtn != null) _itemDirBrowseBtn.text = ToolImporterLoc.Tr("Browse");
            if (_dbPathAutoBtn != null)
            {
                _dbPathAutoBtn.text = ToolImporterLoc.Tr("Auto");
                _dbPathAutoBtn.tooltip = ToolImporterLoc.Tr("AutoTooltip");
            }
            if (_itemDirAutoBtn != null)
            {
                _itemDirAutoBtn.text = ToolImporterLoc.Tr("Auto");
                _itemDirAutoBtn.tooltip = ToolImporterLoc.Tr("AutoTooltip");
            }
            if (_clearCacheBtn != null) _clearCacheBtn.text = ToolImporterLoc.Tr("ClearCache");

            if (_reloadListBtn != null) _reloadListBtn.text = ToolImporterLoc.Tr("ReloadList");
            if (_searchField != null)
            {
                _searchField.label = ToolImporterLoc.Tr("SearchLabel");
                _searchField.tooltip = ToolImporterLoc.Tr("SearchTooltip");
            }
            UpdateImportFilterButtonUI();
            if (_multiSelectToggle != null) _multiSelectToggle.text = ToolImporterLoc.Tr("MultiSelectMode");

            if (_batchSelectAllBtn != null) _batchSelectAllBtn.text = ToolImporterLoc.Tr("SelectAll");
            if (_batchSelectUpdatesBtn != null)
            {
                _batchSelectUpdatesBtn.text = ToolImporterLoc.Tr("SelectUpdates");
                _batchSelectUpdatesBtn.tooltip = ToolImporterLoc.Tr("SelectUpdatesTooltip");
            }
            if (_batchDeselectAllBtn != null) _batchDeselectAllBtn.text = ToolImporterLoc.Tr("DeselectAll");
            if (_batchInteractiveToggle != null)
            {
                _batchInteractiveToggle.text = ToolImporterLoc.Tr("BatchInteractive");
                _batchInteractiveToggle.tooltip = ToolImporterLoc.Tr("BatchInteractiveTooltip");
            }

            if (_queueSkipBtn != null)
            {
                _queueSkipBtn.text = ToolImporterLoc.Tr("QueueSkipCurrent");
                _queueSkipBtn.tooltip = ToolImporterLoc.Tr("QueueSkipCurrentTooltip");
            }
            if (_queueCancelBtn != null) _queueCancelBtn.text = ToolImporterLoc.Tr("QueueCancelPending");

            ApplyVersionLabel();
            UpdateSettingsUI();
            UpdateShopButtonUI();
            UpdateActionBarUI();
            UpdateBatchBarUI();
            UpdateQueueUI();
            UpdateItemList();
            UpdateFooterUI();
        }

        #region Version Check

        private void StartVersionCheck()
        {
            LoadVersionResultFromSessionState();
            ToolImporterVersion.StartCheckBackgroundTask();
        }

        internal void LoadVersionResultFromSessionState()
        {
            string local  = ToolImporterVersion.Current;
            string latest = SessionState.GetString(ToolImporterVersion.VerCheckLatestKey, string.Empty);
            bool   done   = SessionState.GetBool(ToolImporterVersion.VerCheckDoneKey, false);
            bool   error  = SessionState.GetBool(ToolImporterVersion.VerCheckErrorKey, false);

            ToolImporterVersionChecker.State state;
            if (!done)
                state = ToolImporterVersionChecker.State.Checking;
            else if (error || string.IsNullOrEmpty(latest))
                state = ToolImporterVersionChecker.State.Error;
            else if (ToolImporterVersionChecker.IsUpdateAvailable(latest, local))
                state = ToolImporterVersionChecker.State.UpdateAvailable;
            else
                state = ToolImporterVersionChecker.State.UpToDate;

            _versionResult = new ToolImporterVersionChecker.Result
            {
                State = state,
                LocalVersion = local,
                LatestVersion = latest,
                Url = SessionState.GetString(ToolImporterVersion.VerCheckUrlKey, string.Empty),
                Message = SessionState.GetString(ToolImporterVersion.VerCheckMessageKey, string.Empty)
            };
            ApplyVersionLabel();
        }

        private void ApplyVersionLabel()
        {
            if (_versionLabel == null) return;

            var r = _versionResult;
            string baseText = "v" + r.LocalVersion;
            string text;
            bool update = false, error = false;
            switch (r.State)
            {
                case ToolImporterVersionChecker.State.UpdateAvailable:
                    text = baseText + "  " + ToolImporterLoc.Tr("VerUpdateAvailable", r.LatestVersion);
                    update = true;
                    break;
                case ToolImporterVersionChecker.State.Error:
                    text = baseText + "  " + ToolImporterLoc.Tr("VerError");
                    error = true;
                    break;
                case ToolImporterVersionChecker.State.Checking:
                    text = baseText + "  " + ToolImporterLoc.Tr("VerChecking");
                    break;
                default:
                    text = baseText;
                    break;
            }
            _versionLabel.text = text;
            _versionLabel.EnableInClassList("dennoko-version-label--update", update);
            _versionLabel.EnableInClassList("dennoko-version-label--error", error);
        }

        #endregion

        #region Data loading & states

        private void Reload()
        {
            _items.Clear();
            _loadError = null;
            _dbPath = null;
            _itemRoot = null;

            if (!SqliteReader.IsSupported)
            {
                _loadError = ToolImporterLoc.Tr("OsNotSupported");
                MarkStatesDirty();
                return;
            }

            _dbPath = BlmDataAccessor.ResolveDatabasePath();
            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath))
            {
                _loadError = string.IsNullOrEmpty(BlmDataAccessor.DbPathOverride)
                    ? ToolImporterLoc.Tr("DbNotFoundDefault")
                    : ToolImporterLoc.Tr("DbNotFoundManual", _dbPath);
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
                _loadError = ToolImporterLoc.Tr("DbReadError", e.Message);
                MarkStatesDirty();
                return;
            }

            if (string.IsNullOrEmpty(_itemRoot) || !Directory.Exists(_itemRoot))
            {
                _loadError = ToolImporterLoc.Tr("ItemDirNotFound", _itemRoot);
                _showSettings = true;
            }

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
            UpdateBatchBarUI();
            UpdateItemList();
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

        private bool HasUpdate(ToolItemState state)
        {
            return IsDennokoworks && state.LocalTool != null
                && DennokoVersionChecker.IsNewer(state.BlmVersion, state.LocalTool.Version);
        }

        private static bool HasArchive(string directory)
        {
            try
            {
                return Directory.GetFiles(directory, "*.zip", SearchOption.AllDirectories).Length > 0;
            }
            catch
            {
                return false;
            }
        }

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
                DennokoVersionChecker.CheckAsync(tool.GitHubOwner, tool.GitHubRepo, tool.ToolFolder, _ =>
                {
                    UpdateActionBarUI();
                    UpdateItemList();
                });
                requested++;
            }

            if (requested == 0)
            {
                EditorUtility.DisplayDialog(ToolImporterLoc.Tr("CheckAllRemote"),
                    ToolImporterLoc.Tr("NoLocalToolsFound"), "OK");
            }
            UpdateActionBarUI();
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
            UpdateBatchBarUI();
            UpdateItemList();
        }

        private static string GetPackageLabel(ToolItemState state, PackageFile package)
        {
            if (package == null) return state.Item.Name;
            if (!string.IsNullOrEmpty(package.FileVersion))
                return $"{state.Item.Name} ({DennokoVersionChecker.Display(package.FileVersion)})";
            return state.Packages.Count > 1 ? $"{state.Item.Name} ({package.FileName})" : state.Item.Name;
        }

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

        #endregion

        #region UI Updates

        private void UpdateUI()
        {
            if (_root == null) return;
            if (_statesDirty) RefreshStates();

            UpdateSettingsUI();
            UpdateShopButtonUI();
            UpdateLoadErrorUI();
            UpdateQueueUI();
            UpdateActionBarUI();
            UpdateBatchBarUI();
            UpdateItemList();
            UpdateFooterUI();
        }

        private void UpdateShopButtonUI()
        {
            if (_shopDropdownBtn == null || _shopManager == null) return;
            var current = _shopManager.CurrentShop;
            _shopDropdownBtn.text = $"{current.Label} ▾";
        }

        private void ShowShopDropdownMenu()
        {
            var menu = new GenericMenu();
            bool allShops = _shopManager.IsAllShopsSelected;
            menu.AddItem(new GUIContent($"{ShopManager.AllShops.Label}  ({ToolImporterLoc.Tr("AllShops")})"), allShops,
                () => _shopManager.Select(null));
            menu.AddSeparator("");
            foreach (var shop in _shopManager.Shops)
            {
                var subdomain = shop.subdomain;
                menu.AddItem(new GUIContent($"{shop.Label}  ({shop.subdomain})"),
                    !allShops && string.Equals(subdomain, _shopManager.CurrentShop.subdomain, StringComparison.OrdinalIgnoreCase),
                    () => _shopManager.Select(subdomain));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(ToolImporterLoc.Tr("ManageShops")), false, OpenShopManageDialog);
            menu.ShowAsContext();
        }

        private void ShowImportFilterMenu()
        {
            var menu = new GenericMenu();
            foreach (ImportFilter filter in Enum.GetValues(typeof(ImportFilter)))
            {
                var f = filter;
                menu.AddItem(new GUIContent(GetImportFilterLabel(f)), _importFilter == f, () => SetImportFilter(f));
            }
            menu.DropDown(_importFilterBtn.worldBound);
        }

        private void SetImportFilter(ImportFilter filter)
        {
            if (_importFilter == filter) return;
            _importFilter = filter;
            EditorPrefs.SetInt(ImportFilterPrefKey, (int)filter);
            UpdateImportFilterButtonUI();
            UpdateItemList();
        }

        private void UpdateImportFilterButtonUI()
        {
            if (_importFilterBtn == null) return;
            _importFilterBtn.text = ToolImporterLoc.Tr("ImportFilterButton", GetImportFilterLabel(_importFilter));
            _importFilterBtn.tooltip = ToolImporterLoc.Tr("ImportFilterTooltip");
            // 既定 (全て) 以外で絞り込み中は青枠で示す
            _importFilterBtn.EnableInClassList("dennoko-button-active", _importFilter != ImportFilter.All);
        }

        private static string GetImportFilterLabel(ImportFilter filter)
        {
            switch (filter)
            {
                case ImportFilter.NotImported: return ToolImporterLoc.Tr("ImportFilterNotImported");
                case ImportFilter.Imported: return ToolImporterLoc.Tr("ImportFilterImported");
                default: return ToolImporterLoc.Tr("ImportFilterAll");
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
                try { shops = BlmDataAccessor.GetShopsWithItems(_dbPath); }
                catch (Exception e) { error = $"BLM のショップ一覧を読み込めませんでした: {e.Message}"; }
            }
            ShopManageDialog.Open(_shopManager, shops, error);
        }

        private static void OpenManual()
        {
            var path = GetManualPath();
            if (path == null || !File.Exists(path))
            {
                EditorUtility.DisplayDialog(WindowTitle, ToolImporterLoc.Tr("ManualNotFound", path ?? ManualRelativePath), "OK");
                return;
            }
            Application.OpenURL(new Uri(path).AbsoluteUri);
        }

        private static string GetManualPath()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DennokoworksToolImporterWindow).Assembly);
            var root = info != null && !string.IsNullOrEmpty(info.resolvedPath)
                ? info.resolvedPath
                : Path.GetFullPath(Path.Combine("Packages", PackageName));
            return Path.GetFullPath(Path.Combine(root, ManualRelativePath));
        }

        private void UpdateSettingsUI()
        {
            if (_settingsCard == null) return;
            _settingsCard.style.display = _showSettings ? DisplayStyle.Flex : DisplayStyle.None;
            if (_settingsToggleBtn != null)
                _settingsToggleBtn.EnableInClassList("dennoko-button-active", _showSettings);

            if (_showSettings)
            {
                bool dbManual = !string.IsNullOrEmpty(BlmDataAccessor.DbPathOverride);
                if (_dbPathValue != null)
                    _dbPathValue.text = string.IsNullOrEmpty(_dbPath) ? ToolImporterLoc.Tr("NotDetected") : _dbPath;
                if (_dbPathStatus != null)
                    _dbPathStatus.text = dbManual ? ToolImporterLoc.Tr("ManualStatus") : ToolImporterLoc.Tr("AutoStatus");
                if (_dbPathAutoBtn != null)
                    _dbPathAutoBtn.SetEnabled(dbManual);

                bool itemDirManual = !string.IsNullOrEmpty(BlmDataAccessor.ItemDirectoryOverride);
                if (_itemDirValue != null)
                    _itemDirValue.text = string.IsNullOrEmpty(_itemRoot) ? ToolImporterLoc.Tr("NotDetected") : _itemRoot;
                if (_itemDirStatus != null)
                    _itemDirStatus.text = itemDirManual ? ToolImporterLoc.Tr("ManualStatus") : ToolImporterLoc.Tr("AutoStatus");
                if (_itemDirAutoBtn != null)
                    _itemDirAutoBtn.SetEnabled(itemDirManual);
            }
        }

        private void UpdateLoadErrorUI()
        {
            if (_loadErrorContainer == null || _loadErrorLabel == null) return;
            if (!string.IsNullOrEmpty(_loadError))
            {
                _loadErrorContainer.style.display = DisplayStyle.Flex;
                _loadErrorLabel.text = _loadError;
            }
            else
            {
                _loadErrorContainer.style.display = DisplayStyle.None;
            }
        }

        private void UpdateQueueUI()
        {
            if (_queueCard == null) return;

            if (ImportQueue.IsBusy)
            {
                _queueCard.style.display = DisplayStyle.Flex;
                if (_queueSummaryContainer != null) _queueSummaryContainer.style.display = DisplayStyle.None;

                int total = Math.Max(ImportQueue.Total, 1);
                int processed = ImportQueue.Processed;
                var current = ImportQueue.CurrentLabel;
                var text = current != null
                    ? ToolImporterLoc.Tr("QueueImporting", processed + 1, total, current)
                    : ToolImporterLoc.Tr("QueueWaiting", processed, total);

                if (_queueProgressBar != null)
                {
                    _queueProgressBar.value = (float)processed / total * 100f;
                    _queueProgressBar.title = text;
                }
                if (_queuePendingLabel != null)
                    _queuePendingLabel.text = ToolImporterLoc.Tr("QueuePendingCount", ImportQueue.PendingCount);

                if (_queueSkipBtn != null) _queueSkipBtn.SetEnabled(current != null);
                if (_queueCancelBtn != null) _queueCancelBtn.SetEnabled(ImportQueue.PendingCount > 0);
            }
            else
            {
                _queueCard.style.display = DisplayStyle.None;
                var summary = ImportQueue.LastSummary;
                if (!string.IsNullOrEmpty(summary) && _queueSummaryContainer != null)
                {
                    _queueSummaryContainer.style.display = DisplayStyle.Flex;
                    if (_queueSummaryLabel != null) _queueSummaryLabel.text = summary;
                }
                else if (_queueSummaryContainer != null)
                {
                    _queueSummaryContainer.style.display = DisplayStyle.None;
                }
            }
        }

        private void UpdateActionBarUI()
        {
            if (_checkRemoteBtn != null)
            {
                _checkRemoteBtn.style.display = IsDennokoworks ? DisplayStyle.Flex : DisplayStyle.None;
                bool checking = IsAnyRemoteChecking();
                _checkRemoteBtn.text = checking ? ToolImporterLoc.Tr("CheckAllRemoteChecking") : ToolImporterLoc.Tr("CheckAllRemote");
                _checkRemoteBtn.SetEnabled(!checking);
            }
        }

        private void UpdateBatchBarUI()
        {
            if (_batchBar == null) return;
            _batchBar.style.display = _multiSelect ? DisplayStyle.Flex : DisplayStyle.None;
            if (_batchSelectUpdatesBtn != null)
                _batchSelectUpdatesBtn.style.display = IsDennokoworks ? DisplayStyle.Flex : DisplayStyle.None;

            int count = SelectedPackageCount;
            if (_batchImportBtn != null)
            {
                _batchImportBtn.text = ToolImporterLoc.Tr("BatchImport", count);
                _batchImportBtn.SetEnabled(count > 0 && !ImportQueue.IsBusy);
            }
        }

        private void UpdateFooterUI()
        {
            if (_openBoothShopBtn != null && _shopManager != null)
            {
                var shop = _shopManager.CurrentShop;
                _openBoothShopBtn.text = ToolImporterLoc.Tr("OpenBoothShop", shop.Label);
            }
        }

        private void SetupItemsListView()
        {
            if (_itemsListView == null) return;

            _itemsListView.itemsSource = _rows;
            _itemsListView.makeItem = () => new VisualElement();
            _itemsListView.bindItem = (element, index) =>
            {
                UnbindRowThumbnails(element);
                element.Clear();
                if (index >= 0 && index < _rows.Count) element.Add(CreateRowElement(_rows[index]));
            };
            _itemsListView.unbindItem = (element, index) => UnbindRowThumbnails(element);

            // 読み込み待ちのサムネイルを参照し続ける（参照が途切れると ThumbnailCache が取り消す）。
            // 表示中のテクスチャも参照して、上限超過時の破棄対象から外す。
            _itemsListView.schedule.Execute(TouchBoundThumbnails).Every(ThumbnailTouchIntervalMs);
        }

        private VisualElement CreateRowElement(ListRow row)
        {
            if (row.IsOtherToolsHeader)
            {
                var header = new Label(ToolImporterLoc.Tr("OtherLocalToolsTitle"));
                header.AddToClassList("other-tools-header");
                return header;
            }
            if (row.OtherTool != null) return CreateOtherToolElement(row.OtherTool);
            return CreateItemElement(row.State, _allShopsInList);
        }

        private void UnbindRowThumbnails(VisualElement element)
        {
            element.Query<Image>(className: "item-thumbnail").ForEach(img =>
            {
                _boundThumbnails.Remove(img);
                img.image = null;
            });
        }

        private void TouchBoundThumbnails()
        {
            if (_boundThumbnails.Count == 0) return;
            foreach (var kv in _boundThumbnails)
            {
                var tex = ThumbnailCache.Get(kv.Value);
                if (tex != null && kv.Key.image != tex) kv.Key.image = tex;
            }
        }

        private void UpdateItemList()
        {
            if (_itemsListView == null) return;

            bool allShops = _shopManager != null && _shopManager.IsAllShopsSelected;
            var shopLabel = allShops ? ToolImporterLoc.Tr("AllShops") : _shopManager.CurrentShop.Label;
            int scanning = PackageImporter.PendingCount;

            string headerText = ToolImporterLoc.Tr("ItemListHeader", shopLabel, _items.Count);
            if (scanning > 0) headerText += $"  - " + ToolImporterLoc.Tr("ScanningCount", scanning);
            if (_itemsCountLabel != null) _itemsCountLabel.text = headerText;

            if (_itemsWarningContainer != null)
            {
                if (_hiddenItemCount > 0)
                {
                    if (_itemsWarningIcon != null && _itemsWarningIcon.image == null)
                    {
                        var iconContent = EditorGUIUtility.IconContent("console.warnicon.sml");
                        if (iconContent != null && iconContent.image != null)
                            _itemsWarningIcon.image = iconContent.image;
                    }
                    if (_itemsWarningCount != null)
                        _itemsWarningCount.text = _hiddenItemCount.ToString();

                    var tooltip = ToolImporterLoc.Tr("HiddenItemsTooltip", _hiddenItemCount);
                    if (_hiddenArchiveCount > 0)
                        tooltip += ToolImporterLoc.Tr("HiddenArchivesTooltip", _hiddenArchiveCount);
                    _itemsWarningContainer.tooltip = tooltip;
                    _itemsWarningContainer.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _itemsWarningContainer.style.display = DisplayStyle.None;
                }
            }

            var visibleItems = new List<ToolItemState>();
            foreach (var state in _items)
            {
                if (IsListed(state)) visibleItems.Add(state);
            }

            if (visibleItems.Count == 0 && string.IsNullOrEmpty(_loadError))
            {
                if (_emptyMessageCard != null)
                {
                    _emptyMessageCard.style.display = DisplayStyle.Flex;
                    var target = allShops ? ToolImporterLoc.Tr("InBlm") : ToolImporterLoc.Tr("InBlmShop", shopLabel);
                    // アイテムはあるが検索・フィルターで全て外れた場合は、BLM が空だという案内を出さない
                    if (_emptyMessageLabel != null)
                        _emptyMessageLabel.text = _items.Count > 0
                            ? ToolImporterLoc.Tr("NoFilteredItemsMessage")
                            : ToolImporterLoc.Tr("NoItemsMessage", target);
                }
            }
            else
            {
                if (_emptyMessageCard != null) _emptyMessageCard.style.display = DisplayStyle.None;
            }

            _allShopsInList = allShops;
            _rows.Clear();
            foreach (var state in visibleItems) _rows.Add(new ListRow { State = state });
            AppendOtherLocalToolRows();

            // 行要素をすべて bind し直す。RefreshItems は DynamicHeight だと非表示中の行を
            // bind し直さず、後で表示されたときに古い内容のまま出るため使わない。
            _itemsListView.Rebuild();
        }

        private VisualElement CreateItemElement(ToolItemState state, bool allShops)
        {
            var item = state.Item;
            var card = new VisualElement();
            card.AddToClassList("item-card");

            if (_multiSelect)
            {
                var toggle = new Toggle();
                toggle.AddToClassList("item-card-checkbox");
                toggle.value = _selection.ContainsKey(item.RegisteredId);
                toggle.SetEnabled(state.LatestPackage != null);
                toggle.RegisterValueChangedCallback(evt =>
                {
                    SetSelected(state, evt.newValue);
                    UpdateBatchBarUI();
                    UpdateItemList();
                });
                card.Add(toggle);
            }

            var thumb = new Image();
            thumb.AddToClassList("item-thumbnail");
            if (!string.IsNullOrEmpty(item.ThumbnailUrl))
            {
                thumb.image = ThumbnailCache.Get(item.ThumbnailUrl);
                _boundThumbnails[thumb] = item.ThumbnailUrl;
            }
            card.Add(thumb);

            var body = new VisualElement();
            body.AddToClassList("item-body");

            var title = new Label(item.Name);
            title.AddToClassList("item-title");
            body.Add(title);

            if (allShops && !string.IsNullOrEmpty(item.ShopSubdomain))
            {
                var shopSub = new Label(GetShopLabel(item.ShopSubdomain, item.ShopName));
                shopSub.AddToClassList("item-sub-label");
                body.Add(shopSub);
            }

            if (_multiSelect && state.Packages.Count > 1
                && _selection.TryGetValue(item.RegisteredId, out var selectedPaths))
            {
                var names = selectedPaths.ConvertAll(path => state.Packages.Find(p => p.Path == path)?.RelativePath ?? Path.GetFileName(path));
                var batchNotice = new Label(ToolImporterLoc.Tr("BatchTargetsLabel", string.Join(", ", names)));
                batchNotice.AddToClassList("item-sub-label");
                body.Add(batchNotice);
            }

            if (IsDennokoworks)
            {
                var local = state.LocalTool;
                if (local != null && local.HasGitHubRepo)
                {
                    var remote = DennokoVersionChecker.GetCachedResult(local.GitHubOwner, local.GitHubRepo);
                    if (remote != null && remote.state == RemoteVersionState.Success
                        && DennokoVersionChecker.IsNewer(remote.version, local.Version)
                        && (string.IsNullOrEmpty(state.BlmVersion) || DennokoVersionChecker.IsNewer(remote.version, state.BlmVersion)))
                    {
                        var vBox = new VisualElement();
                        vBox.AddToClassList("item-version-box");

                        var vText = new Label(ToolImporterLoc.Tr("NewVersionAvailable", DennokoVersionChecker.Display(remote.version))
                            + (string.IsNullOrEmpty(remote.message) ? "" : $"\n{remote.message}"));
                        vText.AddToClassList("item-version-text");
                        vBox.Add(vText);

                        if (!string.IsNullOrEmpty(item.BoothUrl))
                        {
                            var boothBtn = new Button(() => Application.OpenURL(item.BoothUrl))
                            {
                                text = ToolImporterLoc.Tr("OpenBoothItemPage")
                            };
                            boothBtn.AddToClassList("mini-button");
                            boothBtn.style.alignSelf = Align.FlexEnd;
                            vBox.Add(boothBtn);
                        }
                        body.Add(vBox);
                    }
                }
            }

            var actionsRow = new VisualElement();
            actionsRow.AddToClassList("item-actions-row");

            var leftActions = new VisualElement();
            leftActions.AddToClassList("item-actions-left");

            var boothBtnLeft = new Button(() => { if (!string.IsNullOrEmpty(item.BoothUrl)) Application.OpenURL(item.BoothUrl); })
            {
                text = ToolImporterLoc.Tr("BoothButton")
            };
            boothBtnLeft.AddToClassList("mini-button");
            boothBtnLeft.SetEnabled(!string.IsNullOrEmpty(item.BoothUrl));
            leftActions.Add(boothBtnLeft);

            var folderBtn = new Button(() => { if (state.DirectoryExists) EditorUtility.RevealInFinder(item.ItemDirectory); })
            {
                text = ToolImporterLoc.Tr("FolderButton")
            };
            folderBtn.AddToClassList("mini-button");
            folderBtn.SetEnabled(state.DirectoryExists);
            leftActions.Add(folderBtn);

            actionsRow.Add(leftActions);

            var importBtn = CreateImportButton(state);
            actionsRow.Add(importBtn);

            body.Add(actionsRow);
            card.Add(body);
            return card;
        }

        private VisualElement CreateImportButton(ToolItemState state)
        {
            var latest = state.LatestPackage;
            if (latest == null)
            {
                var btn = new Button { text = ToolImporterLoc.Tr("NoPackage") };
                btn.AddToClassList("import-button");
                btn.SetEnabled(false);
                return btn;
            }

            bool hasUpdate = HasUpdate(state);
            bool isInstalled = state.InstallState == InstallState.Installed;
            string labelText = hasUpdate ? ToolImporterLoc.Tr("ImportUpdate")
                : (isInstalled ? ToolImporterLoc.Tr("Reimport") : ToolImporterLoc.Tr("Import"));

            bool multiple = state.Packages.Count > 1;
            if (multiple) labelText += " ▾";

            var importBtn = new Button();
            importBtn.text = labelText;
            importBtn.AddToClassList("import-button");

            if (hasUpdate) importBtn.AddToClassList("import-button-update");
            else if (isInstalled) importBtn.AddToClassList("import-button-reimport");
            else importBtn.AddToClassList("import-button-new");

            importBtn.SetEnabled(!ImportQueue.IsBusy);

            importBtn.clicked += () =>
            {
                if (!multiple)
                {
                    PackageImporter.ImportPackage(latest.Path);
                    return;
                }

                _selection.TryGetValue(state.Item.RegisteredId, out var selectedPaths);
                var popup = new PackageSelectPopup(
                    state.Item.Name,
                    state.Packages,
                    _multiSelect ? selectedPaths : null,
                    packages => ImportPackages(state, packages),
                    _multiSelect ? packages => SetSelectedPackages(state, packages) : (Action<List<PackageFile>>)null);

                var screenRect = importBtn.worldBound;
                UnityEditor.PopupWindow.Show(screenRect, popup);
            };

            return importBtn;
        }

        private void AppendOtherLocalToolRows()
        {
            // プロジェクトに導入済みのツールなので「未インポート」では出さない
            if (!IsDennokoworks || _localOnlyTools.Count == 0 || _importFilter == ImportFilter.NotImported) return;

            bool headerAdded = false;
            foreach (var tool in _localOnlyTools)
            {
                if (!MatchesSearch(tool.ToolFolder)) continue;
                if (!headerAdded)
                {
                    _rows.Add(new ListRow { IsOtherToolsHeader = true });
                    headerAdded = true;
                }
                _rows.Add(new ListRow { OtherTool = tool });
            }
        }

        private VisualElement CreateOtherToolElement(LocalToolVersion tool)
        {
            var remote = tool.HasGitHubRepo ? DennokoVersionChecker.GetCachedResult(tool.GitHubOwner, tool.GitHubRepo) : null;
            bool hasUpdate = remote != null && remote.state == RemoteVersionState.Success
                && DennokoVersionChecker.IsNewer(remote.version, tool.Version);

            var card = new VisualElement();
            card.AddToClassList("item-card");

            var body = new VisualElement();
            body.AddToClassList("item-body");

            var nameLabel = new Label(tool.ToolFolder);
            nameLabel.AddToClassList("item-title");
            body.Add(nameLabel);

            if (hasUpdate)
            {
                var notice = new Label(ToolImporterLoc.Tr("OtherToolUpdateAvailable", DennokoVersionChecker.Display(remote.version)));
                notice.AddToClassList("item-sub-label");
                notice.AddToClassList("dennoko-text-warning");
                body.Add(notice);
            }

            card.Add(body);

            var actions = new VisualElement();
            actions.AddToClassList("item-actions-left");
            actions.style.alignSelf = Align.Center;

            var pingBtn = new Button(() =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tool.VersionJsonPath);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            })
            {
                text = ToolImporterLoc.Tr("PingButton")
            };
            pingBtn.AddToClassList("mini-button");
            actions.Add(pingBtn);

            var boothBtn = new Button(() =>
            {
                if (_shopManager?.CurrentShop != null)
                    Application.OpenURL(_shopManager.CurrentShop.ShopUrl);
            })
            {
                text = ToolImporterLoc.Tr("BoothButton")
            };
            boothBtn.AddToClassList("mini-button");
            if (hasUpdate) boothBtn.AddToClassList("import-button-update");
            actions.Add(boothBtn);

            card.Add(actions);
            return card;
        }

        private void OnThumbnailLoaded() => TouchBoundThumbnails();

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

        /// <summary>検索文字列とインポート状況フィルターの両方に合う（一覧に出す）アイテムか。</summary>
        private bool IsListed(ToolItemState state) => MatchesImportFilter(state) && MatchesSearch(state);

        private bool MatchesImportFilter(ToolItemState state)
        {
            switch (_importFilter)
            {
                case ImportFilter.NotImported:
                    return state.InstallState == InstallState.NotInstalled;
                case ImportFilter.Imported:
                    // 一部だけ導入済みのものもインポート済みとして扱う
                    return state.InstallState == InstallState.Installed
                        || state.InstallState == InstallState.PartiallyInstalled;
                default:
                    return true;
            }
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

        private void SetStatus(string message, StatusType type, long autoResetMs = 3000)
        {
            if (_statusLabel == null) return;

            _statusLabel.text = message;
            _statusLabel.EnableInClassList("dennoko-status--success", type == StatusType.Success);
            _statusLabel.EnableInClassList("dennoko-status--error",   type == StatusType.Error);

            _statusResetSchedule?.Pause();
            if (type != StatusType.Info)
            {
                _statusResetSchedule = _statusLabel.schedule
                    .Execute(() => SetStatus(ToolImporterLoc.Tr("StatusReady"), StatusType.Info))
                    .StartingIn(autoResetMs);
            }
        }

        #endregion
    }
}

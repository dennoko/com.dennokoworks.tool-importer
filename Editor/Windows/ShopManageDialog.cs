using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>ショップの追加・表示名編集・削除を行うダイアログ。</summary>
    internal sealed class ShopManageDialog : EditorWindow
    {
        private ShopManager _shopManager;
        private List<BlmShop> _blmShops;
        private string _blmError;

        private string _search = "";
        private string _manualSubdomain = "";
        private string _manualDisplayName = "";
        private Vector2 _registeredScroll;
        private Vector2 _blmScroll;

        public static void Open(ShopManager shopManager, List<BlmShop> blmShops, string blmError)
        {
            var window = GetWindow<ShopManageDialog>(true, ToolImporterLoc.Tr("ShopManageTitle"), true);
            window._shopManager = shopManager;
            window._blmShops = blmShops;
            window._blmError = blmError;
            window.minSize = new Vector2(420, 480);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (_shopManager == null)
            {
                EditorGUILayout.HelpBox(ToolImporterLoc.CurrentLanguage == Language.English
                    ? "Please reopen this dialog from the Tool Importer window."
                    : "ツールウィンドウから開き直してください。", MessageType.Info);
                if (GUILayout.Button(ToolImporterLoc.Tr("ShopManageClose"))) Close();
                return;
            }

            DrawDefaultShopSelector();
            DrawRegisteredShops();
            EditorGUILayout.Space(8);
            DrawBlmShops();
            EditorGUILayout.Space(8);
            DrawManualInput();
        }

        private void DrawDefaultShopSelector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(ToolImporterLoc.Tr("ShopManageDefaultShop"), EditorStyles.boldLabel);
                var options = new List<string> { ToolImporterLoc.Tr("ShopManageDefaultAll") };
                var values = new List<string> { string.Empty };
                int currentIndex = 0;
                for (int i = 0; i < _shopManager.Shops.Count; i++)
                {
                    var s = _shopManager.Shops[i];
                    options.Add($"{s.Label}  ({s.subdomain})");
                    values.Add(s.subdomain);
                    if (_shopManager.IsDefaultShop(s.subdomain))
                    {
                        currentIndex = i + 1;
                    }
                }

                int selectedIndex = EditorGUILayout.Popup(ToolImporterLoc.Tr("ShopManageDefaultLabel"), currentIndex, options.ToArray());
                if (selectedIndex != currentIndex)
                {
                    _shopManager.DefaultShopSubdomain = values[selectedIndex];
                }
            }
            EditorGUILayout.Space(4);
        }

        private void DrawRegisteredShops()
        {
            EditorGUILayout.LabelField(ToolImporterLoc.Tr("ShopManageRegisteredShops"), EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(_registeredScroll, EditorStyles.helpBox, GUILayout.MaxHeight(150)))
            {
                _registeredScroll = scroll.scrollPosition;

                ShopInfo toRemove = null;
                foreach (var shop in _shopManager.Shops)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool isBuiltin = ShopManager.IsBuiltin(shop.subdomain);
                        bool isDefault = _shopManager.IsDefaultShop(shop.subdomain);

                        using (new EditorGUI.DisabledScope(isBuiltin))
                        {
                            var newName = EditorGUILayout.DelayedTextField(shop.displayName ?? "", GUILayout.MinWidth(110));
                            if (newName != (shop.displayName ?? "")) _shopManager.UpdateShop(shop.subdomain, newName);
                        }

                        EditorGUILayout.LabelField(shop.subdomain, EditorStyles.miniLabel, GUILayout.MinWidth(70));

                        string defaultBadge = ToolImporterLoc.CurrentLanguage == Language.English ? "[Default]" : "[既定]";
                        string defaultBtn = ToolImporterLoc.CurrentLanguage == Language.English ? "Default" : "既定";
                        string openBtn = ToolImporterLoc.CurrentLanguage == Language.English ? "Open" : "開く";
                        string deleteBtn = ToolImporterLoc.CurrentLanguage == Language.English ? "Delete" : "削除";

                        if (isDefault)
                        {
                            GUILayout.Label(defaultBadge, EditorStyles.miniBoldLabel, GUILayout.Width(46));
                        }
                        else
                        {
                            if (GUILayout.Button(defaultBtn, EditorStyles.miniButton, GUILayout.Width(46)))
                            {
                                _shopManager.DefaultShopSubdomain = shop.subdomain;
                            }
                        }

                        if (GUILayout.Button(openBtn, EditorStyles.miniButtonLeft, GUILayout.Width(38)))
                            Application.OpenURL(shop.ShopUrl);

                        using (new EditorGUI.DisabledScope(isBuiltin))
                        {
                            if (GUILayout.Button(deleteBtn, EditorStyles.miniButtonRight, GUILayout.Width(38)))
                                toRemove = shop;
                        }
                    }
                }

                if (toRemove != null)
                {
                    string delTitle = ToolImporterLoc.CurrentLanguage == Language.English ? "Remove Shop" : "ショップの削除";
                    string delMsg = ToolImporterLoc.CurrentLanguage == Language.English
                        ? $"Remove '{toRemove.Label}' from registered shops?"
                        : $"「{toRemove.Label}」を一覧から削除しますか？";
                    string delOk = ToolImporterLoc.CurrentLanguage == Language.English ? "Remove" : "削除";
                    string delCancel = ToolImporterLoc.CurrentLanguage == Language.English ? "Cancel" : "キャンセル";

                    if (EditorUtility.DisplayDialog(delTitle, delMsg, delOk, delCancel))
                    {
                        _shopManager.RemoveShop(toRemove.subdomain);
                    }
                }
            }
            string hint = ToolImporterLoc.CurrentLanguage == Language.English
                ? "Display names are saved on pressing Enter. dennokoworks cannot be removed."
                : "表示名は入力後 Enter で保存されます。dennokoworks は登録解除できません。";
            EditorGUILayout.LabelField(hint, EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawBlmShops()
        {
            EditorGUILayout.LabelField(ToolImporterLoc.Tr("ShopManageBlmShops"), EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_blmError))
            {
                EditorGUILayout.HelpBox(_blmError, MessageType.Warning);
                return;
            }
            if (_blmShops == null || _blmShops.Count == 0)
            {
                EditorGUILayout.HelpBox(ToolImporterLoc.CurrentLanguage == Language.English
                    ? "No shops with registered items found in BLM."
                    : "BLM にアイテムが登録されたショップが見つかりませんでした。", MessageType.Info);
                return;
            }

            _search = EditorGUILayout.TextField(ToolImporterLoc.Tr("SearchLabel"), _search);

            using (var scroll = new EditorGUILayout.ScrollViewScope(_blmScroll, EditorStyles.helpBox, GUILayout.MinHeight(120)))
            {
                _blmScroll = scroll.scrollPosition;
                foreach (var shop in _blmShops)
                {
                    if (!Matches(shop, _search)) continue;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(new GUIContent(shop.Name, shop.Subdomain), GUILayout.MinWidth(120));
                        string itemUnit = ToolImporterLoc.CurrentLanguage == Language.English ? "items" : "件";
                        EditorGUILayout.LabelField($"{shop.ItemCount} {itemUnit}", EditorStyles.miniLabel, GUILayout.Width(50));

                        bool registered = _shopManager.Find(shop.Subdomain) != null;
                        using (new EditorGUI.DisabledScope(registered))
                        {
                            string registeredText = ToolImporterLoc.CurrentLanguage == Language.English ? "Added" : "登録済み";
                            string addText = ToolImporterLoc.CurrentLanguage == Language.English ? "Add" : "追加";
                            if (GUILayout.Button(registered ? registeredText : addText, EditorStyles.miniButton, GUILayout.Width(64)))
                                _shopManager.AddShop(shop.Subdomain, shop.Name);
                        }
                    }
                }
            }
        }

        private void DrawManualInput()
        {
            EditorGUILayout.LabelField(ToolImporterLoc.Tr("ShopManageManualInput"), EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string subLabel = ToolImporterLoc.CurrentLanguage == Language.English ? "Subdomain" : "サブドメイン";
                string subTooltip = ToolImporterLoc.CurrentLanguage == Language.English ? "e.g. dennokoworks (URL is also supported)" : "例: dennokoworks（https://dennokoworks.booth.pm/ の URL も可）";
                string nameLabel = ToolImporterLoc.CurrentLanguage == Language.English ? "Display Name (Optional)" : "表示名（任意）";
                string addBtn = ToolImporterLoc.CurrentLanguage == Language.English ? "Add" : "追加";

                _manualSubdomain = EditorGUILayout.TextField(new GUIContent(subLabel, subTooltip), _manualSubdomain);
                _manualDisplayName = EditorGUILayout.TextField(nameLabel, _manualDisplayName);

                var subdomain = ShopManager.NormalizeSubdomain(_manualSubdomain);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(subdomain)))
                {
                    if (GUILayout.Button(addBtn))
                    {
                        var displayName = string.IsNullOrWhiteSpace(_manualDisplayName)
                            ? FindBlmShopName(subdomain) ?? subdomain
                            : _manualDisplayName.Trim();
                        _shopManager.AddShop(subdomain, displayName);
                        _manualSubdomain = "";
                        _manualDisplayName = "";
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        private string FindBlmShopName(string subdomain)
        {
            if (_blmShops == null) return null;
            foreach (var shop in _blmShops)
            {
                if (string.Equals(shop.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase)) return shop.Name;
            }
            return null;
        }

        private static bool Matches(BlmShop shop, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            search = search.Trim();
            return (shop.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || (shop.Subdomain ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

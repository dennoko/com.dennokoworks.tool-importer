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
                EditorGUILayout.HelpBox(ToolImporterLoc.Tr("ShopManageReopen"), MessageType.Info);
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

                        if (isDefault)
                        {
                            GUILayout.Label(ToolImporterLoc.Tr("ShopManageDefaultBadge"), EditorStyles.miniBoldLabel, GUILayout.Width(46));
                        }
                        else
                        {
                            if (GUILayout.Button(ToolImporterLoc.Tr("ShopManageSetDefault"), EditorStyles.miniButton, GUILayout.Width(46)))
                            {
                                _shopManager.DefaultShopSubdomain = shop.subdomain;
                            }
                        }

                        if (GUILayout.Button(ToolImporterLoc.Tr("ShopManageOpen"), EditorStyles.miniButtonLeft, GUILayout.Width(38)))
                            Application.OpenURL(shop.ShopUrl);

                        using (new EditorGUI.DisabledScope(isBuiltin))
                        {
                            if (GUILayout.Button(ToolImporterLoc.Tr("ShopManageDelete"), EditorStyles.miniButtonRight, GUILayout.Width(38)))
                                toRemove = shop;
                        }
                    }
                }

                if (toRemove != null
                    && EditorUtility.DisplayDialog(
                        ToolImporterLoc.Tr("ShopManageDeleteTitle"),
                        ToolImporterLoc.Tr("ShopManageDeleteMessage", toRemove.Label),
                        ToolImporterLoc.Tr("ShopManageDeleteOk"),
                        ToolImporterLoc.Tr("Cancel")))
                {
                    _shopManager.RemoveShop(toRemove.subdomain);
                }
            }
            EditorGUILayout.LabelField(ToolImporterLoc.Tr("ShopManageNameHint"), EditorStyles.wordWrappedMiniLabel);
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
                EditorGUILayout.HelpBox(ToolImporterLoc.Tr("ShopManageNoBlmShops"), MessageType.Info);
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
                        EditorGUILayout.LabelField(ToolImporterLoc.Tr("ShopManageItemCount", shop.ItemCount), EditorStyles.miniLabel, GUILayout.Width(50));

                        bool registered = _shopManager.Find(shop.Subdomain) != null;
                        using (new EditorGUI.DisabledScope(registered))
                        {
                            if (GUILayout.Button(ToolImporterLoc.Tr(registered ? "ShopManageRegistered" : "ShopManageAdd"), EditorStyles.miniButton, GUILayout.Width(64)))
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
                _manualSubdomain = EditorGUILayout.TextField(
                    new GUIContent(ToolImporterLoc.Tr("ShopManageSubdomain"), ToolImporterLoc.Tr("ShopManageSubdomainTooltip")),
                    _manualSubdomain);
                _manualDisplayName = EditorGUILayout.TextField(ToolImporterLoc.Tr("ShopManageDisplayName"), _manualDisplayName);

                var subdomain = ShopManager.NormalizeSubdomain(_manualSubdomain);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(subdomain)))
                {
                    if (GUILayout.Button(ToolImporterLoc.Tr("ShopManageAdd")))
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

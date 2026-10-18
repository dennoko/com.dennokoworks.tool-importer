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
            var window = GetWindow<ShopManageDialog>(true, "ショップの管理", true);
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
                // ドメインリロードで参照が切れた場合
                EditorGUILayout.HelpBox("ツールウィンドウから開き直してください。", MessageType.Info);
                if (GUILayout.Button("閉じる")) Close();
                return;
            }

            DrawRegisteredShops();
            EditorGUILayout.Space(8);
            DrawBlmShops();
            EditorGUILayout.Space(8);
            DrawManualInput();
        }

        private void DrawRegisteredShops()
        {
            EditorGUILayout.LabelField("登録済みショップ", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(_registeredScroll, EditorStyles.helpBox, GUILayout.MaxHeight(150)))
            {
                _registeredScroll = scroll.scrollPosition;

                ShopInfo toRemove = null;
                foreach (var shop in _shopManager.Shops)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool isDefault = ShopManager.IsDefault(shop.subdomain);
                        using (new EditorGUI.DisabledScope(isDefault))
                        {
                            var newName = EditorGUILayout.DelayedTextField(shop.displayName ?? "", GUILayout.MinWidth(120));
                            if (newName != (shop.displayName ?? "")) _shopManager.UpdateShop(shop.subdomain, newName);
                        }

                        EditorGUILayout.LabelField(shop.subdomain, EditorStyles.miniLabel, GUILayout.MinWidth(80));

                        if (GUILayout.Button("開く", EditorStyles.miniButtonLeft, GUILayout.Width(40)))
                            Application.OpenURL(shop.ShopUrl);

                        using (new EditorGUI.DisabledScope(isDefault))
                        {
                            if (GUILayout.Button("削除", EditorStyles.miniButtonRight, GUILayout.Width(40)))
                                toRemove = shop;
                        }
                    }
                }

                if (toRemove != null
                    && EditorUtility.DisplayDialog("ショップの削除", $"「{toRemove.Label}」を一覧から削除しますか？", "削除", "キャンセル"))
                {
                    _shopManager.RemoveShop(toRemove.subdomain);
                }
            }
            EditorGUILayout.LabelField("表示名は入力後 Enter で保存されます。dennokoworks は削除できません。", EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawBlmShops()
        {
            EditorGUILayout.LabelField("BLM のショップから追加", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_blmError))
            {
                EditorGUILayout.HelpBox(_blmError, MessageType.Warning);
                return;
            }
            if (_blmShops == null || _blmShops.Count == 0)
            {
                EditorGUILayout.HelpBox("BLM にアイテムが登録されたショップが見つかりませんでした。", MessageType.Info);
                return;
            }

            _search = EditorGUILayout.TextField("検索", _search);

            using (var scroll = new EditorGUILayout.ScrollViewScope(_blmScroll, EditorStyles.helpBox, GUILayout.MinHeight(120)))
            {
                _blmScroll = scroll.scrollPosition;
                foreach (var shop in _blmShops)
                {
                    if (!Matches(shop, _search)) continue;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(new GUIContent(shop.Name, shop.Subdomain), GUILayout.MinWidth(120));
                        EditorGUILayout.LabelField($"{shop.ItemCount} 件", EditorStyles.miniLabel, GUILayout.Width(50));

                        bool registered = _shopManager.Find(shop.Subdomain) != null;
                        using (new EditorGUI.DisabledScope(registered))
                        {
                            if (GUILayout.Button(registered ? "登録済み" : "追加", EditorStyles.miniButton, GUILayout.Width(64)))
                                _shopManager.AddShop(shop.Subdomain, shop.Name);
                        }
                    }
                }
            }
        }

        private void DrawManualInput()
        {
            EditorGUILayout.LabelField("サブドメインを直接入力して追加", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _manualSubdomain = EditorGUILayout.TextField(new GUIContent("サブドメイン", "例: dennokoworks（https://dennokoworks.booth.pm/ の URL も可）"), _manualSubdomain);
                _manualDisplayName = EditorGUILayout.TextField("表示名（任意）", _manualDisplayName);

                var subdomain = ShopManager.NormalizeSubdomain(_manualSubdomain);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(subdomain)))
                {
                    if (GUILayout.Button("追加"))
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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// 表示対象ショップの登録・切り替えを管理し、EditorPrefs（ユーザー単位・全プロジェクト共通）に保存する。
    /// デフォルトの dennokoworks は常に先頭に存在し、削除できない。
    /// </summary>
    internal sealed class ShopManager
    {
        public const string DefaultSubdomain = "dennokoworks";

        private const string ShopsPrefKey = "Dennokoworks.ToolImporter.Shops";
        private const string CurrentPrefKey = "Dennokoworks.ToolImporter.CurrentShop";

        [Serializable]
        private class ShopList
        {
            public List<ShopInfo> shops = new List<ShopInfo>();
        }

        private readonly List<ShopInfo> _shops = new List<ShopInfo>();
        private string _currentSubdomain = DefaultSubdomain;

        public event Action Changed;

        public IReadOnlyList<ShopInfo> Shops => _shops;

        /// <summary>ショップで絞り込まない（全ショップのアイテムを表示する）選択肢。subdomain は空。</summary>
        public static readonly ShopInfo AllShops = new ShopInfo(string.Empty, "None");

        public ShopInfo CurrentShop
        {
            get
            {
                if (IsAllShopsSelected) return AllShops;
                var shop = Find(_currentSubdomain);
                return shop ?? _shops[0];
            }
        }

        public bool IsAllShopsSelected => string.IsNullOrEmpty(_currentSubdomain);

        public bool IsDefaultShopSelected => !IsAllShopsSelected && IsDefault(CurrentShop.subdomain);

        public ShopManager()
        {
            Load();
        }

        public static bool IsDefault(string subdomain)
            => string.Equals(subdomain, DefaultSubdomain, StringComparison.OrdinalIgnoreCase);

        /// <summary>BOOTH の URL などが入力された場合もサブドメインに正規化する。</summary>
        public static string NormalizeSubdomain(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var s = input.Trim().ToLowerInvariant();
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            const string boothSuffix = ".booth.pm";
            if (s.EndsWith(boothSuffix, StringComparison.Ordinal)) s = s.Substring(0, s.Length - boothSuffix.Length);
            return s;
        }

        public ShopInfo Find(string subdomain)
        {
            foreach (var shop in _shops)
            {
                if (string.Equals(shop.subdomain, subdomain, StringComparison.OrdinalIgnoreCase)) return shop;
            }
            return null;
        }

        /// <summary>ショップを選択する。null / 空文字なら None（全ショップ）。</summary>
        public void Select(string subdomain)
        {
            subdomain = subdomain ?? string.Empty;
            if (subdomain.Length > 0 && Find(subdomain) == null) return;
            _currentSubdomain = subdomain;
            EditorPrefs.SetString(CurrentPrefKey, subdomain);
            Changed?.Invoke();
        }

        /// <summary>ショップを追加する。既に登録済みの場合は表示名を更新する。</summary>
        public void AddShop(string subdomain, string displayName)
        {
            subdomain = NormalizeSubdomain(subdomain);
            if (string.IsNullOrEmpty(subdomain)) return;

            var existing = Find(subdomain);
            if (existing != null)
            {
                existing.displayName = displayName;
            }
            else
            {
                _shops.Add(new ShopInfo(subdomain, displayName));
            }
            Save();
        }

        public void UpdateShop(string subdomain, string displayName)
        {
            var shop = Find(subdomain);
            if (shop == null) return;
            shop.displayName = displayName;
            Save();
        }

        public void RemoveShop(string subdomain)
        {
            if (IsDefault(subdomain)) return;
            var shop = Find(subdomain);
            if (shop == null) return;
            _shops.Remove(shop);
            if (string.Equals(_currentSubdomain, subdomain, StringComparison.OrdinalIgnoreCase))
            {
                _currentSubdomain = DefaultSubdomain;
                EditorPrefs.SetString(CurrentPrefKey, _currentSubdomain);
            }
            Save();
        }

        public void Save()
        {
            var list = new ShopList { shops = new List<ShopInfo>(_shops) };
            EditorPrefs.SetString(ShopsPrefKey, JsonUtility.ToJson(list));
            Changed?.Invoke();
        }

        public void Load()
        {
            _shops.Clear();
            var json = EditorPrefs.GetString(ShopsPrefKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var list = JsonUtility.FromJson<ShopList>(json);
                    if (list?.shops != null)
                    {
                        foreach (var shop in list.shops)
                        {
                            if (shop == null || string.IsNullOrEmpty(shop.subdomain) || Find(shop.subdomain) != null) continue;
                            _shops.Add(shop);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Tool Importer] ショップ設定の読み込みに失敗しました: {e.Message}");
                }
            }

            // デフォルトショップは常に先頭
            var def = Find(DefaultSubdomain);
            if (def != null) _shops.Remove(def);
            _shops.Insert(0, def ?? new ShopInfo(DefaultSubdomain, "dennokoworks"));

            _currentSubdomain = EditorPrefs.GetString(CurrentPrefKey, DefaultSubdomain);
            if (!IsAllShopsSelected && Find(_currentSubdomain) == null) _currentSubdomain = DefaultSubdomain;
        }
    }
}

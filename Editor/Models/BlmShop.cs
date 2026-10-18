using System;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>BLM の shops テーブル 1 行分（登録アイテム数付き）。</summary>
    internal sealed class BlmShop
    {
        public string Subdomain;
        public string Name;
        public string ThumbnailUrl;
        public int ItemCount;
    }

    /// <summary>本ツールに登録されたショップ。EditorPrefs に JSON で保存する。</summary>
    [Serializable]
    internal sealed class ShopInfo
    {
        public string subdomain;
        public string displayName;

        public ShopInfo() { }

        public ShopInfo(string subdomain, string displayName)
        {
            this.subdomain = subdomain;
            this.displayName = displayName;
        }

        public string Label => string.IsNullOrEmpty(displayName) ? subdomain : displayName;

        public string ShopUrl => $"https://{subdomain}.booth.pm/";
    }
}

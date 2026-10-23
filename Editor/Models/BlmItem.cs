namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>BLM に登録済みの BOOTH アイテム 1 件。</summary>
    internal sealed class BlmItem
    {
        /// <summary>registered_items.id。保存先のフォルダ名でもある（例: "b7684274"）。</summary>
        public string RegisteredId;
        public long BoothItemId;
        public string Name;
        public string ShopSubdomain;
        public string ShopName;
        public string ThumbnailUrl;

        /// <summary>[保存先]/[RegisteredId] の絶対パス。</summary>
        public string ItemDirectory;

        /// <summary>
        /// BOOTH の商品ページ URL。BLM の DB は URL 自体を保存していないため、
        /// booth_items.id と shop_subdomain から BOOTH の正規 URL を組み立てる。
        /// </summary>
        public string BoothUrl
        {
            get
            {
                if (BoothItemId <= 0) return null;
                return string.IsNullOrEmpty(ShopSubdomain)
                    ? $"https://booth.pm/ja/items/{BoothItemId}"
                    : $"https://{ShopSubdomain}.booth.pm/items/{BoothItemId}";
            }
        }
    }
}

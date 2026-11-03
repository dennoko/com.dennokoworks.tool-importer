using System.Collections.Generic;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>BLM でユーザーが作成したリスト（lists テーブル）1 件と、その所属アイテム。</summary>
    internal sealed class BlmList
    {
        /// <summary>lists.id。</summary>
        public long Id;
        public string Title;

        /// <summary>所属アイテムの registered_items.id（= BlmItem.RegisteredId）。</summary>
        public readonly HashSet<string> ItemIds = new HashSet<string>();
    }
}

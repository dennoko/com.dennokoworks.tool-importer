using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// BOOTH Library Manager の data.db とアイテム保存先を読み取る。
    /// DB は読み取り専用で開き、開けない場合は一時フォルダへコピーしてから読む。
    /// </summary>
    internal static class BlmDataAccessor
    {
        private const string BlmFolderName = "pm.booth.library-manager";
        private const string DbFileName = "data.db";

        private const string DbPathPrefKey = "Dennokoworks.ToolImporter.BlmDbPathOverride";
        private const string ItemDirPrefKey = "Dennokoworks.ToolImporter.BlmItemDirOverride";

        /// <summary>手動指定された DB パス（空なら自動検出）。</summary>
        public static string DbPathOverride
        {
            get => EditorPrefs.GetString(DbPathPrefKey, "");
            set => EditorPrefs.SetString(DbPathPrefKey, value ?? "");
        }

        /// <summary>手動指定されたアイテム保存先（空なら DB の設定値）。</summary>
        public static string ItemDirectoryOverride
        {
            get => EditorPrefs.GetString(ItemDirPrefKey, "");
            set => EditorPrefs.SetString(ItemDirPrefKey, value ?? "");
        }

        /// <summary>OS 標準の data.db の場所を返す。見つからなければ null。</summary>
        public static string DetectBlmDatabasePath()
        {
            foreach (var candidate in GetDefaultDatabaseCandidates())
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>手動指定があればそれを、無ければ自動検出した DB パスを返す。</summary>
        public static string ResolveDatabasePath()
        {
            var manual = DbPathOverride;
            if (!string.IsNullOrEmpty(manual)) return manual;
            return DetectBlmDatabasePath();
        }

        private static IEnumerable<string> GetDefaultDatabaseCandidates()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                yield return Path.Combine(home, "Library", "Application Support", BlmFolderName, DbFileName);
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                yield return Path.Combine(appData, BlmFolderName, DbFileName);
            }
        }

        /// <summary>BLM の preferences に設定されたアイテム保存先を返す。取得できなければ null。</summary>
        public static string GetItemDirectoryPath(string dbPath)
        {
            var rows = Query(dbPath, "SELECT item_directory_path FROM preferences ORDER BY id DESC LIMIT 1");
            if (rows.Count == 0) return null;
            return DecodePath(rows[0][0]);
        }

        /// <summary>手動指定があればそれを、無ければ DB の設定値を返す。</summary>
        public static string ResolveItemDirectory(string dbPath)
        {
            var manual = ItemDirectoryOverride;
            if (!string.IsNullOrEmpty(manual)) return manual;
            if (string.IsNullOrEmpty(dbPath)) return null;
            return GetItemDirectoryPath(dbPath);
        }

        /// <summary>登録アイテムを 1 件以上持つショップの一覧（アイテム数の多い順）。</summary>
        public static List<BlmShop> GetShopsWithItems(string dbPath)
        {
            const string sql =
                "SELECT s.subdomain, s.name, s.thumbnail_url, COUNT(r.id) " +
                "FROM shops s " +
                "JOIN booth_items b ON b.shop_subdomain = s.subdomain " +
                "JOIN registered_items r ON r.booth_item_id = b.id " +
                "GROUP BY s.subdomain " +
                "ORDER BY COUNT(r.id) DESC, s.name";

            var result = new List<BlmShop>();
            foreach (var row in Query(dbPath, sql))
            {
                result.Add(new BlmShop
                {
                    Subdomain = row[0] as string,
                    Name = row[1] as string,
                    ThumbnailUrl = row[2] as string,
                    ItemCount = row[3] is long n ? (int)n : 0,
                });
            }
            return result;
        }

        /// <summary>指定ショップの登録アイテム一覧。ユーザーが上書きした名前があればそちらを使う。</summary>
        public static List<BlmItem> GetItemsByShop(string dbPath, string itemRoot, string shopSubdomain)
        {
            const string sql =
                "SELECT r.id, b.id, COALESCE(NULLIF(o.name, ''), b.name), b.shop_subdomain, b.thumbnail_url " +
                "FROM registered_items r " +
                "JOIN booth_items b ON r.booth_item_id = b.id " +
                "LEFT JOIN overwritten_booth_items o ON o.booth_item_id = b.id " +
                "WHERE b.shop_subdomain = ? " +
                "ORDER BY b.published_at DESC";

            var result = new List<BlmItem>();
            foreach (var row in Query(dbPath, sql, shopSubdomain))
            {
                var registeredId = row[0] as string;
                if (string.IsNullOrEmpty(registeredId)) continue;
                result.Add(new BlmItem
                {
                    RegisteredId = registeredId,
                    BoothItemId = row[1] is long id ? id : 0,
                    Name = row[2] as string ?? registeredId,
                    ShopSubdomain = row[3] as string,
                    ThumbnailUrl = row[4] as string,
                    ItemDirectory = string.IsNullOrEmpty(itemRoot) ? null : Path.Combine(itemRoot, registeredId),
                });
            }
            return result;
        }

        /// <summary>
        /// item_directory_path は BLOB で、中身は UTF-16LE 文字列（例: "K\0:\0\\\0..."）。
        /// 念のため TEXT / UTF-8 の BLOB にも対応する。
        /// </summary>
        internal static string DecodePath(object value)
        {
            switch (value)
            {
                case string s:
                    return s.TrimEnd('\0');
                case byte[] bytes when bytes.Length > 0:
                {
                    string decoded = LooksLikeUtf16Le(bytes)
                        ? Encoding.Unicode.GetString(bytes)
                        : Encoding.UTF8.GetString(bytes);
                    return decoded.TrimEnd('\0');
                }
                default:
                    return null;
            }
        }

        private static bool LooksLikeUtf16Le(byte[] bytes)
        {
            if (bytes.Length < 2 || bytes.Length % 2 != 0) return false;
            if (bytes[0] == 0xFF && bytes[1] == 0xFE) return true;
            // ASCII 範囲の文字が多いパスなら、奇数バイトに 0 が多く現れる
            int zeros = 0;
            for (int i = 1; i < bytes.Length; i += 2)
            {
                if (bytes[i] == 0) zeros++;
            }
            return zeros * 2 >= bytes.Length / 2;
        }

        private static List<object[]> Query(string dbPath, string sql, params object[] args)
        {
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                throw new FileNotFoundException("BLM のデータベースが見つかりません。", dbPath);
            if (!SqliteReader.IsSupported)
                throw new PlatformNotSupportedException("この OS では BLM のデータベース読み込みに対応していません。");

            try
            {
                using (var reader = SqliteReader.Open(dbPath))
                {
                    return reader.Query(sql, args);
                }
            }
            catch (SqliteException e)
            {
                // BLM がロックしている等で読めない場合は、一時コピーから読む
                Debug.LogWarning($"[Tool Importer] BLM の DB を直接読めなかったため一時コピーから読み込みます: {e.Message}");
                var copy = CopyToTemp(dbPath);
                try
                {
                    using (var reader = SqliteReader.Open(copy))
                    {
                        return reader.Query(sql, args);
                    }
                }
                finally
                {
                    TryDelete(copy);
                    TryDelete(copy + "-journal");
                }
            }
        }

        private static string CopyToTemp(string dbPath)
        {
            var dir = Path.Combine(Path.GetTempPath(), "DennokoworksToolImporter");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, $"blm_{Guid.NewGuid():N}.db");

            // SQLite が開いているファイルでも読めるよう共有モードを広く取る
            using (var src = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = new FileStream(dest, FileMode.CreateNew, FileAccess.Write))
            {
                src.CopyTo(dst);
            }
            return dest;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // 一時ファイルの削除失敗は無視する
            }
        }
    }
}

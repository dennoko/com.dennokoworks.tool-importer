using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        /// <param name="shopSubdomain">null / 空文字なら全ショップのアイテムを返す。</param>
        public static List<BlmItem> GetItemsByShop(string dbPath, string itemRoot, string shopSubdomain)
        {
            const string select =
                "SELECT r.id, b.id, COALESCE(NULLIF(o.name, ''), b.name), b.shop_subdomain, b.thumbnail_url, s.name " +
                "FROM registered_items r " +
                "JOIN booth_items b ON r.booth_item_id = b.id " +
                "LEFT JOIN overwritten_booth_items o ON o.booth_item_id = b.id " +
                "LEFT JOIN shops s ON s.subdomain = b.shop_subdomain ";
            const string order = "ORDER BY b.published_at DESC";

            var rows = string.IsNullOrEmpty(shopSubdomain)
                ? Query(dbPath, select + order)
                : Query(dbPath, select + "WHERE b.shop_subdomain = ? " + order, shopSubdomain);

            var result = new List<BlmItem>();
            foreach (var row in rows)
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
                    ShopName = row.Length > 5 ? row[5] as string : null,
                    ItemDirectory = string.IsNullOrEmpty(itemRoot) ? null : Path.Combine(itemRoot, registeredId),
                });
            }
            return result;
        }

        /// <summary>
        /// BLM のリスト（ユーザーが作成したもの）と所属アイテムの一覧（タイトル順）。
        /// lists テーブルが無い古い BLM では空を返す。スマートリストは対象外。
        /// </summary>
        public static List<BlmList> GetLists(string dbPath)
        {
            const string tableCheck = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name IN ('lists', 'list_items')";
            const string sql =
                "SELECT l.id, l.title, li.item_id " +
                "FROM lists l " +
                "LEFT JOIN list_items li ON li.list_id = l.id " +
                "ORDER BY l.title COLLATE NOCASE, l.id";

            var result = new List<BlmList>();
            WithReader(dbPath, reader =>
            {
                result.Clear();
                if (reader.Query(tableCheck).Count < 2) return;

                BlmList current = null;
                foreach (var row in reader.Query(sql))
                {
                    if (!(row[0] is long id)) continue;
                    if (current == null || current.Id != id)
                    {
                        current = new BlmList { Id = id, Title = row[1] as string ?? id.ToString() };
                        result.Add(current);
                    }
                    if (row[2] is string itemId && !string.IsNullOrEmpty(itemId)) current.ItemIds.Add(itemId);
                }
            });
            return result;
        }

        /// <summary>
        /// BOOTH のファイル更新通知（notifications の latestDownloadableAvailable）を、
        /// 登録アイテム ID (registered_items.id) ごとに、バリエーションごとの最新 1 件だけ返す。既読の通知も含む。
        /// notifications / booth_item_variations テーブルが無い古い BLM では空を返す。
        /// </summary>
        /// <remarks>
        /// content は {"type":"latestDownloadableAvailable","data":[バリエーションID,["ファイル名",...]]} の JSON。
        /// SQLite の JSON 関数は環境によって使えないため、C# 側で解析する。
        /// </remarks>
        public static Dictionary<string, List<BlmUpdateNotice>> GetLatestUpdateNotices(string dbPath)
        {
            const string tableCheck =
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name IN ('notifications', 'booth_item_variations')";
            const string variationSql =
                "SELECT v.id, v.variation_name, r.id " +
                "FROM booth_item_variations v " +
                "JOIN registered_items r ON r.booth_item_id = v.booth_item_id";
            const string noticeSql =
                "SELECT id, content, created_at FROM notifications " +
                "WHERE content LIKE '%latestDownloadableAvailable%' " +
                "ORDER BY id DESC";

            var result = new Dictionary<string, List<BlmUpdateNotice>>();
            WithReader(dbPath, reader =>
            {
                result.Clear();
                if (reader.Query(tableCheck).Count < 2) return;

                var variationNames = new Dictionary<long, string>();
                var variationItems = new Dictionary<long, string>();
                foreach (var row in reader.Query(variationSql))
                {
                    if (!(row[0] is long variationId) || !(row[2] is string registeredId) || string.IsNullOrEmpty(registeredId)) continue;
                    variationItems[variationId] = registeredId;
                    variationNames[variationId] = row[1] as string;
                }

                var seenVariations = new HashSet<long>();
                foreach (var row in reader.Query(noticeSql))
                {
                    if (!(row[0] is long notificationId) || !(row[1] is string content)) continue;
                    if (!TryParseDownloadableNotice(content, out var variationId, out var fileNames)) continue;
                    // id の降順に読むので、バリエーションごとに最初に出てきたものが最新
                    if (!seenVariations.Add(variationId)) continue;
                    // booth_item_variations に無いバリエーションは BLM 本体でも商品を引けないため無視する
                    if (!variationItems.TryGetValue(variationId, out var itemId)) continue;

                    if (!result.TryGetValue(itemId, out var list))
                    {
                        list = new List<BlmUpdateNotice>();
                        result[itemId] = list;
                    }
                    variationNames.TryGetValue(variationId, out var variationName);
                    list.Add(new BlmUpdateNotice
                    {
                        NotificationId = notificationId,
                        VariationId = variationId,
                        VariationName = string.IsNullOrWhiteSpace(variationName) ? null : variationName,
                        CreatedAtUtc = ParseUtc(row[2] as string),
                        FileNames = fileNames,
                    });
                }
            });
            return result;
        }

        /// <summary>
        /// 通知のうち、最新版がまだアイテムフォルダにダウンロードされていないものを返す（新しい順）。
        /// 通知のファイルが 1 つでもアイテムフォルダの直下にあれば、そのバリエーションはダウンロード済みとみなす。
        /// BLM は zip を拡張子を除いた名前のフォルダに展開し、それ以外はそのままのファイル名で保存する。
        /// アイテムフォルダが無ければ、すべて未ダウンロードとする。
        /// </summary>
        public static List<BlmUpdateNotice> FindPendingUpdates(string itemDirectory, List<BlmUpdateNotice> notices)
        {
            var pending = new List<BlmUpdateNotice>();
            if (notices == null || notices.Count == 0) return pending;

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!string.IsNullOrEmpty(itemDirectory) && Directory.Exists(itemDirectory))
                {
                    foreach (var f in Directory.GetFiles(itemDirectory)) files.Add(Path.GetFileName(f));
                    foreach (var d in Directory.GetDirectories(itemDirectory)) dirs.Add(Path.GetFileName(d));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tool Importer] BLM のアイテムフォルダを読み込めませんでした: {itemDirectory}\n{e.Message}");
            }

            foreach (var notice in notices)
            {
                if (!IsDownloaded(notice, files, dirs)) pending.Add(notice);
            }
            pending.Sort((a, b) => b.NotificationId.CompareTo(a.NotificationId));
            return pending;
        }

        private static bool IsDownloaded(BlmUpdateNotice notice, HashSet<string> files, HashSet<string> dirs)
        {
            foreach (var name in notice.FileNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (files.Contains(name)) return true;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && dirs.Contains(name.Substring(0, name.Length - ".zip".Length)))
                    return true;
            }
            return false;
        }

        private static readonly Regex NoticeTypeRegex =
            new Regex("\"type\"\\s*:\\s*\"latestDownloadableAvailable\"", RegexOptions.CultureInvariant);
        private static readonly Regex NoticeDataRegex =
            new Regex("\"data\"\\s*:\\s*\\[\\s*(\\d+)\\s*,\\s*\\[", RegexOptions.CultureInvariant);

        /// <summary>latestDownloadableAvailable 通知の JSON から、バリエーション ID とファイル名一覧を取り出す。</summary>
        internal static bool TryParseDownloadableNotice(string json, out long variationId, out List<string> fileNames)
        {
            variationId = 0;
            fileNames = new List<string>();
            if (string.IsNullOrEmpty(json) || !NoticeTypeRegex.IsMatch(json)) return false;

            var match = NoticeDataRegex.Match(json);
            if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out variationId))
                return false;

            // data[1] の文字列配列を読む
            int i = match.Index + match.Length;
            while (true)
            {
                i = SkipWhitespace(json, i);
                if (i >= json.Length) return false;
                if (json[i] == ']') break;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"' || !TryReadJsonString(json, ref i, out var name)) return false;
                fileNames.Add(name);
            }
            return true;
        }

        private static int SkipWhitespace(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        /// <summary><paramref name="i"/> の位置の '"' から JSON 文字列を読み、閉じ '"' の次へ進める。</summary>
        private static bool TryReadJsonString(string s, ref int i, out string value)
        {
            value = null;
            var sb = new StringBuilder();
            for (int p = i + 1; p < s.Length; p++)
            {
                char c = s[p];
                if (c == '"')
                {
                    value = sb.ToString();
                    i = p + 1;
                    return true;
                }
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (++p >= s.Length) return false;
                switch (s[p])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (p + 4 >= s.Length
                            || !int.TryParse(s.Substring(p + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            return false;
                        sb.Append((char)code);
                        p += 4;
                        break;
                    default:
                        return false;
                }
            }
            return false;
        }

        private static DateTime? ParseUtc(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                ? dt
                : (DateTime?)null;
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
            List<object[]> rows = null;
            WithReader(dbPath, reader => rows = reader.Query(sql, args));
            return rows;
        }

        /// <summary>
        /// DB を開いて <paramref name="action"/> を実行する。複数のクエリを 1 回の接続（一時コピーも 1 回）で済ませたいときに使う。
        /// 直接読めなかった場合は一時コピーで <paramref name="action"/> をやり直すため、action は再実行しても安全に作ること。
        /// </summary>
        private static void WithReader(string dbPath, Action<SqliteReader> action)
        {
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                throw new FileNotFoundException("BLM のデータベースが見つかりません。", dbPath);
            if (!SqliteReader.IsSupported)
                throw new PlatformNotSupportedException("この OS では BLM のデータベース読み込みに対応していません。");

            try
            {
                using (var reader = SqliteReader.Open(dbPath))
                {
                    action(reader);
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
                        action(reader);
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

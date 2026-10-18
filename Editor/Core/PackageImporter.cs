using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// BLM アイテムフォルダ内の .unitypackage の列挙・解析・導入判定・インポートを行う。
    /// パッケージ解析は 1 本のバックグラウンドスレッドで順に行い、結果は Library/ にキャッシュする。
    /// </summary>
    internal static class PackageImporter
    {
        private const float InstalledThreshold = 0.8f;
        private const string CacheDirectory = "Library/DennokoworksToolImporter";
        private const string CachePath = CacheDirectory + "/package_cache.json";

        // "_v1.2.0" / "-1.2" / " 1.2.3.4" などを拾う。前後が数字やドットの場合は除外
        private static readonly Regex VersionRegex = new Regex(@"(?<![\d.])[vV]?(\d+(?:\.\d+){1,3})(?![\d.])", RegexOptions.Compiled);

        [Serializable]
        private class CacheFile
        {
            public List<PackageScanResult> entries = new List<PackageScanResult>();
        }

        private static readonly Dictionary<string, PackageScanResult> Cache = new Dictionary<string, PackageScanResult>(StringComparer.OrdinalIgnoreCase);
        private static readonly object QueueLock = new object();
        private static readonly Queue<string> PendingQueue = new Queue<string>();
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<PackageScanResult> Completed = new List<PackageScanResult>();
        private static Thread _worker;
        private static bool _cacheLoaded;
        private static bool _cacheDirty;

        /// <summary>バックグラウンド解析が 1 件以上完了したとき（メインスレッドで）呼ばれる。</summary>
        public static event Action ScanCompleted;

        /// <summary>解析待ち・解析中のパッケージ数。</summary>
        public static int PendingCount
        {
            get { lock (QueueLock) return Pending.Count; }
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.update += DrainCompleted;
        }

        /// <summary>フォルダ以下の .unitypackage を再帰的に探し、新しいバージョン順に返す。</summary>
        public static List<PackageFile> FindUnityPackages(string itemDirectory)
        {
            var result = new List<PackageFile>();
            if (string.IsNullOrEmpty(itemDirectory) || !Directory.Exists(itemDirectory)) return result;

            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(itemDirectory, "*.unitypackage", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tool Importer] フォルダを読み込めませんでした: {itemDirectory} ({e.Message})");
                return result;
            }

            foreach (var path in files)
            {
                var info = new FileInfo(path);
                result.Add(new PackageFile
                {
                    Path = info.FullName,
                    FileName = info.Name,
                    FileVersion = ExtractVersion(info),
                    Size = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    RelativePath = GetRelativePath(itemDirectory, info.FullName),
                });
            }

            result.Sort((a, b) =>
            {
                int byVersion = DennokoVersionChecker.Compare(b.FileVersion, a.FileVersion);
                if (byVersion != 0) return byVersion;
                return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
            });
            return result;
        }

        /// <summary>ファイル名、無ければ親フォルダ名からバージョンを抽出する。</summary>
        internal static string ExtractVersion(FileInfo info)
        {
            var fromName = MatchLastVersion(Path.GetFileNameWithoutExtension(info.Name));
            if (fromName != null) return fromName;
            return info.Directory != null ? MatchLastVersion(info.Directory.Name) : null;
        }

        private static string MatchLastVersion(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var matches = VersionRegex.Matches(text);
            return matches.Count > 0 ? matches[matches.Count - 1].Groups[1].Value : null;
        }

        private static string GetRelativePath(string root, string fullPath)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(normalizedRoot.Length)
                : Path.GetFileName(fullPath);
        }

        /// <summary>
        /// 解析結果を返す。キャッシュが無い・古い場合は解析を予約して null を返す
        /// （完了すると <see cref="ScanCompleted"/> が発火する）。
        /// </summary>
        public static PackageScanResult GetScanResult(PackageFile package)
        {
            if (package == null) return null;
            LoadCache();

            if (Cache.TryGetValue(package.Path, out var cached)
                && cached.size == package.Size
                && cached.lastWriteTicks == package.LastWriteTimeUtc.Ticks)
            {
                return cached;
            }

            EnqueueScan(package.Path);
            return null;
        }

        public static bool IsScanning(PackageFile package)
        {
            if (package == null) return false;
            lock (QueueLock) return Pending.Contains(package.Path);
        }

        /// <summary>パッケージ内のアセットのうち、プロジェクトに存在する GUID の割合で導入状態を判定する。</summary>
        public static InstallState EvaluateInstallState(PackageScanResult scan, out float ratio)
        {
            ratio = 0f;
            if (scan == null) return InstallState.Scanning;
            if (!scan.IsValid || scan.guids.Count == 0) return InstallState.Unknown;

            int found = 0;
            foreach (var guid in scan.guids)
            {
                if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))) found++;
            }

            ratio = (float)found / scan.guids.Count;
            if (ratio >= InstalledThreshold) return InstallState.Installed;
            if (found > 0) return InstallState.PartiallyInstalled;
            return InstallState.NotInstalled;
        }

        /// <summary>インポートダイアログ付きでパッケージをインポートする。</summary>
        public static void ImportPackage(string packagePath)
        {
            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            {
                EditorUtility.DisplayDialog("インポート", $"パッケージが見つかりません。\n{packagePath}", "OK");
                return;
            }
            AssetDatabase.ImportPackage(packagePath, true);
        }

        /// <summary>キャッシュを破棄して全パッケージを再解析させる。</summary>
        public static void ClearCache()
        {
            LoadCache();
            Cache.Clear();
            _cacheDirty = true;
            SaveCache();
        }

        private static void EnqueueScan(string path)
        {
            lock (QueueLock)
            {
                if (!Pending.Add(path)) return;
                PendingQueue.Enqueue(path);

                if (_worker != null && _worker.IsAlive) return;
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "Dennokoworks Tool Importer Package Scan",
                };
                _worker.Start();
            }
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                string path;
                lock (QueueLock)
                {
                    if (PendingQueue.Count == 0)
                    {
                        _worker = null;
                        return;
                    }
                    path = PendingQueue.Dequeue();
                }

                var result = UnityPackageReader.Scan(path);

                lock (QueueLock)
                {
                    Completed.Add(result);
                }
            }
        }

        private static void DrainCompleted()
        {
            List<PackageScanResult> done = null;
            bool idle;
            lock (QueueLock)
            {
                if (Completed.Count > 0)
                {
                    done = new List<PackageScanResult>(Completed);
                    Completed.Clear();
                    foreach (var r in done) Pending.Remove(r.path);
                }
                idle = Pending.Count == 0;
            }

            if (done == null) return;

            LoadCache();
            foreach (var r in done)
            {
                if (!r.IsValid) Debug.LogWarning($"[Tool Importer] パッケージを解析できませんでした: {r.path} ({r.error})");
                Cache[r.path] = r;
            }
            _cacheDirty = true;
            if (idle) SaveCache();

            ScanCompleted?.Invoke();
        }

        private static void LoadCache()
        {
            if (_cacheLoaded) return;
            _cacheLoaded = true;
            if (!File.Exists(CachePath)) return;

            try
            {
                var file = JsonUtility.FromJson<CacheFile>(File.ReadAllText(CachePath));
                if (file?.entries == null) return;
                foreach (var entry in file.entries)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.path)) Cache[entry.path] = entry;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tool Importer] パッケージ解析キャッシュを読み込めませんでした: {e.Message}");
            }
        }

        private static void SaveCache()
        {
            if (!_cacheDirty) return;
            _cacheDirty = false;
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                var file = new CacheFile();
                foreach (var entry in Cache.Values)
                {
                    // 存在しなくなったパッケージはキャッシュから落とす
                    if (File.Exists(entry.path)) file.entries.Add(entry);
                }
                File.WriteAllText(CachePath, JsonUtility.ToJson(file));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tool Importer] パッケージ解析キャッシュを保存できませんでした: {e.Message}");
            }
        }
    }
}

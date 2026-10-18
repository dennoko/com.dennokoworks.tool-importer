using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// dennokoworks 製ツールの version.json（{ "version", "url", "message" }）を扱う。
    /// ローカル: Assets/dennokoworks/[ツール]/version.json
    /// GitHub : https://raw.githubusercontent.com/{owner}/{repo}/{main|master}/version.json
    /// owner/repo はローカル version.json の url から取り出す（ツール名と repo 名が一致しないものがあるため）。
    /// </summary>
    internal static class DennokoVersionChecker
    {
        public const string DennokoAssetsRoot = "Assets/dennokoworks";

        private const int RequestTimeoutSeconds = 10;
        private const string SessionKeyPrefix = "Dennokoworks.ToolImporter.Remote.";
        private static readonly string[] Branches = { "main", "master" };

        private static readonly Regex GitHubRepoRegex = new Regex(@"github\.com/([^/\s]+)/([^/\s#?]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

#pragma warning disable CS0649 // JsonUtility が値を設定する
        [Serializable]
        private class VersionJson
        {
            public string version;
            public string url;
            public string message;
        }
#pragma warning restore CS0649

        private static readonly Dictionary<string, RemoteVersionResult> Results = new Dictionary<string, RemoteVersionResult>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Assets/dennokoworks/ 直下で version.json を持つツールを列挙する。</summary>
        public static List<LocalToolVersion> FindLocalTools()
        {
            var result = new List<LocalToolVersion>();
            if (!Directory.Exists(DennokoAssetsRoot)) return result;

            foreach (var dir in Directory.GetDirectories(DennokoAssetsRoot))
            {
                var folder = Path.GetFileName(dir);
                var jsonPath = $"{DennokoAssetsRoot}/{folder}/version.json";
                if (!File.Exists(jsonPath)) continue;

                var tool = ReadLocalVersion(folder, jsonPath);
                if (tool != null) result.Add(tool);
            }

            result.Sort((a, b) => string.Compare(a.ToolFolder, b.ToolFolder, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static LocalToolVersion ReadLocalVersion(string folder, string jsonPath)
        {
            try
            {
                var text = File.ReadAllText(jsonPath).Trim().TrimStart('﻿');
                var json = JsonUtility.FromJson<VersionJson>(text);
                if (json == null || string.IsNullOrEmpty(json.version)) return null;

                var tool = new LocalToolVersion
                {
                    ToolFolder = folder,
                    VersionJsonPath = jsonPath,
                    Version = json.version.Trim(),
                    Url = json.url,
                };
                if (TryParseGitHubRepo(json.url, out var owner, out var repo))
                {
                    tool.GitHubOwner = owner;
                    tool.GitHubRepo = repo;
                }
                return tool;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tool Importer] version.json を読み込めませんでした: {jsonPath} ({e.Message})");
                return null;
            }
        }

        internal static bool TryParseGitHubRepo(string url, out string owner, out string repo)
        {
            owner = null;
            repo = null;
            if (string.IsNullOrEmpty(url)) return false;

            var m = GitHubRepoRegex.Match(url);
            if (!m.Success) return false;

            owner = m.Groups[1].Value;
            repo = m.Groups[2].Value;
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(0, repo.Length - 4);
            return !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(repo);
        }

        /// <summary>取得済み（または取得中）の結果。未確認なら null。セッション中はドメインリロード後も保持される。</summary>
        public static RemoteVersionResult GetCachedResult(string owner, string repo)
        {
            var key = $"{owner}/{repo}";
            if (Results.TryGetValue(key, out var result)) return result;

            var json = SessionState.GetString(SessionKeyPrefix + key, "");
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                result = JsonUtility.FromJson<RemoteVersionResult>(json);
            }
            catch (Exception)
            {
                return null;
            }
            if (result == null || result.state == RemoteVersionState.Checking) return null;
            Results[key] = result;
            return result;
        }

        /// <summary>GitHub の version.json を取得する。main → master の順に試す。onDone はメインスレッドで呼ばれる。</summary>
        public static void CheckAsync(string owner, string repo, string label, Action<RemoteVersionResult> onDone)
        {
            var key = $"{owner}/{repo}";
            if (Results.TryGetValue(key, out var current) && current.state == RemoteVersionState.Checking) return;

            var result = new RemoteVersionResult { owner = owner, repo = repo, label = label, state = RemoteVersionState.Checking };
            Results[key] = result;
            TryBranch(result, 0, onDone);
        }

        private static void TryBranch(RemoteVersionResult result, int index, Action<RemoteVersionResult> onDone)
        {
            var url = $"https://raw.githubusercontent.com/{result.owner}/{result.repo}/{Branches[index]}/version.json";
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", "Dennokoworks-ToolImporter");
            req.timeout = RequestTimeoutSeconds;

            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                long code = req.responseCode;
                bool success = req.result == UnityWebRequest.Result.Success;
                string body = req.downloadHandler != null ? req.downloadHandler.text : null;
                req.Dispose();

                if (success && TryApply(result, body))
                {
                    Finish(result, onDone);
                    return;
                }

                // 403 / 429 はレート制限なのでブランチを変えても解消しない
                bool rateLimited = code == 403 || code == 429;
                if (!rateLimited && index + 1 < Branches.Length)
                {
                    TryBranch(result, index + 1, onDone);
                    return;
                }

                result.state = RemoteVersionState.Error;
                // 配布は BOOTH のみのため、ユーザーに見える文言には取得先（GitHub / owner / repo）を出さない
                result.error = rateLimited
                    ? $"アクセスが集中しています。時間をおいて再度お試しください (HTTP {code})"
                    : $"取得に失敗しました (HTTP {code})";
                Debug.LogWarning($"[Tool Importer] {result.label} の最新バージョン情報を取得できませんでした: {result.error}");
                Finish(result, onDone);
            };
        }

        private static bool TryApply(RemoteVersionResult result, string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            try
            {
                var json = JsonUtility.FromJson<VersionJson>(body.Trim().TrimStart('﻿'));
                if (json == null || string.IsNullOrEmpty(json.version)) return false;
                result.state = RemoteVersionState.Success;
                result.version = json.version.Trim();
                result.url = json.url;
                result.message = json.message;
                result.error = null;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Finish(RemoteVersionResult result, Action<RemoteVersionResult> onDone)
        {
            SessionState.SetString(SessionKeyPrefix + $"{result.owner}/{result.repo}", JsonUtility.ToJson(result));
            onDone?.Invoke(result);
        }

        /// <summary>a が b より新しいか。どちらかが空なら false。</summary>
        public static bool IsNewer(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return Compare(a, b) > 0;
        }

        /// <summary>
        /// バージョン比較。null / 空は最も古い扱い。数値として解釈できない場合は文字列比較。
        /// </summary>
        public static int Compare(string a, string b)
        {
            bool emptyA = string.IsNullOrEmpty(a);
            bool emptyB = string.IsNullOrEmpty(b);
            if (emptyA || emptyB) return emptyA == emptyB ? 0 : (emptyA ? -1 : 1);

            var na = Normalize(a);
            var nb = Normalize(b);
            if (Version.TryParse(na, out var va) && Version.TryParse(nb, out var vb)) return va.CompareTo(vb);
            return string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 比較用に正規化する。BOM / 先頭 v / プレリリース・ビルドメタデータを除去し、
        /// 3 桁未満は 0 埋めする（"3.0" と "3.0.0" を同一視するため）。
        /// </summary>
        public static string Normalize(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0.0.0";
            v = v.Trim().Trim('﻿').Trim();
            if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V')) v = v.Substring(1);
            int cut = v.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) v = v.Substring(0, cut);
            if (string.IsNullOrEmpty(v)) return "0.0.0";

            var parts = v.Split('.');
            if (parts.Length >= 3) return v;
            var padded = new string[3];
            for (int i = 0; i < 3; i++)
                padded[i] = (i < parts.Length && !string.IsNullOrEmpty(parts[i])) ? parts[i] : "0";
            return string.Join(".", padded);
        }

        /// <summary>表示用に "v" を付ける。</summary>
        public static string Display(string v)
        {
            if (string.IsNullOrEmpty(v)) return "-";
            return v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v : "v" + v;
        }
    }
}

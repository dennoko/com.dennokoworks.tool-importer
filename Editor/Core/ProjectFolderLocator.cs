using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// インポート済みパッケージのアセットがプロジェクト内のどのフォルダにあるかを求め、Project ウィンドウでそのフォルダを開く。
    /// GUID からパスを引くため、インポート後にフォルダを移動・リネームしていても現在の場所を追える。
    /// </summary>
    internal static class ProjectFolderLocator
    {
        /// <summary>開くフォルダの候補 1 件。</summary>
        internal sealed class Candidate
        {
            public string Path;
            /// <summary>候補に含まれるアセット（実ファイル。実ファイルが無い場合はフォルダ）の数。</summary>
            public int AssetCount;
        }

        /// <summary>
        /// パッケージ内の GUID から、開くフォルダの候補を返す（アセット数の多い順）。
        /// 共通の親フォルダがルート (Assets / Packages) より深ければ候補は 1 つ。
        /// ルートそのものになる場合は、ルート直下のフォルダごとにまとめた候補を返す。
        /// プロジェクト内に 1 つもアセットが無ければ空を返す。
        /// </summary>
        public static List<Candidate> FindCandidates(IEnumerable<string> guids)
        {
            var files = new List<string>();
            var folders = new List<string>();
            if (guids != null)
            {
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    // 削除済みのアセットは除く
                    if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets))) continue;
                    if (AssetDatabase.IsValidFolder(path)) folders.Add(path);
                    else files.Add(path);
                }
            }

            // 実ファイルはその親フォルダを、実ファイルが無いときはフォルダ自体を「置き場所」として扱う
            var locations = new List<string>();
            if (files.Count > 0)
            {
                foreach (var file in files) locations.Add(GetParent(file));
            }
            else
            {
                locations.AddRange(folders);
            }

            var result = new List<Candidate>();
            if (locations.Count == 0) return result;

            var common = GetCommonParent(locations);
            if (Depth(common) >= 2)
            {
                result.Add(new Candidate { Path = common, AssetCount = locations.Count });
                return result;
            }

            // 共通の親がルート（または Assets と Packages にまたがる）なら、ルート直下のフォルダごとにまとめる。
            // ルート直下に直接置かれたものは、ルート自体を 1 つの候補にする。
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var location in locations)
            {
                var segments = location.Split('/');
                var key = segments.Length >= 2 ? segments[0] + "/" + segments[1] : segments[0];
                if (!groups.TryGetValue(key, out var members))
                {
                    members = new List<string>();
                    groups[key] = members;
                    order.Add(key);
                }
                members.Add(location);
            }

            foreach (var key in order)
            {
                var members = groups[key];
                result.Add(new Candidate { Path = GetCommonParent(members), AssetCount = members.Count });
            }
            result.Sort((a, b) =>
            {
                int byCount = b.AssetCount.CompareTo(a.AssetCount);
                return byCount != 0 ? byCount : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>
        /// Project ウィンドウで、フォルダを開いた状態（フォルダの中身が見える状態）にする。フォルダを読み込めなければ false。
        /// 2 カラム表示では左のフォルダツリーでそのフォルダを選び、右ペインに中身を出す。
        /// 1 カラム表示（または内部 API が使えない場合）は、フォルダを選択し、中の最初のアセットを Ping してフォルダを展開する。
        /// </summary>
        public static bool Reveal(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return false;
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
            if (folder == null) return false;

            EditorUtility.FocusProjectWindow();
            if (TryShowFolderContentsInTwoColumns(folder)) return true;

            Selection.activeObject = folder;
            var child = FindFirstChild(folderPath);
            EditorGUIUtility.PingObject(child != null ? child : folder);
            return true;
        }

        /// <summary>
        /// 2 カラム表示の Project ウィンドウで、フォルダツリーのフォルダ選択を変えて右ペインに中身を出す。
        /// 公開 API が無いため内部の ProjectBrowser をリフレクションで呼ぶ（Unity 2022.3 で確認）。
        /// 1 カラム表示・Project ウィンドウが無い・API が変わっていた場合は false。
        /// </summary>
        /// <remarks>
        /// フォルダ自体を Selection に入れると、ProjectBrowser が親フォルダを開いてそのフォルダをハイライトし直すため、選択は変えない。
        /// </remarks>
        private static bool TryShowFolderContentsInTwoColumns(UnityEngine.Object folder)
        {
            try
            {
                var browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
                if (browserType == null) return false;

                var browser = browserType
                    .GetField("s_LastInteractedProjectBrowser", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null);
                if (browser == null) return false;

                const BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var isTwoColumns = browserType.GetMethod("IsTwoColumns", instance, null, Type.EmptyTypes, null);
                var setFolderSelection = browserType.GetMethod("SetFolderSelection", instance, null,
                    new[] { typeof(int[]), typeof(bool) }, null);
                if (isTwoColumns == null || setFolderSelection == null) return false;
                if (!(isTwoColumns.Invoke(browser, null) is bool twoColumns) || !twoColumns) return false;

                setFolderSelection.Invoke(browser, new object[] { new[] { folder.GetInstanceID() }, true });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tool Importer] Project ウィンドウでフォルダを開けなかったため、Ping で表示します: {e.Message}");
                return false;
            }
        }

        /// <summary>フォルダ直下の最初のアセット（名前順）。空のフォルダなら null。</summary>
        private static UnityEngine.Object FindFirstChild(string folderPath)
        {
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(folderPath);
            }
            catch (Exception)
            {
                return null;
            }

            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var name = System.IO.Path.GetFileName(entry);
                // Unity が取り込まないもの（.meta・隠しファイル・末尾 ~ のフォルダ）は除く
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".") || name.EndsWith("~")) continue;
                var child = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath + "/" + name);
                if (child != null) return child;
            }
            return null;
        }

        private static string GetParent(string assetPath)
        {
            int slash = assetPath.LastIndexOf('/');
            return slash > 0 ? assetPath.Substring(0, slash) : assetPath;
        }

        /// <summary>パスの共通の親フォルダ（区切りごとに比較）。共通部分が無ければ空文字。</summary>
        private static string GetCommonParent(List<string> paths)
        {
            var common = paths[0].Split('/');
            int length = common.Length;
            for (int i = 1; i < paths.Count && length > 0; i++)
            {
                var segments = paths[i].Split('/');
                int n = Math.Min(length, segments.Length);
                int same = 0;
                while (same < n && string.Equals(common[same], segments[same], StringComparison.OrdinalIgnoreCase)) same++;
                length = same;
            }
            return string.Join("/", common, 0, length);
        }

        private static int Depth(string path) => string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
    }
}

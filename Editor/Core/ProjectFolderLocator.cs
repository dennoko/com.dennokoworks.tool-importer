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

        /// <summary><see cref="Reveal"/> の結果。</summary>
        internal enum RevealResult
        {
            /// <summary>Project ウィンドウを前面に出し、フォルダを開く処理を始めた（フォルダを開くのは次のエディタ更新）。</summary>
            Started,
            /// <summary>フォルダを読み込めなかった（削除済み等）。</summary>
            FolderNotFound,
            /// <summary>Project ウィンドウ（タブ）が 1 つも開かれていない。</summary>
            NoProjectWindow,
        }

        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>SetFolderSelection が失敗したときに、次のエディタ更新でやり直す回数。</summary>
        private const int FolderSelectionRetries = 1;

        /// <summary>
        /// Project ウィンドウで、フォルダを開いた状態（フォルダの中身が見える状態）にする。
        /// 2 カラム表示では左のフォルダツリーでそのフォルダを選び、右ペインに中身を出す。
        /// 1 カラム表示は、フォルダを選択し、中の最初のアセットを Ping してフォルダを展開する。
        /// </summary>
        /// <remarks>
        /// 別のタブの裏に隠れていた Project ウィンドウは、前面に出した直後はまだ描画・初期化されていないことがあるため、
        /// ウィンドウを前面に出すところまでをここで行い、フォルダを開くのは次のエディタ更新 (delayCall) で行う。
        /// </remarks>
        public static RevealResult Reveal(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return RevealResult.FolderNotFound;
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
            if (folder == null) return RevealResult.FolderNotFound;

            var browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
            if (browserType == null)
            {
                // 内部 API が無い Unity: 公開 API だけで開く。表示モードが分からないので選択は変えずに Ping だけ行う
                EditorUtility.FocusProjectWindow();
                PingFolderContents(folderPath, folder, selectFolder: false);
                return RevealResult.Started;
            }

            var browser = FindProjectBrowser(browserType);
            if (browser == null) return RevealResult.NoProjectWindow;

            // EditorUtility.FocusProjectWindow は前面に出すウィンドウを自分で選ぶため、操作対象と食い違わないよう同じインスタンスを前面に出す
            browser.Focus();
            EditorApplication.delayCall += () => ShowFolder(browser, folderPath, FolderSelectionRetries);
            return RevealResult.Started;
        }

        /// <summary>
        /// 操作対象の Project ウィンドウ。最後に操作したものを優先し、無ければ開いているもののうち最初の 1 つ。1 つも無ければ null。
        /// s_LastInteractedProjectBrowser は static なので、ドメインリロード後は Project ウィンドウにフォーカスが当たるまで null になる。
        /// </summary>
        private static EditorWindow FindProjectBrowser(Type browserType)
        {
            try
            {
                if (browserType.GetField("s_LastInteractedProjectBrowser", StaticFlags)?.GetValue(null) is EditorWindow last && last != null)
                    return last;
            }
            catch (Exception)
            {
                // フィールドが変わっていても、下の検索で見つける
            }

            foreach (var obj in Resources.FindObjectsOfTypeAll(browserType))
            {
                if (obj is EditorWindow window && window != null) return window;
            }
            return null;
        }

        /// <summary>
        /// 前面に出した Project ウィンドウでフォルダを開く。2 カラム表示で内部 API が失敗した場合は、次のエディタ更新でやり直し、
        /// それでも失敗したら Ping で表示する。
        /// </summary>
        private static void ShowFolder(EditorWindow browser, string folderPath, int retriesLeft)
        {
            // 待つ間にウィンドウが閉じられた・フォルダが消えた場合は何もしない
            if (browser == null) return;
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
            if (folder == null) return;

            bool? twoColumns = IsTwoColumns(browser);
            if (twoColumns == true)
            {
                var error = TrySetFolderSelection(browser, folder);
                if (error == null) return;

                if (retriesLeft > 0)
                {
                    EditorApplication.delayCall += () => ShowFolder(browser, folderPath, retriesLeft - 1);
                    return;
                }
                Debug.LogWarning($"[Tool Importer] Project ウィンドウでフォルダを開けなかったため、Ping で表示します: {error.Message}");
            }

            // 2 カラム表示でフォルダ自体を Selection に入れると、後から届く選択変更の処理で親フォルダが開き直されるため、
            // 選択を変えるのは 1 カラム表示と分かっているときだけにする
            PingFolderContents(folderPath, folder, selectFolder: twoColumns == false);
        }

        /// <summary>Project ウィンドウが 2 カラム表示か。内部 API が使えず判定できなければ null。</summary>
        private static bool? IsTwoColumns(EditorWindow browser)
        {
            try
            {
                var method = browser.GetType().GetMethod("IsTwoColumns", InstanceFlags, null, Type.EmptyTypes, null);
                return method?.Invoke(browser, null) is bool twoColumns ? twoColumns : (bool?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 2 カラム表示の Project ウィンドウで、フォルダツリーのフォルダ選択を変えて右ペインに中身を出す。成功すれば null、失敗すればその例外。
        /// 公開 API が無いため内部の ProjectBrowser.SetFolderSelection をリフレクションで呼ぶ（Unity 2022.3 で確認）。
        /// </summary>
        /// <remarks>
        /// フォルダ自体を Selection に入れると、ProjectBrowser が親フォルダを開いてそのフォルダをハイライトし直すため、選択は変えない。
        /// </remarks>
        private static Exception TrySetFolderSelection(EditorWindow browser, UnityEngine.Object folder)
        {
            try
            {
                var method = browser.GetType().GetMethod("SetFolderSelection", InstanceFlags, null,
                    new[] { typeof(int[]), typeof(bool) }, null);
                if (method == null) return new MissingMethodException("UnityEditor.ProjectBrowser", "SetFolderSelection");

                method.Invoke(browser, new object[] { new[] { folder.GetInstanceID() }, true });
                return null;
            }
            catch (TargetInvocationException e)
            {
                // 呼び出し先で出た例外は TargetInvocationException に包まれるので、中身を返す
                return e.InnerException ?? e;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        /// <summary>
        /// フォルダ直下の最初のアセットを Ping してフォルダを展開する（空のフォルダならフォルダ自体を Ping する）。
        /// selectFolder が true ならフォルダを選択もする（1 カラム表示用）。
        /// </summary>
        private static void PingFolderContents(string folderPath, UnityEngine.Object folder, bool selectFolder)
        {
            if (selectFolder) Selection.activeObject = folder;
            var child = FindFirstChild(folderPath);
            EditorGUIUtility.PingObject(child != null ? child : folder);
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

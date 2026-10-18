using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// 複数の .unitypackage を 1 件ずつ順番にインポートするキュー。
    ///
    /// スクリプトを含むパッケージをインポートするとドメインリロードが起き static が消えるため、
    /// 状態は SessionState に保存し、リロード後に [InitializeOnLoad] で再開する。
    /// 次のパッケージはコンパイル・アセット更新が落ち着いてから開始する。
    /// </summary>
    [InitializeOnLoad]
    internal static class ImportQueue
    {
        private const string SessionKey = "Dennokoworks.ToolImporter.ImportQueue";

        /// <summary>インポート完了イベントが来ないまま待ち続けないための上限（秒）。</summary>
        private const double StartTimeoutSeconds = 120;

        [Serializable]
        internal class Entry
        {
            public string label;
            public string path;
        }

        [Serializable]
        private class State
        {
            public List<Entry> pending = new List<Entry>();
            public Entry current;
            public bool interactive;
            public int total;
            public int succeeded;
            public int cancelled;
            public List<string> failed = new List<string>();
            /// <summary>current の開始時刻（EditorApplication.timeSinceStartup）。</summary>
            public double currentStartedAt;
            /// <summary>current の importPackageStarted を受け取ったか。</summary>
            public bool currentStarted;
            public string lastSummary;
        }

        private static State _state;

        /// <summary>キューの状態が変わったとき呼ばれる（表示更新用）。</summary>
        public static event Action Changed;

        static ImportQueue()
        {
            AssetDatabase.importPackageStarted += OnStarted;
            AssetDatabase.importPackageCompleted += OnCompleted;
            AssetDatabase.importPackageCancelled += OnCancelled;
            AssetDatabase.importPackageFailed += OnFailed;
            EditorApplication.update += Update;
        }

        private static State Current
        {
            get
            {
                if (_state != null) return _state;
                var json = SessionState.GetString(SessionKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    try { _state = JsonUtility.FromJson<State>(json); }
                    catch (Exception) { _state = null; }
                }
                if (_state == null) _state = new State();

                // 起動時刻の基準はエディタ再起動でリセットされるため、未来の値は今に合わせる
                if (_state.currentStartedAt > EditorApplication.timeSinceStartup)
                    _state.currentStartedAt = EditorApplication.timeSinceStartup;
                return _state;
            }
        }

        /// <summary>処理待ち・処理中のパッケージがあるか。</summary>
        public static bool IsBusy => Current.current != null || Current.pending.Count > 0;

        public static int Total => Current.total;

        /// <summary>完了（成功・キャンセル・失敗）した件数。</summary>
        public static int Processed => Current.succeeded + Current.cancelled + Current.failed.Count;

        public static string CurrentLabel => Current.current?.label;

        public static int PendingCount => Current.pending.Count;

        /// <summary>直近のまとめてインポートの結果（無ければ null）。</summary>
        public static string LastSummary => Current.lastSummary;

        /// <summary>パッケージをまとめてキューに追加する。interactive なら 1 件ずつインポートダイアログを表示する。</summary>
        public static void Enqueue(IEnumerable<Entry> entries, bool interactive)
        {
            var state = Current;
            if (!IsBusy)
            {
                state.total = 0;
                state.succeeded = 0;
                state.cancelled = 0;
                state.failed.Clear();
                state.lastSummary = null;
            }
            state.interactive = interactive;

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.path)) continue;
                state.pending.Add(entry);
                state.total++;
            }
            Save();
        }

        /// <summary>処理中のパッケージを除き、残りをすべて取り消す。</summary>
        public static void CancelPending()
        {
            var state = Current;
            state.cancelled += state.pending.Count;
            state.pending.Clear();
            if (state.current == null) FinishBatch();
            Save();
        }

        /// <summary>完了通知が来ない場合に、処理中のパッケージを諦めて次へ進む。</summary>
        public static void SkipCurrent()
        {
            var state = Current;
            if (state.current == null) return;
            state.failed.Add($"{state.current.label}（スキップ）");
            state.current = null;
            if (state.pending.Count == 0) FinishBatch();
            Save();
        }

        public static void ClearSummary()
        {
            Current.lastSummary = null;
            Save();
        }

        private static void Update()
        {
            var state = Current;

            if (state.current != null)
            {
                // 非対話インポートで開始通知が無いまま一定時間経ち、Unity も何もしていなければ完了扱いで次へ進む。
                // 対話モードはユーザーがダイアログを開いたままの可能性があるので待ち続ける（手動スキップ可）
                bool idle = !EditorApplication.isCompiling && !EditorApplication.isUpdating;
                if (!state.interactive && !state.currentStarted && idle
                    && EditorApplication.timeSinceStartup - state.currentStartedAt > StartTimeoutSeconds)
                {
                    Debug.LogWarning($"[Tool Importer] {state.current.label} のインポート完了を確認できなかったため、次へ進みます。");
                    MarkCurrentDone(succeeded: true);
                }
                return;
            }

            if (state.pending.Count == 0) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            StartNext();
        }

        private static void StartNext()
        {
            var state = Current;
            var entry = state.pending[0];
            state.pending.RemoveAt(0);

            if (!File.Exists(entry.path))
            {
                state.failed.Add($"{entry.label}（ファイルが見つかりません）");
                if (state.pending.Count == 0) FinishBatch();
                Save();
                return;
            }

            state.current = entry;
            state.currentStarted = false;
            state.currentStartedAt = EditorApplication.timeSinceStartup;
            Save();

            try
            {
                AssetDatabase.ImportPackage(entry.path, state.interactive);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                state.failed.Add($"{entry.label}（{e.Message}）");
                state.current = null;
                if (state.pending.Count == 0) FinishBatch();
                Save();
            }
        }

        private static void OnStarted(string packageName)
        {
            var state = Current;
            if (state.current == null) return;
            state.currentStarted = true;
            Save();
        }

        private static void OnCompleted(string packageName)
        {
            if (Current.current == null) return;
            MarkCurrentDone(succeeded: true);
        }

        private static void OnCancelled(string packageName)
        {
            var state = Current;
            if (state.current == null) return;
            state.cancelled++;
            state.current = null;
            if (state.pending.Count == 0) FinishBatch();
            Save();
        }

        private static void OnFailed(string packageName, string errorMessage)
        {
            var state = Current;
            if (state.current == null) return;
            state.failed.Add($"{state.current.label}（{errorMessage}）");
            state.current = null;
            if (state.pending.Count == 0) FinishBatch();
            Save();
        }

        private static void MarkCurrentDone(bool succeeded)
        {
            var state = Current;
            if (succeeded) state.succeeded++;
            state.current = null;
            if (state.pending.Count == 0) FinishBatch();
            Save();
        }

        private static void FinishBatch()
        {
            var state = Current;
            if (state.total == 0) return;

            var summary = $"まとめてインポート完了: 成功 {state.succeeded} / キャンセル {state.cancelled} / 失敗 {state.failed.Count}（全 {state.total} 件）";
            if (state.failed.Count > 0) summary += "\n失敗: " + string.Join("、", state.failed);
            state.lastSummary = summary;
            Debug.Log($"[Tool Importer] {summary}");

            state.total = 0;
            state.succeeded = 0;
            state.cancelled = 0;
            state.failed.Clear();
        }

        private static void Save()
        {
            SessionState.SetString(SessionKey, JsonUtility.ToJson(Current));
            Changed?.Invoke();
        }
    }
}

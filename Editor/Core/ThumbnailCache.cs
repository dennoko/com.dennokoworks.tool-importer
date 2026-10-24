using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// BOOTH のサムネイル画像を非同期で取得し、メモリにキャッシュする。
    /// 一覧は表示範囲の行だけ描画するため、しばらく参照されない画像は破棄し、
    /// 表示範囲から外れた読み込み待ちは取り消す（再び表示されたら取得し直す）。
    /// </summary>
    internal static class ThumbnailCache
    {
        private const int MaxConcurrent = 4;
        private const int RequestTimeoutSeconds = 15;

        /// <summary>メモリに保持するテクスチャの上限。超えたら最も長く参照されていないものから破棄する。</summary>
        private const int MaxTextures = 150;

        /// <summary>この秒数以上 Get されていない読み込み待ちは、表示範囲外になったとみなして取り消す。</summary>
        private const double WaitingExpireSeconds = 30.0;

        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        /// <summary>URL ごとの最終参照時刻（EditorApplication.timeSinceStartup）。</summary>
        private static readonly Dictionary<string, double> LastAccess = new Dictionary<string, double>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        private static readonly HashSet<string> Loading = new HashSet<string>();
        private static readonly HashSet<string> Waiting = new HashSet<string>();

        /// <summary>画像の読み込みが完了したとき（メインスレッドで）呼ばれる。</summary>
        public static event Action Loaded;

        /// <summary>取得済みならテクスチャを返す。未取得なら読み込みを予約して null を返す。</summary>
        public static Texture2D Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            LastAccess[url] = EditorApplication.timeSinceStartup;

            if (Textures.TryGetValue(url, out var tex))
            {
                if (tex != null) return tex;
                Textures.Remove(url);
            }
            if (Failed.Contains(url) || Loading.Contains(url)) return null;

            Waiting.Add(url);
            if (Loading.Count < MaxConcurrent) Pump();
            return null;
        }

        private static void Pump()
        {
            while (Loading.Count < MaxConcurrent && Waiting.Count > 0)
            {
                var url = TakeNewestWaiting();
                if (url == null) break;
                Loading.Add(url);
                Start(url);
            }
        }

        /// <summary>最近参照されたものを優先して取り出す。古い予約は取り消す。</summary>
        private static string TakeNewestWaiting()
        {
            double now = EditorApplication.timeSinceStartup;
            string newest = null;
            double newestTime = double.MinValue;
            List<string> expired = null;

            foreach (var url in Waiting)
            {
                double t = LastAccess.TryGetValue(url, out var v) ? v : 0;
                if (now - t > WaitingExpireSeconds)
                {
                    (expired ?? (expired = new List<string>())).Add(url);
                    continue;
                }
                if (t > newestTime)
                {
                    newestTime = t;
                    newest = url;
                }
            }

            if (expired != null)
            {
                foreach (var url in expired)
                {
                    Waiting.Remove(url);
                    if (!Textures.ContainsKey(url)) LastAccess.Remove(url);
                }
            }
            if (newest != null) Waiting.Remove(newest);
            return newest;
        }

        private static void Start(string url)
        {
            var req = UnityWebRequestTexture.GetTexture(url);
            req.timeout = RequestTimeoutSeconds;
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        var tex = DownloadHandlerTexture.GetContent(req);
                        if (tex != null)
                        {
                            tex.hideFlags = HideFlags.HideAndDontSave;
                            Textures[url] = tex;
                            EvictIfNeeded();
                        }
                        else
                        {
                            Failed.Add(url);
                        }
                    }
                    else
                    {
                        Failed.Add(url);
                    }
                }
                catch (Exception)
                {
                    Failed.Add(url);
                }
                finally
                {
                    req.Dispose();
                    Loading.Remove(url);
                }

                Pump();
                Loaded?.Invoke();
            };
        }

        /// <summary>上限を超えた分を、最終参照が古い順に破棄する。</summary>
        private static void EvictIfNeeded()
        {
            int over = Textures.Count - MaxTextures;
            if (over <= 0) return;

            var urls = new List<string>(Textures.Keys);
            urls.Sort((a, b) => GetAccess(a).CompareTo(GetAccess(b)));
            for (int i = 0; i < over; i++)
            {
                var url = urls[i];
                var tex = Textures[url];
                Textures.Remove(url);
                LastAccess.Remove(url);
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }

        private static double GetAccess(string url) => LastAccess.TryGetValue(url, out var t) ? t : 0;
    }
}

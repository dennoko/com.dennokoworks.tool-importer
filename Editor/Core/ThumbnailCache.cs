using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>BOOTH のサムネイル画像を非同期で取得し、メモリにキャッシュする。</summary>
    internal static class ThumbnailCache
    {
        private const int MaxConcurrent = 4;
        private const int RequestTimeoutSeconds = 15;

        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        private static readonly HashSet<string> Loading = new HashSet<string>();
        private static readonly Queue<string> Waiting = new Queue<string>();

        /// <summary>画像の読み込みが完了したとき（メインスレッドで）呼ばれる。</summary>
        public static event Action Loaded;

        /// <summary>取得済みならテクスチャを返す。未取得なら読み込みを開始して null を返す。</summary>
        public static Texture2D Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (Textures.TryGetValue(url, out var tex) && tex != null) return tex;
            if (Failed.Contains(url) || Loading.Contains(url) || Waiting.Contains(url)) return null;

            Waiting.Enqueue(url);
            Pump();
            return null;
        }

        private static void Pump()
        {
            while (Loading.Count < MaxConcurrent && Waiting.Count > 0)
            {
                var url = Waiting.Dequeue();
                Loading.Add(url);
                Start(url);
            }
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
    }
}

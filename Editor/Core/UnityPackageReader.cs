using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// .unitypackage（gzip 圧縮された tar）を先頭から逐次読み、
    /// アセット GUID・パス・同梱 version.json を取り出す。
    ///
    /// tar 内の構成:
    ///   [guid]/            ディレクトリ
    ///   [guid]/asset       アセット本体（フォルダの場合は無い）
    ///   [guid]/asset.meta
    ///   [guid]/pathname    インポート先のパス（1 行目）
    ///
    /// Unity API を使わないのでバックグラウンドスレッドから呼べる（JsonUtility はスレッドセーフ）。
    /// </summary>
    internal static class UnityPackageReader
    {
        private const int BlockSize = 512;
        private const string DennokoRoot = "Assets/dennokoworks/";

        /// <summary>version.json 候補としてメモリに保持するアセットの最大サイズ。</summary>
        private const int MaxVersionJsonSize = 8 * 1024;

#pragma warning disable CS0649 // JsonUtility が値を設定する
        [Serializable]
        private class VersionJson
        {
            public string version;
            public string url;
        }
#pragma warning restore CS0649

        public static PackageScanResult Scan(string path)
        {
            var info = new FileInfo(path);
            var result = new PackageScanResult
            {
                path = path,
                size = info.Exists ? info.Length : 0,
                lastWriteTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
            };

            try
            {
                var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pathnames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var smallJsonAssets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                {
                    ReadTar(gzip, (name, typeFlag, size, readData) =>
                    {
                        if (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
                        int slash = name.IndexOf('/');
                        string guid = slash >= 0 ? name.Substring(0, slash) : name;
                        string entry = slash >= 0 ? name.Substring(slash + 1).TrimEnd('/') : string.Empty;
                        if (!IsGuid(guid)) return;

                        guids.Add(guid);

                        if (entry == "pathname")
                        {
                            var text = Encoding.UTF8.GetString(readData());
                            int nl = text.IndexOfAny(new[] { '\r', '\n' });
                            pathnames[guid] = (nl >= 0 ? text.Substring(0, nl) : text).Trim();
                        }
                        else if (entry == "asset" && size > 0 && size <= MaxVersionJsonSize)
                        {
                            var data = readData();
                            if (LooksLikeVersionJson(data)) smallJsonAssets[guid] = data;
                        }
                    });
                }

                result.guids.AddRange(guids);

                var toolFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in pathnames)
                {
                    var assetPath = kv.Value.Replace('\\', '/');
                    var toolFolder = GetDennokoToolFolder(assetPath);
                    if (toolFolder == null) continue;
                    toolFolders.Add(toolFolder);

                    if (!assetPath.EndsWith("/version.json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!smallJsonAssets.TryGetValue(kv.Key, out var json)) continue;

                    var parsed = ParseVersionJson(json);
                    if (parsed == null || string.IsNullOrEmpty(parsed.version)) continue;
                    result.versions.Add(new EmbeddedVersion
                    {
                        toolFolder = toolFolder,
                        assetPath = assetPath,
                        version = parsed.version.Trim(),
                        url = parsed.url,
                    });
                }

                result.dennokoToolFolders.AddRange(toolFolders);
                result.dennokoToolFolders.Sort(StringComparer.OrdinalIgnoreCase);
                // ツール直下の version.json を優先する
                result.versions.Sort((a, b) => a.assetPath.Length.CompareTo(b.assetPath.Length));
            }
            catch (Exception e)
            {
                result.error = e.Message;
            }

            return result;
        }

        /// <summary>"Assets/dennokoworks/[ツール]/..." の [ツール] を返す。</summary>
        internal static string GetDennokoToolFolder(string assetPath)
        {
            if (!assetPath.StartsWith(DennokoRoot, StringComparison.OrdinalIgnoreCase)) return null;
            var rest = assetPath.Substring(DennokoRoot.Length);
            int slash = rest.IndexOf('/');
            var folder = slash >= 0 ? rest.Substring(0, slash) : rest;
            return string.IsNullOrEmpty(folder) ? null : folder;
        }

        private static VersionJson ParseVersionJson(byte[] data)
        {
            try
            {
                var text = Encoding.UTF8.GetString(data).Trim().TrimStart('﻿');
                return JsonUtility.FromJson<VersionJson>(text);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool LooksLikeVersionJson(byte[] data)
        {
            int i = 0;
            // UTF-8 BOM と空白を読み飛ばす
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) i = 3;
            while (i < data.Length && (data[i] == ' ' || data[i] == '\t' || data[i] == '\r' || data[i] == '\n')) i++;
            if (i >= data.Length || data[i] != (byte)'{') return false;
            return Encoding.UTF8.GetString(data).IndexOf("\"version\"", StringComparison.Ordinal) >= 0;
        }

        private static bool IsGuid(string s)
        {
            if (s.Length != 32) return false;
            foreach (var c in s)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>
        /// tar を逐次読み、通常ファイル・ディレクトリごとに onEntry を呼ぶ。
        /// readData を呼ばなかったエントリの中身は読み飛ばされる。
        /// ustar の prefix、GNU の long name ('L')、pax の path ('x') に対応。
        /// </summary>
        private static void ReadTar(Stream stream, Action<string, char, long, Func<byte[]>> onEntry)
        {
            var header = new byte[BlockSize];
            var skipBuffer = new byte[1 << 16];
            string pendingLongName = null;

            while (true)
            {
                if (!ReadExactly(stream, header, BlockSize)) return;
                if (IsZeroBlock(header)) return;

                string name = ReadString(header, 0, 100);
                long size = ReadSize(header, 124, 12);
                char typeFlag = (char)header[156];
                if (ReadString(header, 257, 5) == "ustar")
                {
                    var prefix = ReadString(header, 345, 155);
                    if (!string.IsNullOrEmpty(prefix)) name = prefix + "/" + name;
                }

                long padded = (size + BlockSize - 1) / BlockSize * BlockSize;
                bool consumed = false;
                byte[] data = null;

                Func<byte[]> readData = () =>
                {
                    if (consumed) return data;
                    if (size > int.MaxValue) throw new InvalidDataException("tar エントリが大きすぎます。");
                    data = new byte[size];
                    if (!ReadExactly(stream, data, (int)size)) throw new EndOfStreamException("tar が途中で終わっています。");
                    Skip(stream, padded - size, skipBuffer);
                    consumed = true;
                    return data;
                };

                switch (typeFlag)
                {
                    case 'L': // GNU long name: 次のエントリの名前
                        pendingLongName = ReadString(readData(), 0, (int)size);
                        break;
                    case 'x': // pax 拡張ヘッダ: path= があれば次のエントリの名前
                        pendingLongName = ParsePaxPath(readData()) ?? pendingLongName;
                        break;
                    case 'g': // pax グローバルヘッダは無視
                        break;
                    case '0':
                    case '\0':
                    case '5':
                        if (pendingLongName != null)
                        {
                            name = pendingLongName;
                            pendingLongName = null;
                        }
                        onEntry(name, typeFlag == '\0' ? '0' : typeFlag, size, readData);
                        break;
                    default:
                        pendingLongName = null;
                        break;
                }

                if (!consumed) Skip(stream, padded, skipBuffer);
            }
        }

        private static string ParsePaxPath(byte[] data)
        {
            // 形式: "<len> <key>=<value>\n" の繰り返し
            var text = Encoding.UTF8.GetString(data);
            foreach (var line in text.Split('\n'))
            {
                int space = line.IndexOf(' ');
                if (space < 0) continue;
                var kv = line.Substring(space + 1);
                if (kv.StartsWith("path=", StringComparison.Ordinal)) return kv.Substring(5);
            }
            return null;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }

        private static void Skip(Stream stream, long count, byte[] buffer)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (read <= 0) throw new EndOfStreamException("tar が途中で終わっています。");
                count -= read;
            }
        }

        private static bool IsZeroBlock(byte[] block)
        {
            foreach (var b in block)
            {
                if (b != 0) return false;
            }
            return true;
        }

        private static string ReadString(byte[] buffer, int offset, int length)
        {
            int end = offset;
            int limit = Math.Min(buffer.Length, offset + length);
            while (end < limit && buffer[end] != 0) end++;
            return Encoding.UTF8.GetString(buffer, offset, end - offset);
        }

        private static long ReadSize(byte[] buffer, int offset, int length)
        {
            // GNU の base-256 表現（先頭ビットが立っている）
            if ((buffer[offset] & 0x80) != 0)
            {
                long value = buffer[offset] & 0x7F;
                for (int i = 1; i < length; i++) value = (value << 8) | buffer[offset + i];
                return value;
            }

            long result = 0;
            for (int i = 0; i < length; i++)
            {
                byte b = buffer[offset + i];
                if (b == 0 || b == ' ') continue;
                if (b < '0' || b > '7') break;
                result = (result << 3) + (b - '0');
            }
            return result;
        }
    }
}

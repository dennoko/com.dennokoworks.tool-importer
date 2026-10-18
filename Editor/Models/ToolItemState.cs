using System;
using System.Collections.Generic;

namespace Dennokoworks.ToolImporter.Editor
{
    internal enum InstallState
    {
        /// <summary>.unitypackage が見つからない。</summary>
        NoPackage,
        /// <summary>パッケージ解析中。</summary>
        Scanning,
        /// <summary>解析に失敗した。</summary>
        Unknown,
        NotInstalled,
        PartiallyInstalled,
        Installed,
    }

    /// <summary>BLM アイテムフォルダ内で見つかった .unitypackage 1 件。</summary>
    internal sealed class PackageFile
    {
        public string Path;
        public string FileName;
        /// <summary>ファイル名または親フォルダ名から抽出したバージョン（無ければ null）。</summary>
        public string FileVersion;
        public long Size;
        public DateTime LastWriteTimeUtc;

        /// <summary>BLM アイテムフォルダからの相対パス（メニュー表示用）。</summary>
        public string RelativePath;
    }

    /// <summary>パッケージ内に同梱されていた version.json。</summary>
    [Serializable]
    internal sealed class EmbeddedVersion
    {
        /// <summary>Assets/dennokoworks/ 直下のツールフォルダ名。</summary>
        public string toolFolder;
        public string assetPath;
        public string version;
        public string url;
    }

    /// <summary>.unitypackage の解析結果。Library/ にキャッシュする。</summary>
    [Serializable]
    internal sealed class PackageScanResult
    {
        public string path;
        public long size;
        public long lastWriteTicks;

        public List<string> guids = new List<string>();
        /// <summary>パッケージ内の Assets/dennokoworks/ 直下のフォルダ名。</summary>
        public List<string> dennokoToolFolders = new List<string>();
        public List<EmbeddedVersion> versions = new List<EmbeddedVersion>();
        public string error;

        public bool IsValid => string.IsNullOrEmpty(error);
    }

    /// <summary>Assets/dennokoworks/[ツール]/version.json の内容。</summary>
    internal sealed class LocalToolVersion
    {
        public string ToolFolder;
        /// <summary>version.json のアセットパス。</summary>
        public string VersionJsonPath;
        public string Version;
        public string Url;
        public string GitHubOwner;
        public string GitHubRepo;

        public bool HasGitHubRepo => !string.IsNullOrEmpty(GitHubOwner) && !string.IsNullOrEmpty(GitHubRepo);
    }

    internal enum RemoteVersionState { NotChecked, Checking, Success, Error }

    /// <summary>
    /// 公開されている最新 version.json の取得結果（取得先は GitHub だが、UI には出さない）。
    /// url は version.json 内の配布先 URL で、UI からは開かない。
    /// </summary>
    [Serializable]
    internal sealed class RemoteVersionResult
    {
        public string owner;
        public string repo;
        /// <summary>ログ表示用のツール名。</summary>
        public string label;
        public RemoteVersionState state;
        public string version;
        public string url;
        public string message;
        public string error;
    }

    /// <summary>一覧の 1 行分の表示状態。</summary>
    internal sealed class ToolItemState
    {
        public BlmItem Item;
        public List<PackageFile> Packages = new List<PackageFile>();
        /// <summary>アイテムフォルダに未展開の zip がある。</summary>
        public bool HasArchive;
        /// <summary>アイテムフォルダが存在する（BLM でダウンロード済み）。</summary>
        public bool DirectoryExists;
        public InstallState InstallState = InstallState.NoPackage;
        /// <summary>導入率 (0〜1)。</summary>
        public float InstalledRatio;

        /// <summary>最新パッケージに対応するローカルツール（dennokoworks のみ）。</summary>
        public LocalToolVersion LocalTool;
        /// <summary>最新パッケージ内の version.json、またはファイル名のバージョン。</summary>
        public string BlmVersion;

        public PackageFile LatestPackage => Packages.Count > 0 ? Packages[0] : null;
    }
}

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    public class DennokoworksToolImporterWindow : EditorWindow
    {
        private const string WindowTitle = "dennokoworks Importer";
        private const string BlmDirectoryPrefKey = "Dennokoworks_BlmDirectoryPath";

        private string _blmDirectoryPath = "";
        private Vector2 _scrollPosition;
        private bool _isCheckingUpdates = false;

        [MenuItem("Tools/dennokoworks/Tool Importer", false, 100)]
        [MenuItem("Window/dennokoworks/Tool Importer", false, 2000)]
        public static void Open()
        {
            var window = GetWindow<DennokoworksToolImporterWindow>(WindowTitle);
            window.minSize = new Vector2(480, 400);
            window.Show();
        }

        private void OnEnable()
        {
            _blmDirectoryPath = EditorPrefs.GetString(BlmDirectoryPrefKey, "");
            if (string.IsNullOrEmpty(_blmDirectoryPath))
            {
                // デフォルトのBLM保存場所（DocumentsやAppDataなど一般的なパスを候補として探索）
                _blmDirectoryPath = DetectDefaultBlmDirectory();
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawBlmSettings();
            DrawActionBar();
            DrawToolList();
            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14
                };
                EditorGUILayout.LabelField("dennokoworks Tool Importer (BLM連携)", titleStyle);
                EditorGUILayout.LabelField(
                    "BOOTH Library Manager (BLM) でダウンロードされた dennokoworks 製ツールのインポートおよびバージョン確認を行います。",
                    EditorStyles.wordWrappedMiniLabel
                );
            }
            EditorGUILayout.Space(4);
        }

        private void DrawBlmSettings()
        {
            EditorGUILayout.LabelField("BOOTH Library Manager 設定", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    _blmDirectoryPath = EditorGUILayout.TextField("BLM 保存先フォルダ", _blmDirectoryPath);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorPrefs.SetString(BlmDirectoryPrefKey, _blmDirectoryPath);
                    }

                    if (GUILayout.Button("参照...", GUILayout.Width(70)))
                    {
                        string selected = EditorUtility.OpenFolderPanel("BOOTH Library Manager の保存先フォルダを選択", _blmDirectoryPath, "");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            _blmDirectoryPath = selected;
                            EditorPrefs.SetString(BlmDirectoryPrefKey, _blmDirectoryPath);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(_blmDirectoryPath))
                {
                    if (Directory.Exists(_blmDirectoryPath))
                    {
                        EditorGUILayout.HelpBox($"✓ 有効なディレクトリです: {_blmDirectoryPath}", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("指定されたディレクトリが存在しません。パスを確認してください。", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("BLMのダウンロードフォルダが指定されていません。設定してください。", MessageType.None);
                }
            }
            EditorGUILayout.Space(4);
        }

        private void DrawActionBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("最新バージョン確認", GUILayout.Height(26)))
                {
                    CheckForUpdates();
                }

                if (GUILayout.Button("ツール一覧を再読み込み", GUILayout.Height(26)))
                {
                    ReloadTools();
                }
            }
            EditorGUILayout.Space(4);
        }

        private void DrawToolList()
        {
            EditorGUILayout.LabelField("検出された dennokoworks 製ツール", EditorStyles.boldLabel);

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPosition, EditorStyles.helpBox))
            {
                _scrollPosition = scroll.scrollPosition;

                // テンプレートプレビュー用の仮表示
                DrawToolItemPlaceholder("AtlasCraft", "v1.2.0", "v1.3.0", true);
                DrawToolItemPlaceholder("CableGenerator", "未インポート", "v1.0.5", false);
                DrawToolItemPlaceholder("DenLattice", "v2.0.0", "v2.0.0", false);
                DrawToolItemPlaceholder("DennokoMAT", "未インポート", "v1.1.2", false);
                DrawToolItemPlaceholder("HairChimeraTool", "v1.0.1", "v1.1.0", true);
            }
        }

        private void DrawToolItemPlaceholder(string toolName, string installedVersion, string blmVersion, bool hasUpdate)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(toolName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"プロジェクト内: {installedVersion}  |  BLM最新: {blmVersion}", EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();

                if (hasUpdate)
                {
                    GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
                    if (GUILayout.Button("更新をインポート", GUILayout.Width(110), GUILayout.Height(24)))
                    {
                        ImportTool(toolName);
                    }
                    GUI.backgroundColor = Color.white;
                }
                else if (installedVersion == "未インポート")
                {
                    GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                    if (GUILayout.Button("インポート", GUILayout.Width(110), GUILayout.Height(24)))
                    {
                        ImportTool(toolName);
                    }
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    GUI.enabled = false;
                    GUILayout.Button("最新です", GUILayout.Width(110), GUILayout.Height(24));
                    GUI.enabled = true;
                }
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("BOOTH ショップを開く", EditorStyles.miniButton))
                {
                    Application.OpenURL("https://dennoko.booth.pm/");
                }

                if (GUILayout.Button("BOOTH Library Manager について", EditorStyles.miniButton))
                {
                    Application.OpenURL("https://booth.pm/");
                }
            }
            EditorGUILayout.Space(2);
        }

        private void CheckForUpdates()
        {
            // TODO: 最新バージョンの確認ロジック（GitHub Releases / BOOTH API / BLMキャッシュなど）
            EditorUtility.DisplayDialog("バージョン確認", "ツールの最新バージョン確認ロジックを実行します（準備完了）。", "OK");
        }

        private void ReloadTools()
        {
            // TODO: BLMフォルダ内のアイテムスキャン
        }

        private void ImportTool(string toolName)
        {
            // TODO: BLMフォルダからunitypackageの抽出・インポート実行
            EditorUtility.DisplayDialog("インポート", $"{toolName} のインポート処理を呼び出します。", "OK");
        }

        private string DetectDefaultBlmDirectory()
        {
            // BLMの標準保存パス候補
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string candidate = Path.Combine(documents, "BOOTH Library Manager");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            return "";
        }
    }
}

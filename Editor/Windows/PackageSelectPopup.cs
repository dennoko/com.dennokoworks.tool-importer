using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// アイテムフォルダ内の .unitypackage を一覧表示し、インポートするものをチェックボックスで選ぶポップアップ。
    /// BLM には同じアイテムの複数バージョンが保存されていることが多いため、
    /// どのファイルを入れるかを必ずユーザーに選ばせる。
    /// </summary>
    internal sealed class PackageSelectPopup : PopupWindowContent
    {
        private const float Width = 460f;
        private const float RowHeight = 36f;
        private const float MaxListHeight = 300f;

        private readonly string _title;
        private readonly List<PackageFile> _packages;
        private readonly bool[] _checked;
        private readonly Action<List<PackageFile>> _onImport;
        private readonly Action<List<PackageFile>> _onSetBatch;
        private Vector2 _scroll;

        /// <param name="packages">新しい順に並んだパッケージ（先頭が最新）。</param>
        /// <param name="initialPaths">最初にチェックしておくパス。空なら最新のみ。</param>
        /// <param name="onImport">「インポート」押下時。古い順に並べた選択パッケージを渡す。</param>
        /// <param name="onSetBatch">複数選択モード時のみ指定。まとめてインポートの対象として反映する。</param>
        public PackageSelectPopup(string title, List<PackageFile> packages, ICollection<string> initialPaths,
            Action<List<PackageFile>> onImport, Action<List<PackageFile>> onSetBatch)
        {
            _title = title;
            _packages = packages;
            _onImport = onImport;
            _onSetBatch = onSetBatch;

            _checked = new bool[packages.Count];
            bool hasInitial = initialPaths != null && initialPaths.Count > 0;
            for (int i = 0; i < packages.Count; i++)
            {
                _checked[i] = hasInitial ? initialPaths.Contains(packages[i].Path) : i == 0;
            }
        }

        public override Vector2 GetWindowSize()
        {
            float list = Mathf.Min(_packages.Count * RowHeight + 4, MaxListHeight);
            return new Vector2(Width, 30 + list + 100);
        }

        public override void OnGUI(Rect rect)
        {
            GUILayout.Label(_title, EditorStyles.boldLabel);
            GUILayout.Label("インポートするパッケージを選択してください", EditorStyles.miniLabel);

            float listHeight = Mathf.Min(_packages.Count * RowHeight + 4, MaxListHeight);
            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.Height(listHeight)))
            {
                _scroll = scroll.scrollPosition;
                for (int i = 0; i < _packages.Count; i++) DrawRow(i);
            }

            int count = CheckedCount();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("すべて選択", EditorStyles.miniButtonLeft, GUILayout.ExpandWidth(false)))
                    SetAll(true);
                if (GUILayout.Button("選択解除", EditorStyles.miniButtonRight, GUILayout.ExpandWidth(false)))
                    SetAll(false);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{count} 件選択中", EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(count < 2))
            {
                GUILayout.Label("複数選んだ場合は古い順に 1 件ずつインポートします", EditorStyles.miniLabel);
                DennokoworksToolImporterWindow.BatchInteractive = EditorGUILayout.ToggleLeft(
                    new GUIContent("内容を確認しながら", "オンにすると 1 件ずつインポートダイアログを表示します"),
                    DennokoworksToolImporterWindow.BatchInteractive);
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (_onSetBatch != null
                    && GUILayout.Button(new GUIContent("まとめてインポート対象に設定", "チェックしたパッケージを「まとめてインポート」の対象にします"), GUILayout.Height(22)))
                {
                    var selected = GetSelected();
                    editorWindow.Close();
                    _onSetBatch(selected);
                    GUIUtility.ExitGUI();
                }

                using (new EditorGUI.DisabledScope(count == 0))
                {
                    if (GUILayout.Button($"インポート ({count} 件)", GUILayout.Width(110), GUILayout.Height(22)))
                    {
                        var selected = GetSelected();
                        editorWindow.Close();
                        _onImport(selected);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawRow(int index)
        {
            var package = _packages[index];
            var name = package.RelativePath;
            if (index == 0 && _packages.Count > 1) name += "  （最新）";

            _checked[index] = EditorGUILayout.ToggleLeft(new GUIContent(name, package.Path), _checked[index]);

            var details = new List<string>();
            if (!string.IsNullOrEmpty(package.FileVersion)) details.Add(DennokoVersionChecker.Display(package.FileVersion));
            details.Add(EditorUtility.FormatBytes(package.Size));
            details.Add(package.LastWriteTimeUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(18);
                GUILayout.Label(string.Join("  ·  ", details), EditorStyles.miniLabel);
            }
        }

        private int CheckedCount()
        {
            int count = 0;
            foreach (var c in _checked) if (c) count++;
            return count;
        }

        private void SetAll(bool value)
        {
            for (int i = 0; i < _checked.Length; i++) _checked[i] = value;
        }

        /// <summary>選択されたパッケージを古い順（一覧の逆順）で返す。新しいものが最後に上書きされるようにするため。</summary>
        private List<PackageFile> GetSelected()
        {
            var result = new List<PackageFile>();
            for (int i = _packages.Count - 1; i >= 0; i--)
            {
                if (_checked[i]) result.Add(_packages[i]);
            }
            return result;
        }
    }
}

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
        private readonly List<int> _checkOrder = new List<int>();
        private readonly Action<List<PackageFile>> _onImport;
        private readonly Action<List<PackageFile>> _onSetBatch;
        private Vector2 _scroll;

        public PackageSelectPopup(string title, List<PackageFile> packages, ICollection<string> initialPaths,
            Action<List<PackageFile>> onImport, Action<List<PackageFile>> onSetBatch)
        {
            _title = title;
            _packages = packages;
            _onImport = onImport;
            _onSetBatch = onSetBatch;

            _checked = new bool[packages.Count];
            bool hasInitial = initialPaths != null && initialPaths.Count > 0;
            if (hasInitial)
            {
                foreach (var path in initialPaths)
                {
                    int idx = packages.FindIndex(p => p.Path == path);
                    if (idx >= 0 && !_checkOrder.Contains(idx))
                    {
                        _checked[idx] = true;
                        _checkOrder.Add(idx);
                    }
                }
            }
            else if (packages.Count > 0)
            {
                _checked[0] = true;
                _checkOrder.Add(0);
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
            GUILayout.Label(ToolImporterLoc.Tr("PopupPrompt"), EditorStyles.miniLabel);

            float listHeight = Mathf.Min(_packages.Count * RowHeight + 4, MaxListHeight);
            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.Height(listHeight)))
            {
                _scroll = scroll.scrollPosition;
                for (int i = 0; i < _packages.Count; i++) DrawRow(i);
            }

            int count = CheckedCount();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(ToolImporterLoc.Tr("SelectAll"), EditorStyles.miniButtonLeft, GUILayout.ExpandWidth(false)))
                    SetAll(true);
                if (GUILayout.Button(ToolImporterLoc.Tr("DeselectAll"), EditorStyles.miniButtonRight, GUILayout.ExpandWidth(false)))
                    SetAll(false);
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolImporterLoc.Tr("PopupSelectedCount", count), EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(count < 2))
            {
                GUILayout.Label(ToolImporterLoc.Tr("PopupMultiSequentialHint"), EditorStyles.miniLabel);
                DennokoworksToolImporterWindow.BatchInteractive = EditorGUILayout.ToggleLeft(
                    new GUIContent(ToolImporterLoc.Tr("BatchInteractive"), ToolImporterLoc.Tr("BatchInteractiveTooltip")),
                    DennokoworksToolImporterWindow.BatchInteractive);
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (_onSetBatch != null
                    && GUILayout.Button(new GUIContent(ToolImporterLoc.Tr("PopupSetBatchButton"), ToolImporterLoc.Tr("PopupSetBatchTooltip")), GUILayout.Height(22)))
                {
                    var selected = GetSelected();
                    editorWindow.Close();
                    _onSetBatch(selected);
                    GUIUtility.ExitGUI();
                }

                using (new EditorGUI.DisabledScope(count == 0))
                {
                    if (GUILayout.Button(ToolImporterLoc.Tr("PopupImportButton", count), GUILayout.Width(130), GUILayout.Height(22)))
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
            if (index == 0 && _packages.Count > 1) name += ToolImporterLoc.Tr("PopupLatestSuffix");

            bool prev = _checked[index];
            bool next = EditorGUILayout.ToggleLeft(new GUIContent(name, package.Path), prev);
            if (prev != next)
            {
                _checked[index] = next;
                _checkOrder.Remove(index);
                if (next) _checkOrder.Add(index);
            }

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
            _checkOrder.Clear();
            if (value)
            {
                for (int i = _checked.Length - 1; i >= 0; i--)
                {
                    _checked[i] = true;
                    _checkOrder.Add(i);
                }
            }
            else
            {
                for (int i = 0; i < _checked.Length; i++) _checked[i] = false;
            }
        }

        /// <summary>選択されたパッケージをチェックされた順で返す。</summary>
        private List<PackageFile> GetSelected()
        {
            var result = new List<PackageFile>();
            foreach (var idx in _checkOrder)
            {
                if (idx >= 0 && idx < _packages.Count && _checked[idx])
                {
                    result.Add(_packages[idx]);
                }
            }
            for (int i = 0; i < _checked.Length; i++)
            {
                if (_checked[i] && !_checkOrder.Contains(i))
                {
                    result.Add(_packages[i]);
                }
            }
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Dennokoworks.ToolImporter.Editor
{
    /// <summary>
    /// OS 同梱の SQLite を P/Invoke で呼ぶ、読み取り専用の最小ラッパー。
    /// Windows 10 以降は System32 の winsqlite3.dll、macOS は /usr/lib/libsqlite3.dylib を使う。
    /// ネイティブ DLL を同梱しないため、パッケージを軽く保てる。
    /// </summary>
    internal sealed class SqliteReader : IDisposable
    {
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private const int SQLITE_OPEN_READONLY = 0x00000001;

        private const int SQLITE_INTEGER = 1;
        private const int SQLITE_FLOAT = 2;
        private const int SQLITE_TEXT = 3;
        private const int SQLITE_BLOB = 4;

        /// <summary>sqlite3_bind_* に渡す SQLITE_TRANSIENT（SQLite 側で値をコピーさせる）。</summary>
        private static readonly IntPtr SqliteTransient = new IntPtr(-1);

        private static INative _native;
        private IntPtr _db;

        private SqliteReader(IntPtr db)
        {
            _db = db;
        }

        /// <summary>現在の OS で SQLite が使えるか。</summary>
        public static bool IsSupported
        {
            get
            {
                return Application.platform == RuntimePlatform.WindowsEditor
                    || Application.platform == RuntimePlatform.OSXEditor;
            }
        }

        private static INative Native
        {
            get
            {
                if (_native != null) return _native;
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor: _native = new WinNative(); break;
                    case RuntimePlatform.OSXEditor: _native = new MacNative(); break;
                    default: throw new PlatformNotSupportedException("この OS では SQLite の読み込みに対応していません。");
                }
                return _native;
            }
        }

        /// <summary>DB を読み取り専用で開く。失敗時は例外を投げる。</summary>
        public static SqliteReader Open(string path)
        {
            var native = Native;
            int rc = native.open_v2(ToUtf8Z(path), out var db, SQLITE_OPEN_READONLY, IntPtr.Zero);
            if (rc != SQLITE_OK)
            {
                string msg = db != IntPtr.Zero ? PtrToUtf8(native.errmsg(db)) : $"code {rc}";
                if (db != IntPtr.Zero) native.close_v2(db);
                throw new SqliteException(rc, $"DB を開けませんでした: {msg}");
            }
            native.busy_timeout(db, 2000);
            return new SqliteReader(db);
        }

        /// <summary>
        /// SELECT を実行して全行を返す。値は string / long / double / byte[] / null のいずれか。
        /// 引数は ? プレースホルダに順にバインドされる（string / long / int のみ対応）。
        /// </summary>
        public List<object[]> Query(string sql, params object[] args)
        {
            if (_db == IntPtr.Zero) throw new ObjectDisposedException(nameof(SqliteReader));
            var native = Native;
            var sqlBytes = ToUtf8Z(sql);
            int rc = native.prepare_v2(_db, sqlBytes, sqlBytes.Length, out var stmt, IntPtr.Zero);
            if (rc != SQLITE_OK) throw new SqliteException(rc, $"SQL の準備に失敗しました: {PtrToUtf8(native.errmsg(_db))}");

            try
            {
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        int bindRc;
                        switch (args[i])
                        {
                            case null:
                                bindRc = native.bind_null(stmt, i + 1);
                                break;
                            case string s:
                                var bytes = Encoding.UTF8.GetBytes(s);
                                bindRc = native.bind_text(stmt, i + 1, bytes, bytes.Length, SqliteTransient);
                                break;
                            case long l:
                                bindRc = native.bind_int64(stmt, i + 1, l);
                                break;
                            case int n:
                                bindRc = native.bind_int64(stmt, i + 1, n);
                                break;
                            default:
                                throw new ArgumentException($"未対応の引数型です: {args[i].GetType()}");
                        }
                        if (bindRc != SQLITE_OK) throw new SqliteException(bindRc, "パラメータのバインドに失敗しました。");
                    }
                }

                var rows = new List<object[]>();
                while (true)
                {
                    rc = native.step(stmt);
                    if (rc == SQLITE_DONE) break;
                    if (rc != SQLITE_ROW) throw new SqliteException(rc, $"クエリの実行に失敗しました: {PtrToUtf8(native.errmsg(_db))}");

                    int count = native.column_count(stmt);
                    var row = new object[count];
                    for (int c = 0; c < count; c++) row[c] = ReadColumn(native, stmt, c);
                    rows.Add(row);
                }
                return rows;
            }
            finally
            {
                native.finalize(stmt);
            }
        }

        private static object ReadColumn(INative native, IntPtr stmt, int col)
        {
            switch (native.column_type(stmt, col))
            {
                case SQLITE_INTEGER:
                    return native.column_int64(stmt, col);
                case SQLITE_FLOAT:
                    return native.column_double(stmt, col);
                case SQLITE_TEXT:
                {
                    var ptr = native.column_text(stmt, col);
                    int len = native.column_bytes(stmt, col);
                    return PtrToUtf8(ptr, len);
                }
                case SQLITE_BLOB:
                {
                    var ptr = native.column_blob(stmt, col);
                    int len = native.column_bytes(stmt, col);
                    var buf = new byte[len];
                    if (len > 0 && ptr != IntPtr.Zero) Marshal.Copy(ptr, buf, 0, len);
                    return buf;
                }
                default:
                    return null;
            }
        }

        public void Dispose()
        {
            if (_db == IntPtr.Zero) return;
            Native.close_v2(_db);
            _db = IntPtr.Zero;
        }

        private static byte[] ToUtf8Z(string s)
        {
            int len = Encoding.UTF8.GetByteCount(s);
            var buf = new byte[len + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, buf, 0);
            return buf;
        }

        private static string PtrToUtf8(IntPtr ptr, int len = -1)
        {
            if (ptr == IntPtr.Zero) return null;
            if (len < 0)
            {
                len = 0;
                while (Marshal.ReadByte(ptr, len) != 0) len++;
            }
            if (len == 0) return string.Empty;
            var buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        #region Native bindings

        private interface INative
        {
            int open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
            int close_v2(IntPtr db);
            IntPtr errmsg(IntPtr db);
            int busy_timeout(IntPtr db, int ms);
            int prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
            int bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor);
            int bind_int64(IntPtr stmt, int index, long value);
            int bind_null(IntPtr stmt, int index);
            int step(IntPtr stmt);
            int finalize(IntPtr stmt);
            int column_count(IntPtr stmt);
            int column_type(IntPtr stmt, int col);
            long column_int64(IntPtr stmt, int col);
            double column_double(IntPtr stmt, int col);
            IntPtr column_text(IntPtr stmt, int col);
            IntPtr column_blob(IntPtr stmt, int col);
            int column_bytes(IntPtr stmt, int col);
        }

        private sealed class WinNative : INative
        {
            private const string Lib = "winsqlite3";
            private const CallingConvention CC = CallingConvention.StdCall;

            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_close_v2(IntPtr db);
            [DllImport(Lib, CallingConvention = CC)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_bind_null(IntPtr stmt, int index);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_step(IntPtr stmt);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_column_count(IntPtr stmt);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_column_type(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern long sqlite3_column_int64(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern double sqlite3_column_double(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_column_bytes(IntPtr stmt, int col);

            public int open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs) => sqlite3_open_v2(filename, out db, flags, vfs);
            public int close_v2(IntPtr db) => sqlite3_close_v2(db);
            public IntPtr errmsg(IntPtr db) => sqlite3_errmsg(db);
            public int busy_timeout(IntPtr db, int ms) => sqlite3_busy_timeout(db, ms);
            public int prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail) => sqlite3_prepare_v2(db, sql, nByte, out stmt, tail);
            public int bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor) => sqlite3_bind_text(stmt, index, value, n, destructor);
            public int bind_int64(IntPtr stmt, int index, long value) => sqlite3_bind_int64(stmt, index, value);
            public int bind_null(IntPtr stmt, int index) => sqlite3_bind_null(stmt, index);
            public int step(IntPtr stmt) => sqlite3_step(stmt);
            public int finalize(IntPtr stmt) => sqlite3_finalize(stmt);
            public int column_count(IntPtr stmt) => sqlite3_column_count(stmt);
            public int column_type(IntPtr stmt, int col) => sqlite3_column_type(stmt, col);
            public long column_int64(IntPtr stmt, int col) => sqlite3_column_int64(stmt, col);
            public double column_double(IntPtr stmt, int col) => sqlite3_column_double(stmt, col);
            public IntPtr column_text(IntPtr stmt, int col) => sqlite3_column_text(stmt, col);
            public IntPtr column_blob(IntPtr stmt, int col) => sqlite3_column_blob(stmt, col);
            public int column_bytes(IntPtr stmt, int col) => sqlite3_column_bytes(stmt, col);
        }

        private sealed class MacNative : INative
        {
            private const string Lib = "/usr/lib/libsqlite3.dylib";
            private const CallingConvention CC = CallingConvention.Cdecl;

            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_close_v2(IntPtr db);
            [DllImport(Lib, CallingConvention = CC)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_bind_null(IntPtr stmt, int index);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_step(IntPtr stmt);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_column_count(IntPtr stmt);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_column_type(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern long sqlite3_column_int64(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern double sqlite3_column_double(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int col);
            [DllImport(Lib, CallingConvention = CC)] private static extern int sqlite3_column_bytes(IntPtr stmt, int col);

            public int open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs) => sqlite3_open_v2(filename, out db, flags, vfs);
            public int close_v2(IntPtr db) => sqlite3_close_v2(db);
            public IntPtr errmsg(IntPtr db) => sqlite3_errmsg(db);
            public int busy_timeout(IntPtr db, int ms) => sqlite3_busy_timeout(db, ms);
            public int prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail) => sqlite3_prepare_v2(db, sql, nByte, out stmt, tail);
            public int bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor) => sqlite3_bind_text(stmt, index, value, n, destructor);
            public int bind_int64(IntPtr stmt, int index, long value) => sqlite3_bind_int64(stmt, index, value);
            public int bind_null(IntPtr stmt, int index) => sqlite3_bind_null(stmt, index);
            public int step(IntPtr stmt) => sqlite3_step(stmt);
            public int finalize(IntPtr stmt) => sqlite3_finalize(stmt);
            public int column_count(IntPtr stmt) => sqlite3_column_count(stmt);
            public int column_type(IntPtr stmt, int col) => sqlite3_column_type(stmt, col);
            public long column_int64(IntPtr stmt, int col) => sqlite3_column_int64(stmt, col);
            public double column_double(IntPtr stmt, int col) => sqlite3_column_double(stmt, col);
            public IntPtr column_text(IntPtr stmt, int col) => sqlite3_column_text(stmt, col);
            public IntPtr column_blob(IntPtr stmt, int col) => sqlite3_column_blob(stmt, col);
            public int column_bytes(IntPtr stmt, int col) => sqlite3_column_bytes(stmt, col);
        }

        #endregion
    }

    internal sealed class SqliteException : Exception
    {
        public int ResultCode { get; }

        public SqliteException(int resultCode, string message) : base(message)
        {
            ResultCode = resultCode;
        }
    }
}

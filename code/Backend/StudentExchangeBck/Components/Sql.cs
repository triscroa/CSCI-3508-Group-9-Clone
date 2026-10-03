using Microsoft.Data.Sqlite;
using System.Data;
using System.Text.RegularExpressions;

namespace StudentExchangeBck
{
    public static class Sql
    {
        static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        static readonly Regex _validRead = new Regex(@"^\s*select\b", RegexOptions.IgnoreCase);
        static readonly Regex _validWrite = new Regex(@"^(insert\s+into|update|delete\s+from)\b", RegexOptions.IgnoreCase);

        public static Dictionary<string, List<object>> Read(string sql, Dictionary<string, object>? args = null)
        {
            if (!_validRead.IsMatch(sql.Trim()))
                throw new ArgumentException($"Only SELECT SQL statements are allowed in Sql.Read(): {sql}.");
            try
            {
                _lock.EnterReadLock();
                using (var con = SqLiteConnect())
                {
                    if (con.State == ConnectionState.Closed) con.Open();

                    var com = con.CreateCommand();
                    com.CommandText = sql;

                    if (args != null)
                        foreach (var s in args)
                            com.Parameters.AddWithValue(s.Key, s.Value);

                    List<string>? keys = null;
                    var ls = new List<List<object>>();

                    var reader = com.ExecuteReader();
                    while (reader.Read())
                    {
                        if (keys == null)
                            keys = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                        ls.Add(keys.Select(k => reader[k]).ToList());
                    }
                    reader.Close();

                    if (keys == null) return new Dictionary<string, List<object>>();

                    ls = Utilz.Transpose(ls).ToList();
                    return Enumerable.Range(0, keys.Count).ToDictionary(i => keys[i], i => ls[i]);
                }
            }
            finally { _lock.ExitReadLock(); }
        }

        public static void Write(string sql, Dictionary<string, object>? args = null, int updatedRows = -1)
        {
            if (!_validWrite.IsMatch(sql.Trim().ToLower()))
                throw new ArgumentException($"Invalid SQL statement for Sql.Write(): {sql}.");
            SqliteTransaction? trans = null;
            int cnt = 0;
            try
            {
                _lock.EnterWriteLock();
                using (var con = SqLiteConnect())
                {
                    if (con.State == ConnectionState.Closed) con.Open();

                    trans = con.BeginTransaction();
                    var com = con.CreateCommand();
                    com.CommandText = sql;
                    com.Transaction = trans;

                    if (args != null)
                        foreach (var s in args)
                            com.Parameters.AddWithValue(s.Key, s.Value);

                    cnt = com.ExecuteNonQuery();
                    if (cnt != updatedRows && !new[] { -1, -2 }.Contains(updatedRows))
                        throw new Exception($"cnt({cnt}) != updatedRows({updatedRows})");
                    else if (cnt < 1 && updatedRows == -1)
                        throw new Exception($"cnt({cnt}) < 1");

                    if (cnt != 0)
                        trans.Commit();
                }
            }
            catch
            {
                if (cnt != 0)
                    trans?.Rollback();
                throw;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public static readonly string _path = Path.GetFullPath("bin_/Data.db");
        static SqliteConnection SqLiteConnect()
            => new SqliteConnection($"Data Source={_path};");
    }
}

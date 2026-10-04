using Microsoft.Data.Sqlite;
using System.Data;
using System.Text.RegularExpressions;

/* Asyncronus Read & Write to Sql:
 *      All Sql activity goes through these two functions only! */

namespace StudentExchangeBck
{
    public static class Sql
    {
        // Reader Writer Lock to make SqLite Async
        static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        // Only 'Select' in Read()
        static readonly Regex _validRead = new Regex(@"^\s*select\b", RegexOptions.IgnoreCase);
        // Only 'insert into', 'update', or 'delete from' allowed in write
        static readonly Regex _validWrite = new Regex(@"^(insert\s+into|update|delete\s+from)\b", RegexOptions.IgnoreCase);

        /// <summary>
        /// Async Read Sql Statement: 'SELECT' only
        /// </summary>
        /// <param name="sql">Read Sql Statement</param>
        /// <param name="args">Args to input into sql statement</param>
        /// <returns>Dictionary data[column][irows]</returns>
        /// <exception cref="ArgumentException"></exception>
        public static Dictionary<string, List<object>> Read(string sql, Dictionary<string, object>? args = null)
        {
            // Validate correct statement
            if (!_validRead.IsMatch(sql.Trim()))
                throw new ArgumentException($"Only SELECT SQL statements are allowed in Sql.Read(): {sql}.");
            try
            {
                // Async lock
                _lock.EnterReadLock();
                // Connect Sql
                using (var con = SqLiteConnect())
                {
                    // Open connection if need be
                    if (con.State == ConnectionState.Closed) con.Open();

                    // Sql Command
                    var com = con.CreateCommand();
                    com.CommandText = sql;

                    // Add Sql Args
                    if (args != null)
                        foreach (var s in args)
                            com.Parameters.AddWithValue(s.Key, s.Value);

                    List<string>? keys = null;
                    var ls = new List<List<object>>();

                    // Read data
                    var reader = com.ExecuteReader();
                    while (reader.Read())
                    {
                        if (keys == null)
                            keys = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                        ls.Add(keys.Select(k => reader[k]).ToList());
                    }
                    reader.Close();

                    // If nothing return empty dictionary
                    if (keys == null) return new Dictionary<string, List<object>>();

                    // Return values Data[column_name][irows]
                    ls = Utilz.Transpose(ls).ToList();
                    return Enumerable.Range(0, keys.Count).ToDictionary(i => keys[i], i => ls[i]);
                }
            }
            finally { _lock.ExitReadLock(); }
        }

        /// <summary>
        /// Async Write Sql Statement: 'INSERT', 'UPDATE', or 'DELETE' only
        /// </summary>
        /// <param name="sql">Write Sql Statement</param>
        /// <param name="args">Args in sql statement</param>
        /// <param name="updatedRows">Check number of rows. Specified:: -1: rows>0, -2: don't check rows</param>
        /// <exception cref="ArgumentException"></exception>
        public static void Write(string sql, Dictionary<string, object>? args = null, int updatedRows = -1)
        {
            // Validate Write Sql statement
            if (!_validWrite.IsMatch(sql.Trim().ToLower()))
                throw new ArgumentException($"Invalid SQL statement for Sql.Write(): {sql}.");
            SqliteTransaction? trans = null;        // Sql transaction
            int cnt = 0;                            // Rows affected
            try
            {
                // Async write lock
                _lock.EnterWriteLock();
                // Connect to SqLite
                using (var con = SqLiteConnect())
                {
                    // Open connection if closed
                    if (con.State == ConnectionState.Closed) con.Open();

                    // Sql Command
                    trans = con.BeginTransaction();
                    var com = con.CreateCommand();
                    com.CommandText = sql;
                    com.Transaction = trans;

                    // Add Sql args
                    if (args != null)
                        foreach (var s in args)
                            com.Parameters.AddWithValue(s.Key, s.Value);

                    // Excute non Query
                    cnt = com.ExecuteNonQuery();
                    // Validate affected rows
                    if (cnt != updatedRows && !new[] { -1, -2 }.Contains(updatedRows))
                        throw new Exception($"cnt({cnt}) != updatedRows({updatedRows})");
                    else if (cnt < 1 && updatedRows == -1)
                        throw new Exception($"cnt({cnt}) < 1");

                    // Transaction Commit: if 1+ rows affectted
                    if (cnt != 0)
                        trans.Commit();
                }
            }
            catch
            {
                // There was an error: Disregard changes if rows affected
                if (cnt != 0)
                    trans?.Rollback();
                throw;
            }
            finally { _lock.ExitWriteLock(); }
        }

        // SqLite Path
#if DEBUG
        public static readonly string _path = Path.GetFullPath("bin_/Data.db");
#else
        public static readonly string _path = Path.GetFullPath("/home/bin_/Data.db");
#endif
        // SqLite Connection
        // !!! Needs to be a private method !!!
        static SqliteConnection SqLiteConnect()
            => new SqliteConnection($"Data Source={_path};");
    }
}

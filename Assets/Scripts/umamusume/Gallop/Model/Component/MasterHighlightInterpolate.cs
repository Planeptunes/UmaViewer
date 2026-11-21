using System.Collections.Generic;
using Mono.Data.Sqlite;

namespace Gallop
{
    /// <summary>
    /// Port of Gallop.MasterHighlightInterpolate (dump_jp TypeDefIndex 33555775; Get @RVA 0x3A1E160, _SelectOne @0x3A1E3E0).
    /// Table highlight_interpolate (id, in_time, out_time); rows are cached by id and unknown ids are remembered in
    /// _notFounds, exactly like the game's Get. Times are integers in units of 1e-4 s (RaceUtil.FinishTimeInt2Float,
    /// RVA 0x204B9B0, factor 9.9999997e-05).
    /// </summary>
    public sealed class MasterHighlightInterpolate
    {
        public const string TABLE_NAME = "highlight_interpolate";
        private const float FINISH_TIME_INT_TO_FLOAT = 9.9999997e-05f;

        public sealed class HighlightInterpolate
        {
            public int Id;
            public int InTime;
            public int OutTime;
        }

        private readonly SqliteConnection _db;
        private HashSet<int> _notFounds;
        private readonly Dictionary<int, HighlightInterpolate> _lazyPrimaryKeyDictionary = new Dictionary<int, HighlightInterpolate>();

        public MasterHighlightInterpolate(SqliteConnection db)
        {
            _db = db;
        }

        public HighlightInterpolate Get(int id)
        {
            if (_lazyPrimaryKeyDictionary.TryGetValue(id, out var cached)) return cached;
            if (_db == null) return null;
            if (_notFounds != null && _notFounds.Contains(id)) return null;

            HighlightInterpolate row = SelectOne(id);
            if (row != null)
            {
                _lazyPrimaryKeyDictionary[id] = row;
                return row;
            }
            if (_notFounds == null) _notFounds = new HashSet<int>();
            _notFounds.Add(id);
            return null;
        }

        // _SelectOne: SELECT in_time,out_time FROM highlight_interpolate WHERE id=?
        private HighlightInterpolate SelectOne(int id)
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT in_time,out_time FROM highlight_interpolate WHERE id=" + id;
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new HighlightInterpolate { Id = id, InTime = reader.GetInt32(0), OutTime = reader.GetInt32(1) };
                }
            }
        }

        /// <summary>EyeHighlightController.GetInterpolateTime @0x51FFA20: (in, out) seconds, 0 when the row is missing.</summary>
        public void GetInterpolateTime(int id, out float inSec, out float outSec)
        {
            HighlightInterpolate row = Get(id);
            if (row == null)
            {
                inSec = 0f;
                outSec = 0f;
                return;
            }
            inSec = row.InTime * FINISH_TIME_INT_TO_FLOAT;
            outSec = row.OutTime * FINISH_TIME_INT_TO_FLOAT;
        }

        public void Unload()
        {
            _lazyPrimaryKeyDictionary.Clear();
            _notFounds = null;
        }
    }
}

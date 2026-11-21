using System.Collections.Generic;
using Mono.Data.Sqlite;

namespace Gallop
{
    /// <summary>
    /// Port of Gallop.MasterRandomEarTailMotion (code_JP Gallop.MasterRandomEarTailMotion.c). Table
    /// random_ear_tail_motion(id, parts_type, motion_name, ear_type, use_story, use_theater).
    /// GetEarList @0x7ffd57c85330 = GetListWithPartsType(1) @0x7ffd57c85340: rows are queried once per parts type and
    /// cached (_lazyPrimaryKeyDictionary +0x20 by id, the list cache +0x28 by parts type); _CreateOrmByQueryResultWithPartsType
    /// @0x7ffd57c85a60 reads (0) id, (1) motion_name, (2) ear_type, (3) use_story == 1, (4) use_theater == 1.
    /// parts_type 1 = ear rows, 2 = tail rows. Rows come in id order (the game dictionary lists are in id order);
    /// the game's SELECT text is not recoverable from code_JP, so the order is made explicit with ORDER BY id.
    /// </summary>
    public sealed class MasterRandomEarTailMotion
    {
        public const string TABLE_NAME = "random_ear_tail_motion";
        public const int PARTS_TYPE_EAR = 1;
        public const int PARTS_TYPE_TAIL = 2;

        // Gallop.RandomEarTailMotion (object layout: +0x10 id, +0x14 PartsType, +0x18 MotionName, +0x20 EarType,
        // +0x24 UseStory, +0x25 UseTheater).
        public sealed class RandomEarTailMotion
        {
            public int Id;
            public int PartsType;
            public string MotionName;
            public int EarType;
            public bool UseStory;
            public bool UseTheater;
        }

        private readonly SqliteConnection _db;
        private readonly Dictionary<int, List<RandomEarTailMotion>> _listWithPartsType = new Dictionary<int, List<RandomEarTailMotion>>();

        public MasterRandomEarTailMotion(SqliteConnection db)
        {
            _db = db;
        }

        // GetEarList @0x7ffd57c85330.
        public List<RandomEarTailMotion> GetEarList()
        {
            return GetListWithPartsType(PARTS_TYPE_EAR);
        }

        // GetListWithPartsType @0x7ffd57c85340.
        public List<RandomEarTailMotion> GetListWithPartsType(int partsType)
        {
            if (_listWithPartsType.TryGetValue(partsType, out var cached)) return cached;
            var list = new List<RandomEarTailMotion>();
            if (_db != null)
            {
                using (var cmd = _db.CreateCommand())
                {
                    cmd.CommandText = "SELECT id,motion_name,ear_type,use_story,use_theater FROM " + TABLE_NAME
                        + " WHERE parts_type=" + partsType + " ORDER BY id";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new RandomEarTailMotion
                            {
                                Id = reader.GetInt32(0),
                                PartsType = partsType,
                                MotionName = reader.GetString(1),
                                EarType = reader.GetInt32(2),
                                UseStory = reader.GetInt32(3) == 1,
                                UseTheater = reader.GetInt32(4) == 1,
                            });
                        }
                    }
                }
            }
            _listWithPartsType[partsType] = list;
            return list;
        }

        // Unload @0x7ffd57c859f0: clears both caches.
        public void Unload()
        {
            _listWithPartsType.Clear();
        }
    }
}

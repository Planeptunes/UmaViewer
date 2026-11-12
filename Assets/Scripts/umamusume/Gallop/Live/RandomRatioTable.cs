using UnityEngine;

namespace Gallop.Live
{
    // Port of Gallop.Live.RandomRatioTable (JP Generate 0x7ffd592db310 / Get 0x7ffd592db490).
    // The game builds one table per Live (Director: Generate(10, 64, 0)) and every blink light container reads its
    // random wait / interval / H-S-V ratios from it. The seed is 0 there, so the native does not call InitState and the
    // values come from the global UnityEngine.Random: they change from run to run, but stay fixed during one Live.
    public sealed class RandomRatioTable
    {
        public const int LiveMajorLength = 10;
        public const int LiveMinorLength = 64;

        private static RandomRatioTable sLive;

        // Native: static field lazily created by Director, regenerated once when the Live is set up.
        public static RandomRatioTable Live
        {
            get
            {
                if (sLive == null)
                {
                    sLive = new RandomRatioTable();
                    sLive.Generate(LiveMajorLength, LiveMinorLength, 0);
                }
                return sLive;
            }
        }

        // Called when a Live is loaded so each run draws a new table, like the game does.
        public static void ResetLive()
        {
            sLive = null;
        }

        private int _randomSeed;
        private float[,] _randomValueArray;
        private int _majorLength;
        private int _minorLength;

        public void Generate(int majorLength, int minorLength, int seed)
        {
            _majorLength = majorLength < 2 ? 1 : majorLength;
            _minorLength = minorLength < 2 ? 1 : minorLength;
            _randomValueArray = new float[_majorLength, _minorLength];
            _randomSeed = seed;
            if (_randomSeed != 0)
                Random.InitState(_randomSeed);

            for (int i = 0; i < _majorLength; i++)
                for (int j = 0; j < _minorLength; j++)
                    _randomValueArray[i, j] = Random.Range(0.0f, 1.0f);
        }

        public float Get(int major, int minor)
        {
            int a;
            int b;
            if (major < 0 || minor < 0)
            {
                a = Random.Range(0, _majorLength);
                b = Random.Range(0, _minorLength);
            }
            else
            {
                a = major % _majorLength;
                b = minor % _minorLength;
            }
            return _randomValueArray[a, b];
        }
    }
}

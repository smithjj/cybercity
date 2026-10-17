namespace NeonKaido
{
    /// Bit-exact port of the mulberry32 generator used throughout the JS game
    /// (defined per-file, e.g. src/systems/atmosphere/environment.js).
    /// JS:  a |= 0; a = (a + 0x6d2b79f5) | 0;
    ///      t = Math.imul(a ^ (a >>> 15), 1 | a);
    ///      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    ///      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    /// uint arithmetic in C# wraps mod 2^32 exactly like JS |0 / >>> semantics.
    public struct Mulberry32
    {
        uint _a;
        public Mulberry32(uint seed) { _a = seed; }

        public double Next()
        {
            unchecked
            {
                _a += 0x6D2B79F5u;
                uint t = (_a ^ (_a >> 15)) * (1u | _a);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }
}

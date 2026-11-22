using System;

namespace EmberwardCoop.Core
{
    // Re-entrant guard counters to suppress Harmony patch re-broadcasts while applying remote state.
    public static class NetworkScope
    {
        private static int _applyDepth;
        private static int _metaSaveDepth;

        public static bool IsApplying => _applyDepth > 0;
        public static bool IsClientMetaSaveAllowed => _metaSaveDepth > 0;

        public readonly struct ScopeToken : IDisposable
        {
            private readonly bool _isMeta;
            public ScopeToken(bool isMeta) => _isMeta = isMeta;

            public void Dispose()
            {
                if (_isMeta)
                {
                    if (_metaSaveDepth > 0) _metaSaveDepth--;
                }
                else
                {
                    if (_applyDepth > 0) _applyDepth--;
                }
            }
        }

        public static ScopeToken Enter()
        {
            _applyDepth++;
            return new ScopeToken(false);
        }

        public static ScopeToken EnterClientMetaSave()
        {
            _metaSaveDepth++;
            return new ScopeToken(true);
        }

        public static void Reset()
        {
            _applyDepth = 0;
            _metaSaveDepth = 0;
        }
    }
}

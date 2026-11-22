namespace EmberwardCoop.Core
{
    public static class CoopInputGuard
    {
        public static bool IsEmoteWheelOpen;
        public static bool IsPingWheelOpen;
        public static bool IsMazeSketching;
        public static bool IsCoopModalOpen;

        public static bool IsCapturingWorldInput =>
            IsEmoteWheelOpen || IsPingWheelOpen || IsMazeSketching || IsCoopModalOpen;

        public static void Reset()
        {
            IsEmoteWheelOpen = false;
            IsPingWheelOpen = false;
            IsMazeSketching = false;
            IsCoopModalOpen = false;
        }
    }
}

using EmberwardCoop.UI;
using UnityEngine;

namespace EmberwardCoop.Presence
{
    public enum StickerId : byte
    {
        ThumbsUp = 0,
        Heart = 1,
        Sweat = 2,
        MindBlown = 3,
        Cry = 4,
        Crown = 5,
        Coin = 6,
        Fire = 7,
        Clock = 8,
        Alert = 9
    }

    public static class NativeStickerCatalog
    {
        public static Sprite GetArtworkSprite(StickerId id)
            => NativeAssetRegistry.GetStickerIconSprite(id);

        public static Sprite GetMedallionSprite()
            => NativeAssetRegistry.GetStickerMedallionSprite();

        public static Color GetAccentColor(StickerId id) => id switch
        {
            StickerId.ThumbsUp  => new Color(0.36f, 0.90f, 0.50f, 1f),
            StickerId.Heart     => new Color(0.98f, 0.42f, 0.58f, 1f),
            StickerId.Sweat     => new Color(0.42f, 0.80f, 0.98f, 1f),
            StickerId.MindBlown => new Color(0.82f, 0.52f, 0.98f, 1f),
            StickerId.Cry       => new Color(0.54f, 0.72f, 0.96f, 1f),
            StickerId.Crown     => new Color(0.98f, 0.82f, 0.28f, 1f),
            StickerId.Coin      => new Color(0.96f, 0.78f, 0.24f, 1f),
            StickerId.Fire      => new Color(0.98f, 0.48f, 0.18f, 1f),
            StickerId.Clock     => new Color(0.74f, 0.86f, 0.96f, 1f),
            StickerId.Alert     => new Color(0.96f, 0.30f, 0.28f, 1f),
            _                   => NativeStyleProvider.EmberGold
        };
    }
}

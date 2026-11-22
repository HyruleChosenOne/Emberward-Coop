using UnityEngine;

namespace EmberwardCoop.UI
{
    public enum PanelSurfaceRole
    {
        Window,
        SubPanel,
        Card,
        Well,
        Hud,
        Tooltip,
        Chat,
        CustomAccent
    }

    public static class EmberwardTheme
    {
        // Warm slate-brown and aged-stone tints for white-tintable 9-slice sprites (UISprite, Background).
        public static readonly Color WindowSurface    = new Color(0.27f, 0.23f, 0.20f, 0.98f);
        public static readonly Color SubPanelSurface  = new Color(0.31f, 0.26f, 0.23f, 0.96f);
        public static readonly Color WellSurface      = new Color(0.19f, 0.16f, 0.14f, 0.96f);
        public static readonly Color CardSurface      = new Color(0.37f, 0.31f, 0.27f, 0.97f);
        public static readonly Color CardHoverSurface = new Color(0.45f, 0.38f, 0.32f, 0.98f);
        public static readonly Color HudSurface       = new Color(0.26f, 0.22f, 0.19f, 0.94f);
        public static readonly Color TooltipSurface   = new Color(0.28f, 0.23f, 0.20f, 0.97f);
        public static readonly Color ChatSurface      = new Color(0.21f, 0.18f, 0.16f, 0.88f);
        public static readonly Color ModalScrim       = new Color(0.08f, 0.06f, 0.05f, 0.62f);
        public static readonly Color WheelScrim       = new Color(0.08f, 0.06f, 0.05f, 0.26f);

        // Near-white multipliers for pre-colored sprites (DialogBox_v3_Brown, DialogBox_v3_Sand) to avoid darkening baked interiors.
        public static readonly Color PreColoredWindow   = new Color(1.00f, 0.98f, 0.95f, 0.98f);
        public static readonly Color PreColoredSubPanel = new Color(0.96f, 0.93f, 0.90f, 0.97f);
        public static readonly Color PreColoredCard     = new Color(1.00f, 0.97f, 0.93f, 0.97f);
        public static readonly Color PreColoredWell     = new Color(0.86f, 0.82f, 0.78f, 0.96f);
        public static readonly Color PreColoredHud      = new Color(1.00f, 0.97f, 0.93f, 0.95f);
        public static readonly Color PreColoredTooltip  = new Color(1.00f, 0.97f, 0.93f, 0.97f);
        public static readonly Color PreColoredChat     = new Color(0.94f, 0.90f, 0.86f, 0.88f);

        public static readonly Color EmberGold       = new Color(1.00f, 0.79f, 0.31f, 1.00f);
        public static readonly Color EmberGoldSoft   = new Color(0.92f, 0.75f, 0.40f, 0.95f);
        public static readonly Color EmberAmber      = new Color(0.93f, 0.51f, 0.18f, 1.00f);
        public static readonly Color BronzeBorder    = new Color(0.76f, 0.56f, 0.28f, 0.92f);
        public static readonly Color BronzeSubtle    = new Color(0.60f, 0.45f, 0.26f, 0.70f);
        public static readonly Color DividerWarm     = new Color(0.78f, 0.60f, 0.33f, 0.42f);

        public static readonly Color ButtonPrimary   = new Color(0.84f, 0.44f, 0.16f, 1.00f);
        public static readonly Color ButtonSecondary = new Color(0.45f, 0.38f, 0.33f, 1.00f);
        public static readonly Color ButtonSuccess   = new Color(0.27f, 0.58f, 0.33f, 1.00f);
        public static readonly Color ButtonDanger    = new Color(0.72f, 0.25f, 0.22f, 1.00f);
        public static readonly Color TabActive       = new Color(0.84f, 0.46f, 0.18f, 1.00f);
        public static readonly Color TabInactive     = new Color(0.39f, 0.33f, 0.29f, 1.00f);

        public static readonly Color TextParchment   = new Color(0.98f, 0.94f, 0.86f, 1.00f);
        public static readonly Color TextSand        = new Color(0.81f, 0.74f, 0.64f, 1.00f);
        public static readonly Color TextShadowBrown = new Color(0.13f, 0.08f, 0.05f, 0.80f);

        public static readonly Color StatusReady     = new Color(0.48f, 0.88f, 0.55f, 1.00f);
        public static readonly Color StatusWarn      = new Color(0.97f, 0.75f, 0.32f, 1.00f);
        public static readonly Color StatusBad       = new Color(0.93f, 0.40f, 0.36f, 1.00f);

        public static readonly Color PingGood        = new Color(0.52f, 0.88f, 0.58f, 1.00f);
        public static readonly Color PingWarn        = new Color(0.96f, 0.76f, 0.34f, 1.00f);
        public static readonly Color PingBad         = new Color(0.92f, 0.44f, 0.40f, 1.00f);

        public static readonly Color ModeSharedTint   = new Color(0.80f, 0.52f, 0.20f, 0.96f);
        public static readonly Color ModeSeparateTint = new Color(0.26f, 0.56f, 0.72f, 0.96f);

        public static readonly Color SelectedTileBg   = new Color(0.56f, 0.42f, 0.24f, 0.98f);
        public static readonly Color HostBadgeBg      = new Color(0.52f, 0.36f, 0.16f, 0.95f);

        public static Color GetPingColor(int pingMs)
        {
            if (pingMs < 70) return PingGood;
            if (pingMs <= 140) return PingWarn;
            return PingBad;
        }

        public static Color ResolveSpriteTint(Sprite sprite, Color requestedTint, PanelSurfaceRole role)
        {
            if (!NativeAssetRegistry.IsPreColoredSprite(sprite))
                return requestedTint;

            float alpha = requestedTint.a;
            return role switch
            {
                PanelSurfaceRole.Window   => new Color(PreColoredWindow.r, PreColoredWindow.g, PreColoredWindow.b, alpha),
                PanelSurfaceRole.SubPanel => new Color(PreColoredSubPanel.r, PreColoredSubPanel.g, PreColoredSubPanel.b, alpha),
                PanelSurfaceRole.Card     => new Color(PreColoredCard.r, PreColoredCard.g, PreColoredCard.b, alpha),
                PanelSurfaceRole.Well     => new Color(PreColoredWell.r, PreColoredWell.g, PreColoredWell.b, alpha),
                PanelSurfaceRole.Hud      => new Color(PreColoredHud.r, PreColoredHud.g, PreColoredHud.b, alpha),
                PanelSurfaceRole.Tooltip  => new Color(PreColoredTooltip.r, PreColoredTooltip.g, PreColoredTooltip.b, alpha),
                PanelSurfaceRole.Chat     => new Color(PreColoredChat.r, PreColoredChat.g, PreColoredChat.b, alpha),
                _ => new Color(
                    Mathf.Lerp(0.76f, 1.0f, requestedTint.r),
                    Mathf.Lerp(0.72f, 1.0f, requestedTint.g),
                    Mathf.Lerp(0.68f, 1.0f, requestedTint.b),
                    alpha)
            };
        }
    }
}

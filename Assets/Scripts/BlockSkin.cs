using System;
using System.Collections.Generic;
using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Everything the game looks like: block sprites and their colours, the board's own
/// pieces, and the effect sprites.
///
/// Split from <see cref="ShapeCatalog"/> because the two answer different questions --
/// the catalog decides *what shapes appear and how often*, this decides *what they look
/// like*. Keeping them apart means a reskin never risks disturbing spawn tuning.
///
/// Blocks are one pre-shaded sprite per colour rather than a single white sprite tinted
/// at runtime. A multiply tint can only darken, so it cannot reproduce the lit band
/// across the top of a block without a second overlay layer on every cell.
/// </summary>
[CreateAssetMenu(fileName = "BlockSkin", menuName = "Block Blast/Block Skin", order = 1)]
public class BlockSkin : ScriptableObject
{
    /// <summary>Where the game looks when nothing is wired up in the Inspector.</summary>
    public const string ResourcesPath = "BlockSkin";

    [Serializable]
    public struct BlockEntry
    {
        [Tooltip("The colour written into the board. Also what a save file stores.")]
        public Color color;

        [Tooltip("Pre-shaded sprite for this colour.")]
        public Sprite sprite;
    }

    [Header("Blocks")]
    [SerializeField] private BlockEntry[] blocks = Array.Empty<BlockEntry>();

    [Header("Board")]
    [SerializeField] private Sprite emptyCell;
    [Tooltip("The playfield's dark ground. Separate from the edge because the edge is " +
             "flared on a clear, and a 9-sliced sprite stretched over the whole board " +
             "carries its interior with it -- flaring one sprite lit the entire board.")]
    [SerializeField] private Sprite boardPlate;

    [Tooltip("The lit tube around the playfield, transparent inside.")]
    [SerializeField] private Sprite boardEdge;

    [Header("Effects")]
    [SerializeField] private Sprite glow;
    [SerializeField] private Sprite particle;
    [SerializeField] private Sprite star;

    [Tooltip("Crown beside the high score. White, so the UI tints it.")]
    [SerializeField] private Sprite crown;

    [Tooltip("Bright ring used for emphasis only -- the ghost footprint and the rows a " +
             "drop would clear. Deliberately louder than a resting tile so there is " +
             "somewhere to escalate to.")]
    [SerializeField] private Sprite blockOutline;

    [Tooltip("Material every sprite in the game shares. Unlit: there are no 2D lights, so " +
             "the lit default costs an extra pass and splits batches for nothing.")]
    [SerializeField] private Material spriteMaterial;

    [Header("UI")]
    [SerializeField] private Sprite panelSprite;
    [Tooltip("Neon edge drawn over the panel body. Separate so the body can stay a " +
             "neutral dark while the rim carries the accent hue.")]
    [SerializeField] private Sprite panelRimSprite;
    [SerializeField] private Sprite buttonSprite;
    [SerializeField] private Sprite bestScoreBackground;
    [SerializeField] private Sprite gearIcon;
    [SerializeField] private Sprite closeIcon;
    [SerializeField] private Sprite playIcon;
    [SerializeField] private Sprite soundIcon;
    [SerializeField] private Sprite musicIcon;
    [SerializeField] private Sprite replayIcon;
    [SerializeField] private Sprite slashIcon;
    [SerializeField] private Sprite vibrateIcon;

    [Header("Board Colours")]
    [SerializeField] private Color emptyCellColor = new Color32(22, 34, 66, 255);
    [SerializeField] private Color boardBorderColor = new Color32(34, 59, 118, 255);
    [SerializeField] private Color pageBackgroundColor = new Color32(42, 76, 141, 255);

    [Tooltip("Colour the cleared rows flash before they shatter.")]
    [SerializeField] private Color clearFlashColor = new Color32(90, 214, 255, 255);

    [Tooltip("Gold used for the crown and the high score.")]
    [SerializeField] private Color accentColor = new Color32(255, 181, 0, 255);

    [Tooltip("Warm halo behind the points in the praise popup.")]
    [SerializeField] private Color popupGlowColor = new Color32(255, 128, 40, 255);

    // Built lazily. Colours always originate from this table, so exact packed equality is
    // a safe key -- there is no float comparison to go wrong.
    private Dictionary<uint, Sprite> spriteByColor;

    public int BlockCount => blocks?.Length ?? 0;

    public Sprite EmptyCell => emptyCell;
    public Sprite BoardPlate => boardPlate;
    public Sprite BoardEdge => boardEdge;
    public Sprite Glow => glow;
    public Sprite Particle => particle;
    public Sprite Star => star;
    public Sprite Crown => crown;
    public Sprite BlockOutline => blockOutline;

    /// <summary>Shared sprite material, or null to leave Unity's default in place.</summary>
    public Material SpriteMaterial => spriteMaterial;

    public Sprite PanelSprite => panelSprite;
    public Sprite PanelRimSprite => panelRimSprite;
    public Sprite ButtonSprite => buttonSprite;
    public Sprite BestScoreBackground => bestScoreBackground;
    public Sprite GearIcon => gearIcon;
    public Sprite CloseIcon => closeIcon;
    public Sprite PlayIcon => playIcon;
    public Sprite SoundIcon => soundIcon;
    public Sprite MusicIcon => musicIcon;
    public Sprite ReplayIcon => replayIcon;
    public Sprite SlashIcon => slashIcon;
    public Sprite VibrateIcon => vibrateIcon;

    public Color EmptyCellColor => emptyCellColor;
    public Color BoardBorderColor => boardBorderColor;
    public Color PageBackgroundColor => pageBackgroundColor;
    public Color ClearFlashColor => clearFlashColor;
    public Color AccentColor => accentColor;
    public Color PopupGlowColor => popupGlowColor;

    public Color ColorAt(int index)
    {
        if (BlockCount == 0) return Color.white;
        return blocks[((index % blocks.Length) + blocks.Length) % blocks.Length].color;
    }

    public Sprite SpriteAt(int index)
    {
        if (BlockCount == 0) return null;
        return blocks[((index % blocks.Length) + blocks.Length) % blocks.Length].sprite;
    }

    /// <summary>
    /// The sprite for a colour already on the board. Returns null for a colour this skin
    /// does not define, which the caller should treat as "fall back to a tinted square"
    /// rather than as an error -- a save written under a different skin lands here.
    /// </summary>
    public Sprite SpriteFor(Color color)
    {
        if (spriteByColor == null)
        {
            spriteByColor = new Dictionary<uint, Sprite>(BlockCount);
            for (int i = 0; i < BlockCount; i++)
            {
                uint key = ColorPacking.Pack(blocks[i].color);
                if (!spriteByColor.ContainsKey(key)) spriteByColor[key] = blocks[i].sprite;
            }
        }

        return spriteByColor.TryGetValue(ColorPacking.Pack(color), out Sprite sprite) ? sprite : null;
    }

    private void OnDisable() => spriteByColor = null;

#if UNITY_EDITOR
    private void OnValidate()
    {
        spriteByColor = null;

        if (BlockCount == 0)
            Debug.LogWarning($"[{name}] No block entries; pieces will render as untextured squares.", this);
    }
#endif
}
